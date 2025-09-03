using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services
{
    /// <summary>
    /// Manages a single shared PowerShell session with persistent vCenter connections
    /// Uses PSSession approach to maintain global variables for vCenter connections
    /// </summary>
    public class SharedPowerShellSessionService : IDisposable
    {
        private readonly ILogger<SharedPowerShellSessionService> _logger;
        private readonly PowerShellLoggingService _psLoggingService;
        private Runspace _runspace;
        private bool _isInitialized = false;
        private readonly object _sessionLock = new object();

        public SharedPowerShellSessionService(
            ILogger<SharedPowerShellSessionService> logger,
            PowerShellLoggingService psLoggingService)
        {
            _logger = logger;
            _psLoggingService = psLoggingService;
            InitializeSession();
        }

        /// <summary>
        /// Initialize the shared PowerShell session with PowerCLI modules
        /// </summary>
        private void InitializeSession()
        {
            try
            {
                _logger.LogInformation("Initializing shared PowerShell session with PowerCLI support");

                // Create initial session state with unrestricted execution policy
                var initialSessionState = InitialSessionState.CreateDefault();
                
                // Set execution policy to bypass for this session to avoid module loading issues
                initialSessionState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
                
                // Create the runspace with execution policy bypassed
                _runspace = RunspaceFactory.CreateRunspace(initialSessionState);
                _runspace.Open();

                // Now try to import PowerCLI modules after runspace is open with comprehensive execution policy handling
                var moduleImportScript = @"
                    try {
                        # Diagnostic: Check current execution policy in the runspace
                        $currentPolicy = Get-ExecutionPolicy
                        $currentScope = Get-ExecutionPolicy -List
                        Write-Output ""DIAGNOSTIC: Current execution policy in runspace: $currentPolicy""
                        Write-Output ""DIAGNOSTIC: Execution policy scope details:""
                        $currentScope | ForEach-Object { Write-Output ""DIAGNOSTIC: $($_.Scope): $($_.ExecutionPolicy)"" }
                        
                        # Multiple approaches to ensure execution policy allows module loading
                        try {
                            Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force -ErrorAction SilentlyContinue
                            Write-Output ""DIAGNOSTIC: Successfully set execution policy to Bypass for Process scope""
                        }
                        catch {
                            Write-Output ""DIAGNOSTIC: Could not set execution policy - $($_.Exception.Message)""
                        }
                        
                        # Additional execution policy override attempts
                        try {
                            $ExecutionContext.SessionState.LanguageMode = 'FullLanguage'
                        }
                        catch {
                            # Ignore language mode setting errors
                        }
                        
                        # Try to import PowerCLI modules with comprehensive error handling
                        $modules = @('VMware.PowerCLI', 'VMware.VimAutomation.Core', 'VMware.VimAutomation.Common', 'VMware.VimAutomation.Sdk')
                        $successCount = 0
                        
                        foreach ($module in $modules) {
                            try {
                                # First try to find the module
                                $moduleInfo = Get-Module -ListAvailable -Name $module -ErrorAction SilentlyContinue
                                if ($moduleInfo) {
                                    # Try to import with multiple approaches
                                    Import-Module $module -Force -DisableNameChecking -ErrorAction Stop
                                    Write-Output ""MODULE_IMPORTED: $module""
                                    $successCount++
                                } else {
                                    Write-Output ""MODULE_WARNING: Module $module not found in available modules""
                                }
                            }
                            catch {
                                # Try alternative import method
                                try {
                                    Import-Module $module -Force -SkipEditionCheck -ErrorAction SilentlyContinue
                                    Write-Output ""MODULE_FALLBACK_IMPORTED: $module""
                                    $successCount++
                                }
                                catch {
                                    Write-Output ""MODULE_WARNING: Could not import $module - $($_.Exception.Message)""
                                }
                            }
                        }
                        
                        Write-Output ""MODULES_PROCESSED: PowerCLI module import completed ($successCount/$($modules.Count) successful)""
                    }
                    catch {
                        Write-Output ""MODULE_ERROR: $($_.Exception.Message)""
                    }
                ";

                var moduleResult = ExecuteScriptInternal(moduleImportScript);
                _logger.LogDebug("Module import result: {Result}", moduleResult);

                // Configure PowerCLI settings
                var initScript = @"
                    # Configure PowerCLI settings for multiple vCenter connections and SSL bypass
                    try {
                        # Configure SSL certificate handling (ignore all certificate errors)
                        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session
                        
                        # Disable customer experience improvement program
                        Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session
                        
                        # Enable multiple vCenter server connections (critical for migration scenarios)
                        Set-PowerCLIConfiguration -DefaultVIServerMode Multiple -Confirm:$false -Scope Session
                        
                        # Set web operation timeout (increase for slower vCenter responses)
                        Set-PowerCLIConfiguration -WebOperationTimeoutSeconds 300 -Confirm:$false -Scope Session
                        
                        # Configure proxy settings if needed (bypass proxy for internal connections)
                        Set-PowerCLIConfiguration -ProxyPolicy NoProxy -Confirm:$false -Scope Session
                        
                        # Additional SSL/TLS configuration for maximum compatibility
                        try {
                            # Force TLS 1.2+ for secure connections
                            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
                            
                            # Disable SSL certificate validation at .NET level
                            [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
                            
                            Write-Output 'SSL_CONFIG: Enhanced SSL/TLS configuration applied'
                        }
                        catch {
                            Write-Output 'SSL_WARNING: Advanced SSL configuration failed, continuing with PowerCLI settings only'
                        }
                        
                        # Initialize global connection variables
                        $global:SourceVIConnection = $null
                        $global:TargetVIConnection = $null
                        
                        # Display current PowerCLI configuration for verification
                        $config = Get-PowerCLIConfiguration
                        Write-Output ""CONFIG_VERIFICATION: InvalidCertificateAction=$($config.InvalidCertificateAction)""
                        Write-Output ""CONFIG_VERIFICATION: DefaultVIServerMode=$($config.DefaultVIServerMode)""
                        Write-Output ""CONFIG_VERIFICATION: WebOperationTimeout=$($config.WebOperationTimeoutSeconds)""
                        
                        Write-Output 'INIT_SUCCESS: PowerCLI configured for multiple vCenter connections with SSL bypass'
                    }
                    catch {
                        Write-Output ""INIT_ERROR: $($_.Exception.Message)""
                    }
                ";

                var result = ExecuteScriptInternal(initScript);
                
                if (result.Contains("INIT_SUCCESS"))
                {
                    _isInitialized = true;
                    _logger.LogInformation("Shared PowerShell session initialized successfully with multiple vCenter support and SSL bypass");
                    
                    // Log configuration verification if present
                    if (result.Contains("CONFIG_VERIFICATION"))
                    {
                        var lines = result.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines.Where(l => l.Contains("CONFIG_VERIFICATION")))
                        {
                            _logger.LogInformation("PowerCLI Configuration: {ConfigLine}", line.Replace("CONFIG_VERIFICATION: ", ""));
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("PowerShell session initialization completed with warnings: {Result}", result);
                    _isInitialized = true; // Continue anyway, might still be usable
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize shared PowerShell session");
                throw;
            }
        }

        /// <summary>
        /// Connect to vCenter and store connection in global variable
        /// </summary>
        public async Task<(bool success, string message)> ConnectToVCenterAsync(
            VCenterConnection connectionInfo, 
            string password, 
            bool isSource = true)
        {
            if (!_isInitialized)
            {
                return (false, "PowerShell session not initialized");
            }

            var connectionType = isSource ? "Source" : "Target";
            var globalVarName = isSource ? "$global:SourceVIConnection" : "$global:TargetVIConnection";
            var scriptName = $"Connect-{connectionType}VCenter";
            var sessionId = _psLoggingService.StartScriptLogging(scriptName, connectionType.ToLower());

            try
            {
                _logger.LogInformation("Connecting to {Type} vCenter: {Server}", connectionType, connectionInfo.ServerAddress);
                _psLoggingService.LogScriptAction(sessionId, scriptName, "CONNECTION_START", $"Connecting to {connectionType} vCenter: {connectionInfo.ServerAddress}");

                var connectScript = $@"
                    try {{
                        # Check if already connected
                        if ({globalVarName} -ne $null -and {globalVarName}.IsConnected) {{
                            if ({globalVarName}.Name -eq '{connectionInfo.ServerAddress}') {{
                                Write-Output 'ALREADY_CONNECTED: Connection already exists and is active'
                                return
                            }} else {{
                                # Disconnect from different server
                                try {{ Disconnect-VIServer -Server {globalVarName} -Confirm:$false -Force }} catch {{}}
                                {globalVarName} = $null
                            }}
                        }}

                        # Create secure credential
                        $securePassword = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                        $credential = New-Object System.Management.Automation.PSCredential('{connectionInfo.Username.Replace("'", "''")}', $securePassword)
                        
                        # Connect to vCenter
                        Write-Output 'CONNECTING: Attempting connection to {connectionInfo.ServerAddress}'
                        {globalVarName} = Connect-VIServer -Server '{connectionInfo.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                        
                        if ({globalVarName} -and {globalVarName}.IsConnected) {{
                            Write-Output ""CONNECTION_SUCCESS: Connected to $({globalVarName}.Name) - Version $({globalVarName}.Version)""
                            Write-Output ""SESSION_ID: $({globalVarName}.SessionId)""
                        }} else {{
                            Write-Output 'CONNECTION_FAILED: Connection object exists but IsConnected is false'
                        }}
                    }}
                    catch {{
                        {globalVarName} = $null
                        Write-Output ""CONNECTION_FAILED: $($_.Exception.Message)""
                    }}
                ";

                var result = await Task.Run(() => ExecuteScriptInternal(connectScript));

                if (result.Contains("CONNECTION_SUCCESS") || result.Contains("ALREADY_CONNECTED"))
                {
                    _logger.LogInformation("Successfully connected to {Type} vCenter: {Server}", connectionType, connectionInfo.ServerAddress);
                    _psLoggingService.LogScriptOutput(sessionId, scriptName, result, "SUCCESS");
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, true, $"Successfully connected to {connectionType} vCenter");
                    return (true, "Connected successfully");
                }
                else
                {
                    var errorMessage = result.Contains("CONNECTION_FAILED:")
                        ? result.Substring(result.IndexOf("CONNECTION_FAILED:") + 18).Trim()
                        : $"Connection failed - Result: {result.Trim()}";
                    
                    _logger.LogError("Failed to connect to {Type} vCenter {Server}: {Error}", connectionType, connectionInfo.ServerAddress, errorMessage);
                    _psLoggingService.LogScriptError(sessionId, scriptName, errorMessage);
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, false, errorMessage);
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception connecting to {Type} vCenter {Server}", connectionType, connectionInfo.ServerAddress);
                _psLoggingService.LogScriptError(sessionId, scriptName, ex.Message);
                _psLoggingService.EndScriptLogging(sessionId, scriptName, false, $"Exception: {ex.Message}");
                return (false, $"Connection error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if vCenter connection is active
        /// </summary>
        public async Task<bool> IsVCenterConnectedAsync(bool isSource = true)
        {
            if (!_isInitialized) return false;

            var globalVarName = isSource ? "$global:SourceVIConnection" : "$global:TargetVIConnection";
            var connectionType = isSource ? "Source" : "Target";

            try
            {
                var checkScript = $@"
                    try {{
                        if ({globalVarName} -ne $null -and {globalVarName}.IsConnected) {{
                            # Double-check with Get-VIServer
                            $serverCheck = Get-VIServer -Server {globalVarName}.Name -ErrorAction SilentlyContinue
                            if ($serverCheck -and $serverCheck.IsConnected) {{
                                Write-Output 'CONNECTED: True'
                            }} else {{
                                # Connection lost, clear the variable
                                {globalVarName} = $null
                                Write-Output 'DISCONNECTED: Connection lost'
                            }}
                        }} else {{
                            Write-Output 'DISCONNECTED: No connection'
                        }}
                    }}
                    catch {{
                        Write-Output 'DISCONNECTED: Check failed'
                    }}
                ";

                var result = await Task.Run(() => ExecuteScriptInternal(checkScript));
                var isConnected = result.Contains("CONNECTED: True");
                
                _logger.LogDebug("{Type} vCenter connection status: {Status}", connectionType, isConnected ? "Connected" : "Disconnected");
                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking {Type} vCenter connection status", connectionType);
                return false;
            }
        }

        /// <summary>
        /// Execute PowerShell command in the shared session
        /// </summary>
        public async Task<string> ExecuteCommandAsync(string command, bool isSource = true)
        {
            if (!_isInitialized)
            {
                return "ERROR: PowerShell session not initialized";
            }

            var connectionType = isSource ? "Source" : "Target";
            var globalVarName = isSource ? "$global:SourceVIConnection" : "$global:TargetVIConnection";
            var scriptName = $"Execute-{connectionType}Command";
            var sessionId = _psLoggingService.StartScriptLogging(scriptName, connectionType.ToLower());

            try
            {
                _psLoggingService.LogScriptAction(sessionId, scriptName, "COMMAND_START", $"Executing PowerCLI command on {connectionType}");
                _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Command: {command.Substring(0, Math.Min(command.Length, 200))}", "DEBUG");
                var wrappedScript = $@"
                    # Ensure we're using the correct connection
                    if ({globalVarName} -eq $null -or -not {globalVarName}.IsConnected) {{
                        Write-Output 'ERROR: No active {connectionType} vCenter connection'
                        return
                    }}

                    try {{
                        # Set default server for PowerCLI commands
                        $DefaultVIServer = {globalVarName}
                        
                        # Execute the command
                        {command}
                    }}
                    catch {{
                        Write-Output ""ERROR: $($_.Exception.Message)""
                    }}
                ";

                var result = await Task.Run(() => ExecuteScriptInternal(wrappedScript));
                
                if (result.StartsWith("ERROR"))
                {
                    _psLoggingService.LogScriptError(sessionId, scriptName, result);
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, false, "Command execution failed");
                }
                else
                {
                    _psLoggingService.LogScriptOutput(sessionId, scriptName, result.Substring(0, Math.Min(result.Length, 1000)), "SUCCESS");
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, true, "Command executed successfully");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command in shared session");
                _psLoggingService.LogScriptError(sessionId, scriptName, ex.Message);
                _psLoggingService.EndScriptLogging(sessionId, scriptName, false, $"Exception: {ex.Message}");
                return $"ERROR: {ex.Message}";
            }
        }

        /// <summary>
        /// Internal method to execute PowerShell scripts in the shared runspace
        /// </summary>
        private string ExecuteScriptInternal(string script)
        {
            lock (_sessionLock)
            {
                try
                {
                    using var powerShell = PowerShell.Create();
                    powerShell.Runspace = _runspace;
                    powerShell.AddScript(script);

                    var results = powerShell.Invoke();
                    var output = new List<string>();

                    foreach (var result in results)
                    {
                        if (result != null)
                        {
                            output.Add(result.ToString());
                        }
                    }

                    // Include any errors
                    if (powerShell.HadErrors)
                    {
                        foreach (var error in powerShell.Streams.Error)
                        {
                            output.Add($"ERROR: {error}");
                            _logger.LogWarning("PowerShell Error in shared session: {Error}", error.ToString());
                        }
                    }

                    // Also capture warnings, verbose, and debug streams for comprehensive logging
                    foreach (var warning in powerShell.Streams.Warning)
                    {
                        _logger.LogWarning("PowerShell Warning in shared session: {Warning}", warning.Message);
                    }

                    foreach (var verbose in powerShell.Streams.Verbose)
                    {
                        _logger.LogDebug("PowerShell Verbose in shared session: {Verbose}", verbose.Message);
                    }

                    var resultString = string.Join(Environment.NewLine, output);
                    _logger.LogDebug("PowerShell execution result length: {Length} characters", resultString.Length);
                    
                    return resultString;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing PowerShell script in shared session");
                    return $"ERROR: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// Disconnect from vCenter
        /// </summary>
        public async Task DisconnectVCenterAsync(bool isSource = true)
        {
            if (!_isInitialized) return;

            var connectionType = isSource ? "Source" : "Target";
            var globalVarName = isSource ? "$global:SourceVIConnection" : "$global:TargetVIConnection";

            try
            {
                var disconnectScript = $@"
                    try {{
                        if ({globalVarName} -ne $null) {{
                            if ({globalVarName}.IsConnected) {{
                                Disconnect-VIServer -Server {globalVarName} -Confirm:$false -Force
                                Write-Output 'DISCONNECTED: Successfully disconnected from {connectionType}'
                            }}
                            {globalVarName} = $null
                        }}
                    }}
                    catch {{
                        {globalVarName} = $null
                        Write-Output ""DISCONNECT_ERROR: $($_.Exception.Message)""
                    }}
                ";

                var result = await Task.Run(() => ExecuteScriptInternal(disconnectScript));
                _logger.LogInformation("{Type} vCenter disconnection result: {Result}", connectionType, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting {Type} vCenter", connectionType);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_isInitialized)
                {
                    // Disconnect from both vCenters
                    DisconnectVCenterAsync(true).Wait();
                    DisconnectVCenterAsync(false).Wait();
                }

                _runspace?.Close();
                _runspace?.Dispose();
                _logger.LogInformation("Shared PowerShell session disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing shared PowerShell session");
            }
        }
    }
}