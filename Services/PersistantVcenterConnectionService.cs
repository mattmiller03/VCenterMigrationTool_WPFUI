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
public class PersistentExternalConnectionService : IDisposable
    {
    private readonly ILogger<PersistentExternalConnectionService> _logger;
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

    public PersistentExternalConnectionService (ILogger<PersistentExternalConnectionService> logger)
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
                # Diagnostic: Check PowerCLI availability
                Write-Output ""DIAGNOSTIC: Checking PowerCLI module availability""
                $powerCLIModule = Get-Module -Name VMware.PowerCLI -ListAvailable -ErrorAction SilentlyContinue
                if ($powerCLIModule) {{
                    Write-Output ""DIAGNOSTIC: PowerCLI module found - version $($powerCLIModule.Version)""
                }} else {{
                    Write-Output ""DIAGNOSTIC: PowerCLI module not found - attempting to import""
                    try {{
                        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                        Write-Output ""DIAGNOSTIC: PowerCLI module imported successfully""
                    }} catch {{
                        Write-Output ""DIAGNOSTIC: Failed to import PowerCLI module: $($_.Exception.Message)""
                    }}
                }}

                # Re-apply critical PowerCLI configuration before connection (ensure settings persist)
                try {{
                    Write-Output ""DIAGNOSTIC: Re-applying PowerCLI configuration before connection...""
                    
                    # Essential configurations for vCenter connection
                    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                    Set-PowerCLIConfiguration -DefaultVIServerMode Multiple -Confirm:$false -Scope Session | Out-Null
                    Set-PowerCLIConfiguration -WebOperationTimeoutSeconds 300 -Confirm:$false -Scope Session | Out-Null
                    Set-PowerCLIConfiguration -ProxyPolicy NoProxy -Confirm:$false -Scope Session | Out-Null
                    
                    # Verify current configuration
                    $config = Get-PowerCLIConfiguration
                    Write-Output ""DIAGNOSTIC: Current DefaultVIServerMode: $($config.DefaultVIServerMode)""
                    Write-Output ""DIAGNOSTIC: Current InvalidCertificateAction: $($config.InvalidCertificateAction)""
                }} catch {{
                    Write-Output ""DIAGNOSTIC: PowerCLI configuration update failed: $($_.Exception.Message)""
                }}

                # Create credential
                $password = '{password.Replace("'", "''")}'
                $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
                $credential = New-Object System.Management.Automation.PSCredential('{connectionInfo.Username.Replace("'", "''")}', $securePassword)
                $password = $null  # Clear from memory
                
                # Connect to vCenter with enhanced diagnostics
                try {{
                    Write-Output ""DIAGNOSTIC: Attempting connection to {connectionInfo.ServerAddress}""
                    Write-Output ""DIAGNOSTIC: Using credential for user: {connectionInfo.Username.Replace("'", "''")}""
                    
                    # Check for existing connections before attempting new connection
                    $existingConnections = Get-VIServer -ErrorAction SilentlyContinue
                    if ($existingConnections) {{
                        Write-Output ""DIAGNOSTIC: Found $($existingConnections.Count) existing vCenter connections""
                        $existingConnections | ForEach-Object {{
                            Write-Output ""DIAGNOSTIC: - Existing connection: $($_.Name) (Connected: $($_.IsConnected))""
                        }}
                    }} else {{
                        Write-Output ""DIAGNOSTIC: No existing vCenter connections found""
                    }}
                    
                    $connection = Connect-VIServer -Server '{connectionInfo.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                    
                    if ($connection -and $connection.IsConnected) {{
                        Write-Output ""CONNECTION_SUCCESS""
                        Write-Output ""SESSION_ID:$($connection.SessionId)""
                        Write-Output ""VERSION:$($connection.Version)""
                        Write-Output ""BUILD:$($connection.Build)""
                        
                        # Store in connection-specific global variables to avoid conflicts
                        $global:VIConnection_{connectionKey.Replace("-", "_")} = $connection
                        
                        # Set as default for this session but save existing default if any
                        if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {{
                            $global:PreviousDefaultVIServer = $global:DefaultVIServer
                        }}
                        $global:DefaultVIServer = $connection
                        $global:CurrentVCenterConnection = $connection
                        
                        # Lightweight connection verification (avoid heavy queries during setup)
                        Write-Output ""CONNECTION_VERIFIED""
                    }} else {{
                        Write-Output ""CONNECTION_FAILED: Not connected""
                    }}
                }} catch {{
                    Write-Output ""CONNECTION_FAILED: $($_.Exception.Message)""
                    Write-Output ""DIAGNOSTIC: Connection failure details:""
                    Write-Output ""DIAGNOSTIC: - Server: {connectionInfo.ServerAddress}""
                    Write-Output ""DIAGNOSTIC: - User: {connectionInfo.Username.Replace("'", "''")}""
                    Write-Output ""DIAGNOSTIC: - Exception Type: $($_.Exception.GetType().Name)""
                    
                    # Additional diagnostics for common connection issues
                    try {{
                        Write-Output ""DIAGNOSTIC: Testing network connectivity...""
                        $ping = Test-NetConnection -ComputerName '{connectionInfo.ServerAddress}' -Port 443 -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
                        if ($ping.TcpTestSucceeded) {{
                            Write-Output ""DIAGNOSTIC: Network connectivity to port 443: SUCCESS""
                        }} else {{
                            Write-Output ""DIAGNOSTIC: Network connectivity to port 443: FAILED""
                        }}
                    }} catch {{
                        Write-Output ""DIAGNOSTIC: Network connectivity test failed: $($_.Exception.Message)""
                    }}
                    
                    # Check if this is a certificate or authentication issue
                    if ($_.Exception.Message -like '*certificate*' -or $_.Exception.Message -like '*SSL*' -or $_.Exception.Message -like '*TLS*') {{
                        Write-Output ""DIAGNOSTIC: This appears to be a certificate/SSL issue""
                        Write-Output ""DIAGNOSTIC: Current InvalidCertificateAction setting should ignore certificates""
                    }} elseif ($_.Exception.Message -like '*authentication*' -or $_.Exception.Message -like '*login*' -or $_.Exception.Message -like '*credential*') {{
                        Write-Output ""DIAGNOSTIC: This appears to be an authentication issue""
                        Write-Output ""DIAGNOSTIC: Please verify username and password are correct""
                    }} elseif ($_.Exception.Message -like '*timeout*' -or $_.Exception.Message -like '*connection*') {{
                        Write-Output ""DIAGNOSTIC: This appears to be a network connectivity or timeout issue""
                    }}
                }}
            ";

            var connectResult = await ExecuteCommandWithTimeoutAsync(connectionKey, connectScript, TimeSpan.FromSeconds(60), skipConnectionCheck: false);

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
                // Log the full result for debugging
                _logger.LogWarning("Connection result did not contain CONNECTION_SUCCESS. Full result: {Result}", connectResult);
                
                // Extract error message and diagnostics
                var errorMessage = connectResult.Contains("CONNECTION_FAILED:")
                    ? connectResult.Substring(connectResult.IndexOf("CONNECTION_FAILED:") + 18).Split('\n')[0].Trim()
                    : $"Connection failed - Result: {connectResult.Trim()}";

                // Extract and log diagnostic information
                var lines = connectResult.Split('\n');
                var diagnostics = lines.Where(l => l.Contains("DIAGNOSTIC:")).ToList();
                
                if (diagnostics.Any())
                {
                    _logger.LogWarning("PowerCLI Connection Diagnostics for {Server}:", connectionInfo.ServerAddress);
                    foreach (var diagnostic in diagnostics)
                    {
                        var cleanDiagnostic = diagnostic.Replace("DIAGNOSTIC:", "").Trim();
                        if (!string.IsNullOrEmpty(cleanDiagnostic))
                        {
                            _logger.LogWarning("  - {Diagnostic}", cleanDiagnostic);
                        }
                    }
                }

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

        _logger.LogInformation("Disposing PersistentExternalConnectionService - closing all connections");

        DisconnectAllAsync().GetAwaiter().GetResult();

        _disposed = true;
        }
    }