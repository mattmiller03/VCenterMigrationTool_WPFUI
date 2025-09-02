using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages persistent vCenter connections using PowerShell processes
/// Refactored to use dedicated managers for better separation of concerns
/// </summary>
public class PersistantVcenterConnectionService : IDisposable
{
    private readonly ILogger<PersistantVcenterConnectionService> _logger;
    private readonly PowerShellProcessManager _processManager;
    private readonly ConnectionStateManager _connectionStateManager;
    private readonly PowerCLIConfigurationService _powerCLIConfigurationService;
    
    // Store the managed processes by connection key
    private readonly ConcurrentDictionary<string, PowerShellProcessManager.ManagedPowerShellProcess> _processes = new();
    private bool _disposed = false;

    public PersistantVcenterConnectionService(
        ILogger<PersistantVcenterConnectionService> logger,
        PowerShellProcessManager processManager,
        ConnectionStateManager connectionStateManager,
        PowerCLIConfigurationService powerCLIConfigurationService)
    {
        _logger = logger;
        _processManager = processManager;
        _connectionStateManager = connectionStateManager;
        _powerCLIConfigurationService = powerCLIConfigurationService;
    }

    /// <summary>
    /// Establishes a persistent connection using an external PowerShell process
    /// </summary>
    public async Task<(bool success, string message, string sessionId)> ConnectAsync(
        VCenterConnection connectionInfo,
        string password,
        bool isSource = true,
        bool bypassModuleCheck = false)
    {
        var connectionKey = isSource ? "source" : "target";

        try
        {
            _logger.LogInformation("🔗 Establishing persistent connection to {Server} ({ConnectionKey})",
                connectionInfo.ServerAddress, connectionKey);

            // Initialize connection state tracking
            _connectionStateManager.CreateOrUpdateConnection(connectionKey, connectionInfo, 
                ConnectionStateManager.ConnectionStatus.Connecting);

            // Clean up any existing connection
            await DisconnectAsync(connectionKey);

            // Step 1: Create PowerShell process
            _logger.LogDebug("Creating PowerShell process for {ConnectionKey}...", connectionKey);
            var process = await _processManager.CreatePersistentProcessAsync();

            if (process == null)
            {
                var errorMsg = "Failed to create PowerShell process";
                _connectionStateManager.MarkConnectionFailed(connectionKey, errorMsg);
                return (false, errorMsg, string.Empty);
            }

            // Store the process
            _processes[connectionKey] = process;
            _logger.LogInformation("✅ PowerShell process created for {ConnectionKey} (PID: {ProcessId})", 
                connectionKey, process.ProcessId);

            // Step 2: Configure PowerCLI (unless bypassed)
            var sessionId = string.Empty;
            
            if (bypassModuleCheck)
            {
                _logger.LogInformation("⚠️ Bypassing PowerCLI configuration for {ConnectionKey}", connectionKey);
                sessionId = $"bypass-{Guid.NewGuid():N}";
                
                _connectionStateManager.MarkConnectionEstablished(connectionKey, sessionId, 
                    "Unknown (bypass mode)", "", "Bypass Mode");
                
                return (true, "Connected in bypass mode - PowerCLI functionality limited", sessionId);
            }

            _logger.LogDebug("Configuring PowerCLI for {ConnectionKey}...", connectionKey);
            var configResult = await _powerCLIConfigurationService.ConfigurePowerCLIAsync(process, bypassModuleCheck);

            if (!configResult.Success)
            {
                var errorMsg = $"PowerCLI configuration failed: {configResult.Message}";
                _logger.LogError("❌ {ErrorMessage}", errorMsg);
                _connectionStateManager.MarkConnectionFailed(connectionKey, errorMsg);
                
                await DisconnectAsync(connectionKey);
                return (false, errorMsg, string.Empty);
            }

            _logger.LogInformation("✅ PowerCLI configured successfully using {ModuleType}", configResult.ModuleType);

            // Step 3: Connect to vCenter
            _logger.LogInformation("🔌 Connecting to vCenter {Server}...", connectionInfo.ServerAddress);
            var connectScript = PowerShellScriptBuilder.BuildVCenterConnectionScript(connectionInfo, password, connectionKey);
            var connectResult = await _processManager.ExecuteCommandAsync(process, connectScript, TimeSpan.FromSeconds(120));

            // Step 4: Process connection result
            if (connectResult.Contains("CONNECTION_SUCCESS"))
            {
                // Parse connection details from PowerShell output
                var connectionDetails = ParseConnectionDetails(connectResult);
                sessionId = connectionDetails.SessionId;

                _connectionStateManager.MarkConnectionEstablished(connectionKey, sessionId, 
                    connectionDetails.Version, connectionDetails.Build, connectionDetails.ProductLine);

                _logger.LogInformation("✅ vCenter connection established for {ConnectionKey} (Session: {SessionId}, Version: {Version})",
                    connectionKey, sessionId, connectionDetails.Version);

                return (true, "Connected successfully", sessionId);
            }
            else
            {
                // Handle connection failure with detailed logging
                var errorMessage = await HandleConnectionFailureAsync(connectionKey, connectionInfo, connectResult);
                
                _connectionStateManager.MarkConnectionFailed(connectionKey, errorMessage);
                await DisconnectAsync(connectionKey);
                
                return (false, errorMessage, string.Empty);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error during connection: {ex.Message}";
            _logger.LogError(ex, "💥 Exception while connecting to {Server}", connectionInfo.ServerAddress);
            
            _connectionStateManager.MarkConnectionFailed(connectionKey, errorMessage);
            await DisconnectAsync(connectionKey);
            
            return (false, errorMessage, string.Empty);
        }
    }

    /// <summary>
    /// Connection details parsed from PowerShell output
    /// </summary>
    private class ConnectionDetails
    {
        public string SessionId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Build { get; set; } = string.Empty;
        public string ProductLine { get; set; } = string.Empty;
        public string User { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses connection details from PowerShell output
    /// </summary>
    private ConnectionDetails ParseConnectionDetails(string output)
    {
        var details = new ConnectionDetails();
        
        try
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("SESSION_ID:"))
                    details.SessionId = trimmedLine.Substring(11).Trim();
                else if (trimmedLine.StartsWith("VERSION:"))
                    details.Version = trimmedLine.Substring(8).Trim();
                else if (trimmedLine.StartsWith("BUILD:"))
                    details.Build = trimmedLine.Substring(6).Trim();
                else if (trimmedLine.StartsWith("PRODUCT_LINE:"))
                    details.ProductLine = trimmedLine.Substring(13).Trim();
                else if (trimmedLine.StartsWith("USER:"))
                    details.User = trimmedLine.Substring(5).Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing connection details from output");
        }

        return details;
    }

    /// <summary>
    /// Handles connection failure with detailed logging and error analysis
    /// </summary>
    private async Task<string> HandleConnectionFailureAsync(string connectionKey, VCenterConnection connectionInfo, string connectResult)
    {
        try
        {
            _logger.LogWarning("🔍 Analyzing connection failure for {Server}...", connectionInfo.ServerAddress);
            
            // Log complete output for debugging
            _logger.LogDebug("Complete PowerShell output for {Server} ({Length} chars): {Output}", 
                connectionInfo.ServerAddress, connectResult?.Length ?? 0, connectResult);

            // Parse the output for specific error information
            var lines = connectResult.Split('\n');
            var errorLines = new List<string>();
            var diagnosticLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine)) continue;
                
                if (trimmedLine.StartsWith("DEBUG_ERROR_VAR:"))
                {
                    errorLines.Add(trimmedLine.Replace("DEBUG_ERROR_VAR:", "").Trim());
                    _logger.LogError("🚨 Error Variable: {Error}", trimmedLine.Replace("DEBUG_ERROR_VAR:", "").Trim());
                }
                else if (trimmedLine.StartsWith("CONNECTION_FAILED:"))
                {
                    errorLines.Add(trimmedLine.Replace("CONNECTION_FAILED:", "").Trim());
                    _logger.LogError("❌ Connection Failed: {Error}", trimmedLine.Replace("CONNECTION_FAILED:", "").Trim());
                }
                else if (trimmedLine.StartsWith("DEBUG_ANALYSIS:"))
                {
                    diagnosticLines.Add(trimmedLine.Replace("DEBUG_ANALYSIS:", "").Trim());
                    _logger.LogWarning("🔍 Analysis: {Analysis}", trimmedLine.Replace("DEBUG_ANALYSIS:", "").Trim());
                }
                else if (trimmedLine.Contains("EXCEPTION") || trimmedLine.Contains("ERROR"))
                {
                    errorLines.Add(trimmedLine);
                    _logger.LogError("⚠️ Exception/Error: {Error}", trimmedLine);
                }
            }

            // Determine primary error message
            string primaryError;
            if (errorLines.Any())
            {
                primaryError = errorLines.First();
            }
            else if (connectResult.Contains("CONNECTION_FAILED:"))
            {
                var startIndex = connectResult.IndexOf("CONNECTION_FAILED:") + 18;
                primaryError = connectResult.Substring(startIndex).Split('\n')[0].Trim();
            }
            else
            {
                primaryError = "Connection failed - no specific error information available";
            }

            // Provide categorized guidance
            var lowerError = primaryError.ToLower();
            if (lowerError.Contains("certificate") || lowerError.Contains("ssl") || lowerError.Contains("tls"))
            {
                _logger.LogInformation("💡 Suggestion: SSL/Certificate issue detected - verify PowerCLI InvalidCertificateAction setting");
            }
            else if (lowerError.Contains("authentication") || lowerError.Contains("login") || lowerError.Contains("credential"))
            {
                _logger.LogInformation("💡 Suggestion: Authentication issue detected - verify username/password and account status");
            }
            else if (lowerError.Contains("timeout") || lowerError.Contains("network") || lowerError.Contains("connection"))
            {
                _logger.LogInformation("💡 Suggestion: Network/Timeout issue detected - check connectivity and firewall settings");
            }

            return primaryError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing connection failure for {ConnectionKey}", connectionKey);
            return $"Connection failed with analysis error: {ex.Message}";
        }
    }

    /// <summary>
    /// Executes a command in the context of a persistent connection
    /// </summary>
    public async Task<string> ExecuteCommandAsync(string connectionKey, string command)
    {
        try
        {
            if (_processes.TryGetValue(connectionKey, out var process))
            {
                _connectionStateManager.RecordActivity(connectionKey);
                return await _processManager.ExecuteCommandAsync(process, command, TimeSpan.FromMinutes(5));
            }
            else
            {
                return $"ERROR: No active connection for {connectionKey}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command for connection {ConnectionKey}", connectionKey);
            return $"ERROR: Command execution failed - {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if a connection is still active
    /// </summary>
    public async Task<bool> IsConnectedAsync(string connectionKey)
    {
        try
        {
            // Check state manager first
            var state = _connectionStateManager.GetConnectionState(connectionKey);
            if (state?.Status != ConnectionStateManager.ConnectionStatus.Connected)
            {
                return false;
            }

            // Verify process is still running
            if (_processes.TryGetValue(connectionKey, out var process))
            {
                if (process.HasExited)
                {
                    _logger.LogWarning("PowerShell process has exited for {ConnectionKey}", connectionKey);
                    _connectionStateManager.MarkConnectionFailed(connectionKey, "PowerShell process has exited");
                    return false;
                }

                // Test connection with a simple command
                var testScript = PowerShellScriptBuilder.BuildConnectionValidationScript(connectionKey);
                var result = await _processManager.ExecuteCommandAsync(process, testScript, TimeSpan.FromSeconds(10));
                
                var isActive = result.Contains("CONNECTION_ACTIVE");
                if (!isActive)
                {
                    _connectionStateManager.MarkConnectionFailed(connectionKey, "Connection validation failed");
                }
                else
                {
                    _connectionStateManager.RecordActivity(connectionKey);
                }

                return isActive;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection status for {ConnectionKey}", connectionKey);
            return false;
        }
    }

    /// <summary>
    /// Gets connection info for compatibility
    /// </summary>
    public (bool isConnected, string sessionId, string version) GetConnectionInfo(string connectionKey)
    {
        return _connectionStateManager.GetConnectionInfo(connectionKey);
    }

    /// <summary>
    /// Disconnects and cleans up a connection
    /// </summary>
    public async Task DisconnectAsync(string connectionKey)
    {
        if (!_processes.TryRemove(connectionKey, out var process))
        {
            _logger.LogDebug("No process found for connection key {ConnectionKey}", connectionKey);
            return;
        }

        try
        {
            var connectionState = _connectionStateManager.GetConnectionState(connectionKey);
            var serverAddress = connectionState?.ConnectionInfo?.ServerAddress ?? "unknown";
            
            _logger.LogInformation("🔌 Disconnecting from {Server} ({ConnectionKey})", serverAddress, connectionKey);

            if (!process.HasExited)
            {
                // Try to disconnect gracefully from vCenter first
                try
                {
                    // Check if PowerCLI is loaded and has active connections before attempting disconnect
                    var checkScript = @"
                        try {
                            if (Get-Command 'Get-VIServer' -ErrorAction SilentlyContinue) { 
                                $connections = @(Get-VIServer -ErrorAction SilentlyContinue)
                                if ($connections.Count -gt 0) { 
                                    'POWERCLI_CONNECTED'
                                } else { 
                                    'POWERCLI_NO_CONNECTION' 
                                }
                            } else { 
                                'NO_POWERCLI' 
                            }
                        } catch {
                            'ERROR_CHECKING'
                        }";
                    
                    var checkResult = await _processManager.ExecuteCommandAsync(process, checkScript, TimeSpan.FromSeconds(5));
                    
                    if (checkResult.Contains("POWERCLI_CONNECTED"))
                    {
                        var disconnectScript = serverAddress != "unknown" 
                            ? $"Disconnect-VIServer -Server '{serverAddress}' -Force -Confirm:$false"
                            : "Disconnect-VIServer * -Force -Confirm:$false";
                        
                        await _processManager.ExecuteCommandAsync(process, disconnectScript, TimeSpan.FromSeconds(10));
                        _logger.LogInformation("✅ Disconnected from vCenter {Server}", serverAddress);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error during graceful vCenter disconnect for {ConnectionKey}", connectionKey);
                }

                // Close the PowerShell process
                await _processManager.TerminateProcessAsync(process, TimeSpan.FromSeconds(5));
            }

            // Update connection state
            _connectionStateManager.RemoveConnection(connectionKey);

            _logger.LogInformation("✅ Successfully cleaned up connection {ConnectionKey}", connectionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during disconnect cleanup for {ConnectionKey}", connectionKey);
        }
    }

    /// <summary>
    /// Disconnects all active connections
    /// </summary>
    public async Task DisconnectAllAsync()
    {
        var connectionKeys = _processes.Keys.ToList();
        _logger.LogInformation("🔌 Disconnecting {Count} active connections", connectionKeys.Count);

        var tasks = connectionKeys.Select(key => DisconnectAsync(key));
        await Task.WhenAll(tasks);

        _logger.LogInformation("✅ All connections disconnected successfully");
    }

    /// <summary>
    /// Disposes the service and cleans up all resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("🧹 Disposing PersistantVcenterConnectionService - cleaning up all connections");

        try
        {
            // Disconnect all connections synchronously
            DisconnectAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during service disposal");
        }
        finally
        {
            _disposed = true;
            _logger.LogInformation("✅ PersistantVcenterConnectionService disposed successfully");
        }
    }
    }