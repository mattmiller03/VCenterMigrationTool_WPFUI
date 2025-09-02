using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages persistent external PowerShell processes with active vCenter connections
/// </summary>
public class PersistantVcenterConnectionService : IDisposable
    {
    private readonly ILogger<PersistantVcenterConnectionService> _logger;
    private readonly ConcurrentDictionary<string, PersistentConnection> _connections = new();
    private bool _disposed = false;

    public class PersistentConnection
        {
        public Process Process { get; set; }
        public StreamWriter StandardInput { get; set; }
        public VCenterConnection ConnectionInfo { get; set; }
        public DateTime ConnectedAt { get; set; }
        public string SessionId { get; set; }
        public string VCenterVersion { get; set; }
        public bool IsConnected { get; set; }
        public TaskCompletionSource<string> CurrentCommand { get; set; }
        public StringBuilder OutputBuffer { get; set; } = new StringBuilder();
        public object LockObject { get; set; } = new object();
        }

    public PersistantVcenterConnectionService (ILogger<PersistantVcenterConnectionService> logger)
        {
        _logger = logger;
        }

    /// <summary>
    /// Establishes a persistent connection using an external PowerShell process
    /// </summary>
    public async Task<(bool success, string message, string sessionId)> ConnectAsync (
        VCenterConnection connectionInfo,
        string password,
        bool isSource = true,
        bool bypassModuleCheck = false)
        {
        var connectionKey = isSource ? "source" : "target";

        try
            {
            _logger.LogInformation("Establishing persistent external connection to {Server} ({Type})",
                connectionInfo.ServerAddress, connectionKey);

            // Disconnect existing connection if any
            await DisconnectAsync(connectionKey);

            // Start a new PowerShell process
            var process = StartPersistentPowerShellProcess();

            if (process == null)
                {
                return (false, "Failed to start PowerShell process", null);
                }

            var connection = new PersistentConnection
                {
                Process = process,
                StandardInput = process.StandardInput,
                ConnectionInfo = connectionInfo,
                ConnectedAt = DateTime.Now
                };

            // Set up output handling
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    {
                    lock (connection.LockObject)
                        {
                        connection.OutputBuffer.AppendLine(e.Data);
                        _logger.LogDebug("PS Output: {Output}", e.Data);
                        }
                    }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    {
                    _logger.LogWarning("PS Error: {Error}", e.Data);
                    }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Store the connection early so we can use ExecuteCommandAsync
            _connections[connectionKey] = connection;

            // Import PowerCLI modules unless explicitly bypassed
            if (bypassModuleCheck)
            {
                _logger.LogInformation("Skipping PowerCLI module import due to bypassModuleCheck=true");
                _logger.LogWarning("PowerCLI functionality will be limited - only basic PowerShell commands available");
                
                // Still store connection info for basic PowerShell operations
                connection.IsConnected = true;
                connection.SessionId = $"bypass-{Guid.NewGuid():N}";
                connection.VCenterVersion = "Unknown (bypass mode)";
                _connections[connectionKey] = connection;
                
                return (true, "Connected in bypass mode - PowerCLI modules skipped", connection.SessionId);
            }

            _logger.LogInformation("Importing PowerCLI modules in persistent session...");
            var importResult = await ExecuteCommandWithTimeoutAsync(connectionKey, @"
                Write-Output 'DIAGNOSTIC: Starting PowerCLI module import...'
                
                # Check for both current and future PowerCLI modules
                $legacyPowerCLI = Get-Module -Name VMware.PowerCLI -ListAvailable -ErrorAction SilentlyContinue
                $vcfPowerCLI = Get-Module -Name VCF.PowerCLI -ListAvailable -ErrorAction SilentlyContinue
                $anyVMwareModules = Get-Module -Name VMware* -ListAvailable -ErrorAction SilentlyContinue
                $anyVCFModules = Get-Module -Name VCF* -ListAvailable -ErrorAction SilentlyContinue
                
                Write-Output ""DIAGNOSTIC: Legacy PowerCLI (VMware.PowerCLI) found: $(if ($legacyPowerCLI) { $legacyPowerCLI.Version -join ', ' } else { 'None' })""
                Write-Output ""DIAGNOSTIC: VCF PowerCLI (VCF.PowerCLI) found: $(if ($vcfPowerCLI) { $vcfPowerCLI.Version -join ', ' } else { 'None' })""
                
                if (-not $legacyPowerCLI -and -not $vcfPowerCLI) {
                    Write-Output 'DIAGNOSTIC: No PowerCLI modules found in available modules'
                    Write-Output 'DIAGNOSTIC: Available VMware modules:'
                    $anyVMwareModules | ForEach-Object { 
                        Write-Output ""DIAGNOSTIC: - $($_.Name) (Version: $($_.Version))""
                    }
                    Write-Output 'DIAGNOSTIC: Available VCF modules:'
                    $anyVCFModules | ForEach-Object { 
                        Write-Output ""DIAGNOSTIC: - $($_.Name) (Version: $($_.Version))""
                    }
                }
                
                # Multiple strategies to handle both legacy and VCF modules
                $importSuccess = $false
                $importError = $null
                $moduleType = 'Unknown'
                
                # Strategy 1: Try VCF.PowerCLI (future-proof for vSphere 9+)
                if ($vcfPowerCLI) {
                    try {
                        Write-Output 'DIAGNOSTIC: Strategy 1 - Attempting VCF.PowerCLI import (vSphere 9+)...'
                        Import-Module VCF.PowerCLI -Force -ErrorAction Stop
                        Write-Output 'DIAGNOSTIC: Strategy 1 - VCF.PowerCLI import successful'
                        $importSuccess = $true
                        $moduleType = 'VCF.PowerCLI'
                    } catch {
                        $importError = $_.Exception.Message
                        Write-Output ""DIAGNOSTIC: Strategy 1 failed - Error: $importError""
                    }
                }
                
                # Strategy 2: Try legacy VMware.PowerCLI if VCF failed or not available
                if (-not $importSuccess -and $legacyPowerCLI) {
                    try {
                        Write-Output 'DIAGNOSTIC: Strategy 2 - Attempting legacy VMware.PowerCLI import...'
                        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                        Write-Output 'DIAGNOSTIC: Strategy 2 - Legacy PowerCLI import successful'
                        $importSuccess = $true
                        $moduleType = 'VMware.PowerCLI'
                    } catch {
                        $importError = $_.Exception.Message
                        Write-Output ""DIAGNOSTIC: Strategy 2 failed - Error: $importError""
                        
                        # Strategy 3: Try importing legacy core modules individually
                        try {
                            Write-Output 'DIAGNOSTIC: Strategy 3 - Attempting legacy core module imports individually...'
                            Import-Module VMware.VimAutomation.Core -Force -ErrorAction Stop
                            Import-Module VMware.VimAutomation.Common -Force -ErrorAction SilentlyContinue
                            Write-Output 'DIAGNOSTIC: Strategy 3 - Legacy core modules imported successfully'
                            $importSuccess = $true
                            $moduleType = 'VMware.VimAutomation.Core'
                        } catch {
                            Write-Output ""DIAGNOSTIC: Strategy 3 failed - Error: $($_.Exception.Message)""
                            
                            # Strategy 4: Try with specific legacy version selection
                            try {
                                Write-Output 'DIAGNOSTIC: Strategy 4 - Attempting newest legacy version import...'
                                $newestModule = Get-Module -Name VMware.PowerCLI -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
                                if ($newestModule) {
                                    Import-Module $newestModule.ModuleBase -Force -ErrorAction Stop
                                    Write-Output ""DIAGNOSTIC: Strategy 4 - Successfully imported PowerCLI version: $($newestModule.Version)""
                                    $importSuccess = $true
                                    $moduleType = ""VMware.PowerCLI v$($newestModule.Version)""
                                } else {
                                    Write-Output 'DIAGNOSTIC: Strategy 4 - No suitable PowerCLI module found'
                                }
                            } catch {
                                Write-Output ""DIAGNOSTIC: Strategy 4 failed - Error: $($_.Exception.Message)""
                            }
                        }
                    }
                }
                
                # Strategy 5: Try VCF core modules if available (future VCF module structure)
                if (-not $importSuccess -and $anyVCFModules) {
                    try {
                        Write-Output 'DIAGNOSTIC: Strategy 5 - Attempting VCF core modules import...'
                        $vcfCoreModules = $anyVCFModules | Where-Object { $_.Name -like '*Core*' -or $_.Name -like '*Common*' }
                        foreach ($module in $vcfCoreModules) {
                            Import-Module $module.Name -Force -ErrorAction SilentlyContinue
                            Write-Output ""DIAGNOSTIC: - Imported $($module.Name)""
                        }
                        if ($vcfCoreModules) {
                            Write-Output 'DIAGNOSTIC: Strategy 5 - VCF core modules imported successfully'
                            $importSuccess = $true
                            $moduleType = 'VCF Core Modules'
                        }
                    } catch {
                        Write-Output ""DIAGNOSTIC: Strategy 5 failed - Error: $($_.Exception.Message)""
                    }
                }
                
                if ($importSuccess) {
                    # Configure PowerCLI (works for both legacy and VCF modules)
                    try {
                        Write-Output ""DIAGNOSTIC: Configuring PowerCLI settings for $moduleType...""
                        
                        # Essential PowerCLI configurations for multiple vCenter connections
                        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                        Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                        
                        # CRITICAL: Enable multiple vCenter server connections (required for migration scenarios)
                        Set-PowerCLIConfiguration -DefaultVIServerMode Multiple -Confirm:$false -Scope Session | Out-Null
                        
                        # Increase web operation timeout for slower vCenter responses
                        Set-PowerCLIConfiguration -WebOperationTimeoutSeconds 300 -Confirm:$false -Scope Session | Out-Null
                        
                        # Configure proxy settings (bypass proxy for internal connections)
                        Set-PowerCLIConfiguration -ProxyPolicy NoProxy -Confirm:$false -Scope Session | Out-Null
                        
                        # Enhanced SSL/TLS configuration for maximum compatibility
                        try {
                            # Force TLS 1.2+ for secure connections
                            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
                            
                            # Disable SSL certificate validation at .NET level
                            [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
                            
                            Write-Output ""DIAGNOSTIC: Enhanced SSL/TLS configuration applied for $moduleType""
                        }
                        catch {
                            Write-Output ""DIAGNOSTIC: Advanced SSL configuration failed for $moduleType, continuing with PowerCLI settings only""
                        }
                        
                        # Verify configuration was applied correctly
                        try {
                            $config = Get-PowerCLIConfiguration
                            Write-Output ""CONFIG_VERIFICATION: InvalidCertificateAction=$($config.InvalidCertificateAction)""
                            Write-Output ""CONFIG_VERIFICATION: DefaultVIServerMode=$($config.DefaultVIServerMode)""
                            Write-Output ""CONFIG_VERIFICATION: WebOperationTimeout=$($config.WebOperationTimeoutSeconds)""
                            Write-Output ""CONFIG_VERIFICATION: ProxyPolicy=$($config.ProxyPolicy)""
                        }
                        catch {
                            Write-Output ""DIAGNOSTIC: Configuration verification failed but continuing""
                        }
                        
                        Write-Output ""DIAGNOSTIC: PowerCLI configuration complete for $moduleType""
                        Write-Output ""MODULES_LOADED:$moduleType""
                    } catch {
                        Write-Output ""DIAGNOSTIC: PowerCLI configuration failed but modules loaded - Error: $($_.Exception.Message)""
                        Write-Output ""MODULES_LOADED:$moduleType""
                    }
                } else {
                    Write-Output ""DIAGNOSTIC: All import strategies failed. Last error: $importError""
                    Write-Output 'DIAGNOSTIC: Consider upgrading to VCF.PowerCLI for vSphere 9+ compatibility'
                    throw ""Failed to import PowerCLI modules after trying multiple strategies: $importError""
                }
            ", TimeSpan.FromSeconds(90), skipConnectionCheck: true);

            if (!importResult.Contains("MODULES_LOADED"))
                {
                _logger.LogError("Failed to load PowerCLI modules in persistent session. Full output: {ImportResult}", importResult);
                await DisconnectAsync(connectionKey);
                return (false, $"Failed to load PowerCLI modules - {importResult}", null);
                }
            
            // Extract and log which module type was successfully loaded
            var moduleTypeMatch = System.Text.RegularExpressions.Regex.Match(importResult, @"MODULES_LOADED:(.+?)(?:\r?\n|$)");
            var loadedModuleType = moduleTypeMatch.Success ? moduleTypeMatch.Groups[1].Value.Trim() : "Unknown";
            _logger.LogInformation("Successfully loaded PowerCLI modules using: {ModuleType}", loadedModuleType);

            // Create credential and connect
            _logger.LogInformation("Connecting to vCenter {Server}...", connectionInfo.ServerAddress);

            var connectScript = $@"
                # ===== ULTRA-VERBOSE POWERCLI CONNECTION DEBUGGING =====
                Write-Output ""DEBUG_START: Beginning PowerCLI connection sequence for {connectionKey}""
                Write-Output ""DEBUG_TIMESTAMP: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
                Write-Output ""DEBUG_POWERSHELL_VERSION: $($PSVersionTable.PSVersion)""
                Write-Output ""DEBUG_EXECUTION_POLICY: $(Get-ExecutionPolicy)""
                
                # Environment diagnostics
                Write-Output ""DEBUG_ENV: Current working directory: $(Get-Location)""
                Write-Output ""DEBUG_ENV: PowerShell process ID: $PID""
                Write-Output ""DEBUG_ENV: Current user: $($env:USERNAME)""
                Write-Output ""DEBUG_ENV: Domain: $($env:USERDOMAIN)""
                
                # Check PowerCLI module availability and status
                Write-Output ""DEBUG_MODULES: Checking PowerCLI module availability""
                try {{
                    $powerCLIModule = Get-Module -Name VMware.PowerCLI -ListAvailable -ErrorAction SilentlyContinue
                    $loadedModules = Get-Module -Name VMware* -ErrorAction SilentlyContinue
                    
                    if ($powerCLIModule) {{
                        Write-Output ""DEBUG_MODULES: PowerCLI module found - version: $($powerCLIModule.Version -join ', ')""
                        Write-Output ""DEBUG_MODULES: Module base path: $($powerCLIModule[0].ModuleBase)""
                    }} else {{
                        Write-Output ""DEBUG_MODULES: PowerCLI module not found in available modules""
                    }}
                    
                    if ($loadedModules) {{
                        Write-Output ""DEBUG_MODULES: Currently loaded VMware modules: $($loadedModules.Name -join ', ')""
                        $loadedModules | ForEach-Object {{
                            Write-Output ""DEBUG_MODULES: - $($_.Name) v$($_.Version) from $($_.ModuleBase)""
                        }}
                    }} else {{
                        Write-Output ""DEBUG_MODULES: No VMware modules currently loaded""
                        Write-Output ""DEBUG_MODULES: Attempting to import core PowerCLI module""
                        try {{
                            Import-Module VMware.VimAutomation.Core -Force -ErrorAction Stop
                            Write-Output ""DEBUG_MODULES: Successfully imported VMware.VimAutomation.Core""
                        }} catch {{
                            Write-Output ""DEBUG_MODULES: Failed to import VMware.VimAutomation.Core: $($_.Exception.Message)""
                        }}
                    }}
                }} catch {{
                    Write-Output ""DEBUG_MODULES: Module check failed: $($_.Exception.Message)""
                }}

                # Check available PowerCLI commands
                Write-Output ""DEBUG_COMMANDS: Checking PowerCLI command availability""
                try {{
                    $connectCmd = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
                    $getServerCmd = Get-Command Get-VIServer -ErrorAction SilentlyContinue
                    $configCmd = Get-Command Set-PowerCLIConfiguration -ErrorAction SilentlyContinue
                    
                    Write-Output ""DEBUG_COMMANDS: Connect-VIServer available: $(if ($connectCmd) {{ 'YES' }} else {{ 'NO' }})""
                    Write-Output ""DEBUG_COMMANDS: Get-VIServer available: $(if ($getServerCmd) {{ 'YES' }} else {{ 'NO' }})""
                    Write-Output ""DEBUG_COMMANDS: Set-PowerCLIConfiguration available: $(if ($configCmd) {{ 'YES' }} else {{ 'NO' }})""
                    
                    if ($connectCmd) {{
                        Write-Output ""DEBUG_COMMANDS: Connect-VIServer source: $($connectCmd.Source)""
                        Write-Output ""DEBUG_COMMANDS: Connect-VIServer version: $($connectCmd.Version)""
                    }}
                }} catch {{
                    Write-Output ""DEBUG_COMMANDS: Command check failed: $($_.Exception.Message)""
                }}

                # PowerCLI configuration diagnostics and setup
                Write-Output ""DEBUG_CONFIG: Applying PowerCLI configuration""
                try {{
                    # Get current configuration before changes
                    $currentConfig = Get-PowerCLIConfiguration -ErrorAction SilentlyContinue
                    if ($currentConfig) {{
                        Write-Output ""DEBUG_CONFIG: Current InvalidCertificateAction: $($currentConfig.InvalidCertificateAction)""
                        Write-Output ""DEBUG_CONFIG: Current DefaultVIServerMode: $($currentConfig.DefaultVIServerMode)""
                        Write-Output ""DEBUG_CONFIG: Current WebOperationTimeout: $($currentConfig.WebOperationTimeoutSeconds)""
                        Write-Output ""DEBUG_CONFIG: Current ProxyPolicy: $($currentConfig.ProxyPolicy)""
                        Write-Output ""DEBUG_CONFIG: Current ParticipateInCEIP: $($currentConfig.ParticipateInCeip)""
                    }} else {{
                        Write-Output ""DEBUG_CONFIG: Unable to get current PowerCLI configuration""
                    }}
                    
                    # Apply essential configurations
                    Write-Output ""DEBUG_CONFIG: Setting InvalidCertificateAction to Ignore""
                    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                    
                    Write-Output ""DEBUG_CONFIG: Setting DefaultVIServerMode to Multiple""
                    Set-PowerCLIConfiguration -DefaultVIServerMode Multiple -Confirm:$false -Scope Session | Out-Null
                    
                    Write-Output ""DEBUG_CONFIG: Setting WebOperationTimeoutSeconds to 300""
                    Set-PowerCLIConfiguration -WebOperationTimeoutSeconds 300 -Confirm:$false -Scope Session | Out-Null
                    
                    Write-Output ""DEBUG_CONFIG: Setting ProxyPolicy to NoProxy""
                    Set-PowerCLIConfiguration -ProxyPolicy NoProxy -Confirm:$false -Scope Session | Out-Null
                    
                    # Verify configuration was applied
                    $verifyConfig = Get-PowerCLIConfiguration
                    Write-Output ""DEBUG_CONFIG: VERIFIED InvalidCertificateAction: $($verifyConfig.InvalidCertificateAction)""
                    Write-Output ""DEBUG_CONFIG: VERIFIED DefaultVIServerMode: $($verifyConfig.DefaultVIServerMode)""
                    Write-Output ""DEBUG_CONFIG: VERIFIED WebOperationTimeout: $($verifyConfig.WebOperationTimeoutSeconds)""
                    Write-Output ""DEBUG_CONFIG: VERIFIED ProxyPolicy: $($verifyConfig.ProxyPolicy)""
                    
                }} catch {{
                    Write-Output ""DEBUG_CONFIG: PowerCLI configuration failed: $($_.Exception.Message)""
                    Write-Output ""DEBUG_CONFIG: Exception type: $($_.Exception.GetType().Name)""
                    Write-Output ""DEBUG_CONFIG: Stack trace: $($_.Exception.StackTrace)""
                }}

                # Credential creation diagnostics
                Write-Output ""DEBUG_CRED: Creating PowerShell credential object""
                try {{
                    Write-Output ""DEBUG_CRED: Target server: {connectionInfo.ServerAddress}""
                    Write-Output ""DEBUG_CRED: Target username: {connectionInfo.Username.Replace("'", "''")}""
                    Write-Output ""DEBUG_CRED: Password length: $(('{password.Replace("'", "''")}'.Length)) characters""
                    
                    $password = '{password.Replace("'", "''")}'
                    Write-Output ""DEBUG_CRED: Password variable created successfully""
                    
                    $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
                    Write-Output ""DEBUG_CRED: SecureString created successfully""
                    
                    $credential = New-Object System.Management.Automation.PSCredential('{connectionInfo.Username.Replace("'", "''")}', $securePassword)
                    Write-Output ""DEBUG_CRED: PSCredential object created successfully""
                    Write-Output ""DEBUG_CRED: Credential username: $($credential.UserName)""
                    Write-Output ""DEBUG_CRED: Credential has password: $(if ($credential.Password) {{ 'YES' }} else {{ 'NO' }})""
                    
                    $password = $null  # Clear from memory
                    Write-Output ""DEBUG_CRED: Password variable cleared from memory""
                }} catch {{
                    Write-Output ""DEBUG_CRED: Credential creation failed: $($_.Exception.Message)""
                    Write-Output ""DEBUG_CRED: Exception type: $($_.Exception.GetType().Name)""
                    return
                }}

                # Pre-connection environment check
                Write-Output ""DEBUG_PRECON: Checking pre-connection environment""
                try {{
                    # Check existing connections
                    $existingConnections = Get-VIServer -ErrorAction SilentlyContinue
                    if ($existingConnections) {{
                        Write-Output ""DEBUG_PRECON: Found $($existingConnections.Count) existing vCenter connections""
                        $existingConnections | ForEach-Object {{
                            Write-Output ""DEBUG_PRECON: - Server: $($_.Name) | Connected: $($_.IsConnected) | Version: $($_.Version) | User: $($_.User)""
                        }}
                    }} else {{
                        Write-Output ""DEBUG_PRECON: No existing vCenter connections found""
                    }}
                    
                    # Network connectivity test
                    Write-Output ""DEBUG_PRECON: Testing network connectivity to {connectionInfo.ServerAddress}:443""
                    $ping = Test-NetConnection -ComputerName '{connectionInfo.ServerAddress}' -Port 443 -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
                    if ($ping) {{
                        Write-Output ""DEBUG_PRECON: Network test - TcpTestSucceeded: $($ping.TcpTestSucceeded)""
                        Write-Output ""DEBUG_PRECON: Network test - RemoteAddress: $($ping.RemoteAddress)""
                        Write-Output ""DEBUG_PRECON: Network test - InterfaceAlias: $($ping.InterfaceAlias)""
                        Write-Output ""DEBUG_PRECON: Network test - PingSucceeded: $($ping.PingSucceeded)""
                    }} else {{
                        Write-Output ""DEBUG_PRECON: Network test failed - no response object""
                    }}
                }} catch {{
                    Write-Output ""DEBUG_PRECON: Pre-connection check failed: $($_.Exception.Message)""
                }}

                # THE ACTUAL CONNECTION ATTEMPT WITH MAXIMUM DIAGNOSTICS
                Write-Output ""DEBUG_CONNECT: ===== STARTING CONNECT-VISERVER ATTEMPT ====""
                Write-Output ""DEBUG_CONNECT: Connection timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
                try {{
                    Write-Output ""DEBUG_CONNECT: Calling Connect-VIServer with parameters:""
                    Write-Output ""DEBUG_CONNECT: - Server: {connectionInfo.ServerAddress}""
                    Write-Output ""DEBUG_CONNECT: - User: {connectionInfo.Username.Replace("'", "''")}""
                    Write-Output ""DEBUG_CONNECT: - Force: True""
                    Write-Output ""DEBUG_CONNECT: - ErrorAction: Stop""
                    
                    # Execute the connection command with verbose error handling
                    Write-Output ""DEBUG_CONNECT: Executing Connect-VIServer now...""
                    $connection = Connect-VIServer -Server '{connectionInfo.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                    Write-Output ""DEBUG_CONNECT: Connect-VIServer command completed without exception""
                    
                    # Analyze the connection result
                    if ($connection) {{
                        Write-Output ""DEBUG_CONNECT: Connection object returned: $(if ($connection) {{ 'YES' }} else {{ 'NO' }})""
                        Write-Output ""DEBUG_CONNECT: Connection type: $($connection.GetType().Name)""
                        Write-Output ""DEBUG_CONNECT: Connection IsConnected: $($connection.IsConnected)""
                        
                        if ($connection.IsConnected) {{
                            Write-Output ""DEBUG_CONNECT: ✅ CONNECTION SUCCESSFUL!""
                            Write-Output ""CONNECTION_SUCCESS""
                            Write-Output ""SESSION_ID:$($connection.SessionId)""
                            Write-Output ""VERSION:$($connection.Version)""
                            Write-Output ""BUILD:$($connection.Build)""
                            Write-Output ""USER:$($connection.User)""
                            Write-Output ""PORT:$($connection.Port)""
                            Write-Output ""PRODUCT_LINE:$($connection.ProductLine)""
                            
                            # Store in global variables
                            $global:VIConnection_{connectionKey.Replace("-", "_")} = $connection
                            Write-Output ""DEBUG_CONNECT: Stored in global variable: VIConnection_{connectionKey.Replace("-", "_")}""
                            
                            # Update default server
                            if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {{
                                $global:PreviousDefaultVIServer = $global:DefaultVIServer
                                Write-Output ""DEBUG_CONNECT: Saved previous default server: $($global:PreviousDefaultVIServer.Name)""
                            }}
                            $global:DefaultVIServer = $connection
                            $global:CurrentVCenterConnection = $connection
                            Write-Output ""DEBUG_CONNECT: Set as new default server""
                            
                            # Verify storage
                            $storedConnection = $global:VIConnection_{connectionKey.Replace("-", "_")}
                            Write-Output ""DEBUG_CONNECT: Verification - stored connection exists: $(if ($storedConnection) {{ 'YES' }} else {{ 'NO' }})""
                            if ($storedConnection) {{
                                Write-Output ""DEBUG_CONNECT: Verification - stored connection IsConnected: $($storedConnection.IsConnected)""
                            }}
                            
                            Write-Output ""CONNECTION_VERIFIED""
                        }} else {{
                            Write-Output ""DEBUG_CONNECT: ❌ Connection object returned but IsConnected = False""
                            Write-Output ""CONNECTION_FAILED: Connection object indicates not connected""
                        }}
                    }} else {{
                        Write-Output ""DEBUG_CONNECT: ❌ No connection object returned""
                        Write-Output ""CONNECTION_FAILED: No connection object returned""
                    }}
                    
                }} catch {{
                    Write-Output ""DEBUG_CONNECT: ❌ CONNECT-VISERVER THREW EXCEPTION""
                    Write-Output ""CONNECTION_FAILED: $($_.Exception.Message)""
                    
                    # Ultra-detailed exception analysis
                    Write-Output ""DEBUG_EXCEPTION: Exception type: $($_.Exception.GetType().FullName)""
                    Write-Output ""DEBUG_EXCEPTION: Exception message: $($_.Exception.Message)""
                    
                    if ($_.Exception.InnerException) {{
                        Write-Output ""DEBUG_EXCEPTION: Inner exception type: $($_.Exception.InnerException.GetType().FullName)""
                        Write-Output ""DEBUG_EXCEPTION: Inner exception message: $($_.Exception.InnerException.Message)""
                    }}
                    
                    Write-Output ""DEBUG_EXCEPTION: Stack trace:""
                    Write-Output $_.Exception.StackTrace
                    
                    # Category analysis
                    Write-Output ""DEBUG_EXCEPTION: Error category: $($_.CategoryInfo.Category)""
                    Write-Output ""DEBUG_EXCEPTION: Error reason: $($_.CategoryInfo.Reason)""
                    Write-Output ""DEBUG_EXCEPTION: Error target name: $($_.CategoryInfo.TargetName)""
                    Write-Output ""DEBUG_EXCEPTION: Error target type: $($_.CategoryInfo.TargetType)""
                    
                    # Specific error pattern analysis
                    $errorMsg = $_.Exception.Message
                    if ($errorMsg -like '*certificate*' -or $errorMsg -like '*SSL*' -or $errorMsg -like '*TLS*') {{
                        Write-Output ""DEBUG_ANALYSIS: 🔒 CERTIFICATE/SSL ISSUE DETECTED""
                        Write-Output ""DEBUG_ANALYSIS: Current InvalidCertificateAction should be 'Ignore'""
                        Write-Output ""DEBUG_ANALYSIS: This may be a PowerCLI SSL bypass failure""
                    }} elseif ($errorMsg -like '*authentication*' -or $errorMsg -like '*login*' -or $errorMsg -like '*credential*' -or $errorMsg -like '*password*') {{
                        Write-Output ""DEBUG_ANALYSIS: 🔑 AUTHENTICATION ISSUE DETECTED""
                        Write-Output ""DEBUG_ANALYSIS: Verify username/password are correct""
                        Write-Output ""DEBUG_ANALYSIS: Check if account is locked or password expired""
                    }} elseif ($errorMsg -like '*timeout*' -or $errorMsg -like '*connection*' -or $errorMsg -like '*network*') {{
                        Write-Output ""DEBUG_ANALYSIS: 🌐 NETWORK/TIMEOUT ISSUE DETECTED""
                        Write-Output ""DEBUG_ANALYSIS: Check network connectivity and firewall settings""
                    }} elseif ($errorMsg -like '*session*' -or $errorMsg -like '*token*') {{
                        Write-Output ""DEBUG_ANALYSIS: 🎫 SESSION/TOKEN ISSUE DETECTED""
                        Write-Output ""DEBUG_ANALYSIS: This may be a vCenter session limit or token issue""
                    }} else {{
                        Write-Output ""DEBUG_ANALYSIS: ❓ UNKNOWN ERROR PATTERN""
                        Write-Output ""DEBUG_ANALYSIS: This may be a new or unusual connection issue""
                    }}
                    
                    # Final diagnostics
                    Write-Output ""DEBUG_FINAL: Connection attempt failed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
                    Write-Output ""DEBUG_FINAL: PowerShell session will remain active for further debugging""
                }}
                
                Write-Output ""DEBUG_END: PowerCLI connection sequence completed for {connectionKey}""
            ";

            var connectResult = await ExecuteCommandWithTimeoutAsync(connectionKey, connectScript, TimeSpan.FromSeconds(120), skipConnectionCheck: true);

            if (connectResult.Contains("CONNECTION_SUCCESS"))
                {
                // Parse connection details
                var lines = connectResult.Split('\n');
                foreach (var line in lines)
                    {
                    if (line.StartsWith("SESSION_ID:"))
                        connection.SessionId = line.Substring(11).Trim();
                    if (line.StartsWith("VERSION:"))
                        connection.VCenterVersion = line.Substring(8).Trim();
                    }

                connection.IsConnected = true;

                _logger.LogInformation("✅ Persistent connection established to {Server} (Session: {SessionId})",
                    connectionInfo.ServerAddress, connection.SessionId);

                return (true, "Connected successfully", connection.SessionId);
                }
            else
                {
                // Ultra-verbose logging of the complete PowerShell output
                _logger.LogWarning("🔍 ULTRA-VERBOSE: Complete PowerShell output for {Server}:", connectionInfo.ServerAddress);
                _logger.LogWarning("Raw output length: {Length} characters", connectResult?.Length ?? 0);
                
                var lines = connectResult.Split('\n');
                _logger.LogWarning("Output contains {LineCount} lines", lines.Length);
                
                // Log all debug lines with appropriate levels
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine)) continue;
                    
                    if (trimmedLine.StartsWith("DEBUG_"))
                    {
                        _logger.LogInformation("PS_DEBUG: {Line}", trimmedLine);
                    }
                    else if (trimmedLine.StartsWith("CONNECTION_FAILED:"))
                    {
                        _logger.LogError("PS_ERROR: {Line}", trimmedLine);
                    }
                    else if (trimmedLine.StartsWith("CONNECTION_SUCCESS"))
                    {
                        _logger.LogInformation("PS_SUCCESS: {Line}", trimmedLine);
                    }
                    else if (trimmedLine.Contains("EXCEPTION") || trimmedLine.Contains("ERROR"))
                    {
                        _logger.LogError("PS_EXCEPTION: {Line}", trimmedLine);
                    }
                    else if (trimmedLine.StartsWith("DIAGNOSTIC:"))
                    {
                        _logger.LogWarning("PS_DIAGNOSTIC: {Line}", trimmedLine.Replace("DIAGNOSTIC:", "").Trim());
                    }
                    else
                    {
                        _logger.LogDebug("PS_OUTPUT: {Line}", trimmedLine);
                    }
                }

                // Extract the primary error message
                var errorMessage = connectResult.Contains("CONNECTION_FAILED:")
                    ? connectResult.Substring(connectResult.IndexOf("CONNECTION_FAILED:") + 18).Split('\n')[0].Trim()
                    : $"Connection failed - no CONNECTION_SUCCESS found";

                _logger.LogError("🚫 PRIMARY ERROR MESSAGE: {ErrorMessage}", errorMessage);

                // Provide specific guidance based on error patterns
                if (errorMessage.ToLower().Contains("certificate") || errorMessage.ToLower().Contains("ssl"))
                {
                    _logger.LogError("PowerCLI SSL/Certificate Error for {Server}: {Error}", connectionInfo.ServerAddress, errorMessage);
                    _logger.LogInformation("Suggestion: Verify PowerCLI InvalidCertificateAction is set to 'Ignore'");
                }
                else if (errorMessage.ToLower().Contains("authentication") || errorMessage.ToLower().Contains("login"))
                {
                    _logger.LogError("PowerCLI Authentication Error for {Server}: {Error}", connectionInfo.ServerAddress, errorMessage);
                    _logger.LogInformation("Suggestion: Verify username and password are correct");
                }
                else if (errorMessage.ToLower().Contains("timeout") || errorMessage.ToLower().Contains("network"))
                {
                    _logger.LogError("PowerCLI Network/Timeout Error for {Server}: {Error}", connectionInfo.ServerAddress, errorMessage);
                    _logger.LogInformation("Suggestion: Check network connectivity and firewall settings");
                }
                else
                {
                    _logger.LogError("PowerCLI Connection Error for {Server}: {Error}", connectionInfo.ServerAddress, errorMessage);
                }

                await DisconnectAsync(connectionKey);
                return (false, errorMessage, null);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to establish persistent connection to {Server}",
                connectionInfo.ServerAddress);
            await DisconnectAsync(connectionKey);
            return (false, $"Connection error: {ex.Message}", null);
            }
        }

    /// <summary>
    /// Starts a persistent PowerShell process
    /// </summary>
    private Process StartPersistentPowerShellProcess ()
        {
        try
            {
            // Try PowerShell 7 first, then fall back to Windows PowerShell
            var powershellPaths = new[]
            {
                "pwsh.exe",
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                "powershell.exe"
            };

            foreach (var psPath in powershellPaths)
                {
                try
                    {
                    var psi = new ProcessStartInfo
                        {
                        FileName = psPath,
                        Arguments = "-NoProfile -NoExit -ExecutionPolicy Unrestricted -Command -",
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                        };

                    var process = Process.Start(psi);

                    if (process != null && !process.HasExited)
                        {
                        _logger.LogInformation("Started persistent PowerShell process: {Path} (PID: {PID})",
                            psPath, process.Id);
                        return process;
                        }
                    }
                catch (Exception ex)
                    {
                    _logger.LogDebug("Could not start {Path}: {Error}", psPath, ex.Message);
                    }
                }

            _logger.LogError("Failed to start any PowerShell executable");
            return null;
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error starting PowerShell process");
            return null;
            }
        }

    /// <summary>
    /// Executes a command in the persistent PowerShell session with timeout
    /// </summary>
    private async Task<string> ExecuteCommandWithTimeoutAsync (string connectionKey, string command, TimeSpan timeout, bool skipConnectionCheck = false)
        {
        if (!_connections.TryGetValue(connectionKey, out var connection))
            {
            return "ERROR: No active connection";
            }

        try
            {
            // Clear the output buffer
            lock (connection.LockObject)
                {
                connection.OutputBuffer.Clear();
                }

            // Add a unique marker to know when command is complete
            var endMarker = $"END_COMMAND_{Guid.NewGuid():N}";
            
            string fullCommand;
            
            // For initial setup commands (like importing PowerCLI), skip the vCenter connection check
            if (skipConnectionCheck)
            {
                fullCommand = $@"
# Initial setup command - no vCenter connection check required
{command}
Write-Output '{endMarker}'
";
            }
            else 
            {
                // Ensure the correct vCenter connection is active for this command
                var connectionVarName = $"VIConnection_{connectionKey.Replace("-", "_")}";
                fullCommand = $@"
# Ensure correct vCenter connection is active for this session
if ($global:{connectionVarName} -and $global:{connectionVarName}.IsConnected) {{
    $global:DefaultVIServer = $global:{connectionVarName}
    $global:CurrentVCenterConnection = $global:{connectionVarName}
}} else {{
    Write-Output 'ERROR: No active connection for {connectionKey}'
    Write-Output '{endMarker}'
    exit
}}

{command}
Write-Output '{endMarker}'
";
            }

            // Check process stability before sending command
            if (connection.Process.HasExited)
            {
                _logger.LogError("PowerShell process has exited for connection {Key}", connectionKey);
                return "ERROR: PowerShell process has exited";
            }
            
            // Send the command with error handling
            try
            {
                await connection.StandardInput.WriteLineAsync(fullCommand);
                await connection.StandardInput.FlushAsync();
                
                // Small delay to allow command to be processed
                await Task.Delay(100);
            }
            catch (System.IO.IOException ioEx) when (ioEx.Message.Contains("pipe") || ioEx.Message.Contains("closed"))
            {
                _logger.LogError(ioEx, "Failed to send command - pipe closed for connection {Key}", connectionKey);
                connection.IsConnected = false;
                return $"ERROR: Cannot send command - pipe closed: {ioEx.Message}";
            }

            // Wait for the end marker with timeout
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime) < timeout)
                {
                await Task.Delay(100);

                string currentOutput;
                lock (connection.LockObject)
                    {
                    currentOutput = connection.OutputBuffer.ToString();
                    }

                if (currentOutput.Contains(endMarker))
                    {
                    // Remove the end marker and return the output
                    var output = currentOutput.Replace(endMarker, "").Trim();
                    return output;
                    }
                }

            _logger.LogWarning("Command timed out after {Timeout} seconds", timeout.TotalSeconds);
            return "ERROR: Command timed out";
            }
        catch (System.IO.IOException ioEx) when (ioEx.Message.Contains("pipe") || ioEx.Message.Contains("closed"))
            {
            _logger.LogError(ioEx, "PowerShell pipe error - process may have crashed");
            
            // Mark connection as failed and attempt cleanup
            if (_connections.TryGetValue(connectionKey, out var failedConnection))
            {
                failedConnection.IsConnected = false;
                _logger.LogWarning("Marking connection {Key} as failed due to pipe error", connectionKey);
            }
            
            return $"ERROR: PowerShell process pipe closed - {ioEx.Message}";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing command on connection {Key}", connectionKey);
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Executes a command in the context of a persistent connection
    /// </summary>
    public async Task<string> ExecuteCommandAsync (string connectionKey, string command)
        {
        return await ExecuteCommandWithTimeoutAsync(connectionKey, command, TimeSpan.FromMinutes(5), skipConnectionCheck: false);
        }

    /// <summary>
    /// Checks if a connection is still active
    /// </summary>
    public async Task<bool> IsConnectedAsync (string connectionKey)
        {
        if (!_connections.TryGetValue(connectionKey, out var connection))
            {
            return false;
            }

        if (connection.Process.HasExited)
            {
            _logger.LogWarning("PowerShell process has exited for {Key}", connectionKey);
            connection.IsConnected = false;
            return false;
            }

        try
            {
            var connectionVarName = $"VIConnection_{connectionKey.Replace("-", "_")}";
            var result = await ExecuteCommandWithTimeoutAsync(connectionKey,
                $"$global:{connectionVarName}.IsConnected",
                TimeSpan.FromSeconds(5), skipConnectionCheck: false);

            return result.Contains("True");
            }
        catch
            {
            return false;
            }
        }

    /// <summary>
    /// Gets connection info
    /// </summary>
    public (bool isConnected, string sessionId, string version) GetConnectionInfo (string connectionKey)
        {
        if (_connections.TryGetValue(connectionKey, out var connection))
            {
            return (connection.IsConnected, connection.SessionId, connection.VCenterVersion);
            }
        return (false, null, null);
        }

    /// <summary>
    /// Disconnects and cleans up a connection
    /// </summary>
    public async Task DisconnectAsync (string connectionKey)
        {
        if (!_connections.TryRemove(connectionKey, out var connection))
            {
            return;
            }

        try
            {
            _logger.LogInformation("Disconnecting from {Server} ({Key})",
                connection.ConnectionInfo.ServerAddress, connectionKey);

            if (!connection.Process.HasExited)
                {
                // Try to disconnect gracefully - only if PowerCLI is loaded and connected
                try
                    {
                    // Check if PowerCLI is loaded and has active connections before attempting disconnect
                    var checkResult = await ExecuteCommandWithTimeoutAsync(connectionKey, 
                        "if (Get-Command 'Get-VIServer' -ErrorAction SilentlyContinue) { if (Get-VIServer -ErrorAction SilentlyContinue) { 'CONNECTED' } else { 'NO_CONNECTION' } } else { 'NO_POWERCLI' }", 
                        TimeSpan.FromSeconds(5), skipConnectionCheck: true);
                    
                    if (checkResult.Contains("CONNECTED"))
                    {
                        await connection.StandardInput.WriteLineAsync(
                            $"Disconnect-VIServer -Server {connection.ConnectionInfo.ServerAddress} -Force -Confirm:$false");
                    }
                    // NOTE: Skip disconnect if PowerCLI not loaded or no active connections to avoid error noise
                    
                    await connection.StandardInput.WriteLineAsync("exit");
                    await connection.StandardInput.FlushAsync();

                    // Give it a moment to exit gracefully
                    if (!connection.Process.WaitForExit(5000))
                        {
                        connection.Process.Kill(entireProcessTree: true);
                        }
                    }
                catch
                    {
                    // Force kill if graceful exit fails
                    try
                        {
                        connection.Process.Kill(entireProcessTree: true);
                        }
                    catch { }
                    }
                }

            connection.StandardInput?.Dispose();
            connection.Process?.Dispose();
            connection.IsConnected = false;

            _logger.LogInformation("✅ Disconnected from {Server}", connection.ConnectionInfo.ServerAddress);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during disconnect from {Key}", connectionKey);
            }
        }

    /// <summary>
    /// Disconnects all active connections
    /// </summary>
    public async Task DisconnectAllAsync ()
        {
        var tasks = new List<Task>();

        foreach (var key in _connections.Keys)
            {
            tasks.Add(DisconnectAsync(key));
            }

        await Task.WhenAll(tasks);
        }

    public void Dispose ()
        {
        if (_disposed) return;

        _logger.LogInformation("Disposing PersistantVcenterConnectionService - closing all connections");

        DisconnectAllAsync().GetAwaiter().GetResult();

        _disposed = true;
        }
    }