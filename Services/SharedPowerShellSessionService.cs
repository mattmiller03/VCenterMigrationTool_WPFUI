using System;
using System.Collections.Generic;
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
        private Runspace _runspace;
        private bool _isInitialized = false;
        private readonly object _sessionLock = new object();

        public SharedPowerShellSessionService(ILogger<SharedPowerShellSessionService> logger)
        {
            _logger = logger;
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

                // Create initial session state with PowerCLI modules
                var initialSessionState = InitialSessionState.CreateDefault();
                
                // Import PowerCLI modules - try both new and legacy approaches
                var powerCLIModules = new[]
                {
                    "VMware.PowerCLI",
                    "VMware.VimAutomation.Core",
                    "VMware.VimAutomation.Common"
                };

                foreach (var module in powerCLIModules)
                {
                    try
                    {
                        initialSessionState.ImportPSModule(new[] { module });
                        _logger.LogDebug("Successfully imported module: {Module}", module);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Could not import module {Module}: {Error}", module, ex.Message);
                    }
                }

                // Create the runspace with initial session state
                _runspace = RunspaceFactory.CreateRunspace(initialSessionState);
                _runspace.Open();

                // Configure PowerCLI settings
                var initScript = @"
                    # Configure PowerCLI settings
                    try {
                        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session
                        Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session
                        
                        # Initialize global connection variables
                        $global:SourceVIConnection = $null
                        $global:TargetVIConnection = $null
                        
                        Write-Output 'INIT_SUCCESS: PowerCLI configured and global variables initialized'
                    }
                    catch {
                        Write-Output ""INIT_ERROR: $($_.Exception.Message)""
                    }
                ";

                var result = ExecuteScriptInternal(initScript);
                
                if (result.Contains("INIT_SUCCESS"))
                {
                    _isInitialized = true;
                    _logger.LogInformation("Shared PowerShell session initialized successfully");
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

            try
            {
                _logger.LogInformation("Connecting to {Type} vCenter: {Server}", connectionType, connectionInfo.ServerAddress);

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
                    return (true, "Connected successfully");
                }
                else
                {
                    var errorMessage = result.Contains("CONNECTION_FAILED:")
                        ? result.Substring(result.IndexOf("CONNECTION_FAILED:") + 18).Trim()
                        : $"Connection failed - Result: {result.Trim()}";
                    
                    _logger.LogError("Failed to connect to {Type} vCenter {Server}: {Error}", connectionType, connectionInfo.ServerAddress, errorMessage);
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception connecting to {Type} vCenter {Server}", connectionType, connectionInfo.ServerAddress);
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

            try
            {
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

                return await Task.Run(() => ExecuteScriptInternal(wrappedScript));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command in shared session");
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
                        }
                    }

                    return string.Join(Environment.NewLine, output);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing PowerShell script");
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