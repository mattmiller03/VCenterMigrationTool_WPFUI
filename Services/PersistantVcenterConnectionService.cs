﻿using Microsoft.Extensions.Logging;
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
            var importScript = PowerShellScriptBuilder.BuildPowerCLIImportScript();
            var importResult = await ExecuteCommandWithTimeoutAsync(connectionKey, importScript, TimeSpan.FromSeconds(90), skipConnectionCheck: true);

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

            // Configure PowerCLI settings
            _logger.LogInformation("Configuring PowerCLI settings for {ModuleType}...", loadedModuleType);
            var configScript = PowerShellScriptBuilder.BuildPowerCLIConfigurationScript(loadedModuleType);
            var configResult = await ExecuteCommandWithTimeoutAsync(connectionKey, configScript, TimeSpan.FromSeconds(30), skipConnectionCheck: true);
            
            if (!configResult.Contains("CONFIG_SUCCESS"))
            {
                _logger.LogWarning("PowerCLI configuration had issues but continuing. Output: {ConfigResult}", configResult);
            }
            else
            {
                _logger.LogInformation("PowerCLI configuration completed successfully");
            }

            // Create credential and connect
            _logger.LogInformation("Connecting to vCenter {Server}...", connectionInfo.ServerAddress);

            var connectScript = PowerShellScriptBuilder.BuildVCenterConnectionScript(connectionInfo, password, connectionKey);

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
                    
                    if (trimmedLine.StartsWith("DEBUG_ERROR_VAR:"))
                    {
                        _logger.LogError("PS_ERROR_VAR: {Line}", trimmedLine.Replace("DEBUG_ERROR_VAR:", "").Trim());
                    }
                    else if (trimmedLine.StartsWith("DEBUG_"))
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
                fullCommand = PowerShellScriptBuilder.BuildScriptWithEndMarker(command, endMarker);
            }
            else 
            {
                // Ensure the correct vCenter connection is active for this command
                var validationScript = PowerShellScriptBuilder.BuildConnectionValidationScript(connectionKey);
                var combinedScript = $@"
{validationScript}

# Execute the requested command if connection is valid
{command}";
                fullCommand = PowerShellScriptBuilder.BuildScriptWithEndMarker(combinedScript, endMarker);
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