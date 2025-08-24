using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

            // Import PowerCLI if needed
            if (!bypassModuleCheck)
                {
                _logger.LogInformation("Importing PowerCLI modules in persistent session...");
                var importResult = await ExecuteCommandWithTimeoutAsync(connectionKey, @"
                    Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                    Write-Output 'MODULES_LOADED'
                ", TimeSpan.FromSeconds(30));

                if (!importResult.Contains("MODULES_LOADED"))
                    {
                    _logger.LogError("Failed to load PowerCLI modules");
                    await DisconnectAsync(connectionKey);
                    return (false, "Failed to load PowerCLI modules", null);
                    }
                }
            else
                {
                _logger.LogInformation("Bypassing module import (assumed already loaded)");
                }

            // Create credential and connect
            _logger.LogInformation("Connecting to vCenter {Server}...", connectionInfo.ServerAddress);

            var connectScript = $@"
                # Create credential
                $password = '{password.Replace("'", "''")}'
                $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
                $credential = New-Object System.Management.Automation.PSCredential('{connectionInfo.Username.Replace("'", "''")}', $securePassword)
                $password = $null  # Clear from memory
                
                # Connect to vCenter
                try {{
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
                        
                        # Quick test
                        $vmCount = (Get-VM -Server $connection -ErrorAction SilentlyContinue).Count
                        Write-Output ""VM_COUNT:$vmCount""
                    }} else {{
                        Write-Output ""CONNECTION_FAILED: Not connected""
                    }}
                }} catch {{
                    Write-Output ""CONNECTION_FAILED: $($_.Exception.Message)""
                }}
            ";

            var connectResult = await ExecuteCommandWithTimeoutAsync(connectionKey, connectScript, TimeSpan.FromSeconds(60));

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
                var errorMessage = connectResult.Contains("CONNECTION_FAILED:")
                    ? connectResult.Substring(connectResult.IndexOf("CONNECTION_FAILED:") + 18).Trim()
                    : "Unknown connection error";

                _logger.LogError("Connection failed: {Error}", errorMessage);
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
    private async Task<string> ExecuteCommandWithTimeoutAsync (string connectionKey, string command, TimeSpan timeout)
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
            
            // Ensure the correct vCenter connection is active for this command
            var connectionVarName = $"VIConnection_{connectionKey.Replace("-", "_")}";
            var fullCommand = $@"
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

            // Send the command
            await connection.StandardInput.WriteLineAsync(fullCommand);
            await connection.StandardInput.FlushAsync();

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
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing command");
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Executes a command in the context of a persistent connection
    /// </summary>
    public async Task<string> ExecuteCommandAsync (string connectionKey, string command)
        {
        return await ExecuteCommandWithTimeoutAsync(connectionKey, command, TimeSpan.FromMinutes(5));
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
                TimeSpan.FromSeconds(5));

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
                // Try to disconnect gracefully
                try
                    {
                    await connection.StandardInput.WriteLineAsync(
                        $"Disconnect-VIServer -Server {connection.ConnectionInfo.ServerAddress} -Force -Confirm:$false");
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