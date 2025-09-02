using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages PowerShell process creation, execution, and lifecycle
/// </summary>
public class PowerShellProcessManager : IDisposable
{
    private readonly ILogger<PowerShellProcessManager> _logger;
    private bool _disposed = false;

    public PowerShellProcessManager(ILogger<PowerShellProcessManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Represents a managed PowerShell process with input/output handling
    /// </summary>
    public class ManagedPowerShellProcess : IDisposable
    {
        public Process Process { get; }
        public StreamWriter StandardInput { get; }
        public StringBuilder OutputBuffer { get; } = new StringBuilder();
        public object LockObject { get; } = new object();
        public DateTime StartedAt { get; }
        public string ProcessId => Process?.Id.ToString() ?? "Unknown";
        public bool HasExited => Process?.HasExited ?? true;

        internal ManagedPowerShellProcess(Process process, DateTime startedAt)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
            StandardInput = process.StandardInput ?? throw new ArgumentNullException(nameof(process.StandardInput));
            StartedAt = startedAt;
        }

        public void Dispose()
        {
            try
            {
                StandardInput?.Dispose();
            }
            catch { }

            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            try
            {
                Process?.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// Creates and starts a new persistent PowerShell process
    /// </summary>
    public async Task<ManagedPowerShellProcess?> CreatePersistentProcessAsync()
    {
        try
        {
            _logger.LogInformation("Creating new persistent PowerShell process...");

            var process = await StartPowerShellProcessAsync();
            if (process == null)
            {
                return null;
            }

            var managedProcess = new ManagedPowerShellProcess(process, DateTime.UtcNow);

            // Set up output and error handling
            SetupProcessOutputHandling(managedProcess);

            _logger.LogInformation("✅ Created persistent PowerShell process (PID: {ProcessId})", managedProcess.ProcessId);
            return managedProcess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create persistent PowerShell process");
            return null;
        }
    }

    /// <summary>
    /// Executes a command in the managed PowerShell process with timeout
    /// </summary>
    public async Task<string> ExecuteCommandAsync(
        ManagedPowerShellProcess managedProcess,
        string command,
        TimeSpan timeout)
    {
        if (managedProcess == null)
            throw new ArgumentNullException(nameof(managedProcess));

        if (string.IsNullOrEmpty(command))
            throw new ArgumentException("Command cannot be null or empty", nameof(command));

        try
        {
            // Validate process is still running
            if (managedProcess.HasExited)
            {
                _logger.LogError("PowerShell process {ProcessId} has exited", managedProcess.ProcessId);
                return "ERROR: PowerShell process has exited";
            }

            // Clear output buffer and prepare for execution
            lock (managedProcess.LockObject)
            {
                managedProcess.OutputBuffer.Clear();
            }

            // Generate unique end marker for this command
            var endMarker = $"END_COMMAND_{Guid.NewGuid():N}";
            var commandWithMarker = $"{command}\nWrite-Output '{endMarker}'";

            _logger.LogDebug("Executing PowerShell command in process {ProcessId}", managedProcess.ProcessId);

            // Send command to process
            try
            {
                await managedProcess.StandardInput.WriteLineAsync(commandWithMarker);
                await managedProcess.StandardInput.FlushAsync();
                await Task.Delay(50); // Small delay for command processing
            }
            catch (IOException ioEx) when (ioEx.Message.Contains("pipe") || ioEx.Message.Contains("closed"))
            {
                _logger.LogError(ioEx, "Failed to send command - pipe closed for process {ProcessId}", managedProcess.ProcessId);
                return $"ERROR: Cannot send command - pipe closed: {ioEx.Message}";
            }

            // Wait for command completion with timeout
            var result = await WaitForCommandCompletionAsync(managedProcess, endMarker, timeout);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in process {ProcessId}", managedProcess.ProcessId);
            return $"ERROR: Command execution failed - {ex.Message}";
        }
    }

    /// <summary>
    /// Gracefully terminates a managed PowerShell process
    /// </summary>
    public async Task<bool> TerminateProcessAsync(ManagedPowerShellProcess managedProcess, TimeSpan gracefulTimeout)
    {
        if (managedProcess == null)
            return true;

        try
        {
            _logger.LogInformation("Terminating PowerShell process {ProcessId}...", managedProcess.ProcessId);

            if (!managedProcess.HasExited)
            {
                try
                {
                    // Try graceful exit first
                    await managedProcess.StandardInput.WriteLineAsync("exit");
                    await managedProcess.StandardInput.FlushAsync();

                    // Wait for graceful exit
                    var exitedGracefully = managedProcess.Process.WaitForExit((int)gracefulTimeout.TotalMilliseconds);
                    
                    if (exitedGracefully)
                    {
                        _logger.LogInformation("✅ PowerShell process {ProcessId} exited gracefully", managedProcess.ProcessId);
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("PowerShell process {ProcessId} did not exit gracefully, forcing termination", managedProcess.ProcessId);
                        managedProcess.Process.Kill(entireProcessTree: true);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during graceful termination of process {ProcessId}, forcing kill", managedProcess.ProcessId);
                    try
                    {
                        managedProcess.Process.Kill(entireProcessTree: true);
                        return false;
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "Failed to force kill process {ProcessId}", managedProcess.ProcessId);
                        return false;
                    }
                }
            }

            _logger.LogInformation("✅ PowerShell process {ProcessId} was already terminated", managedProcess.ProcessId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating PowerShell process {ProcessId}", managedProcess.ProcessId);
            return false;
        }
    }

    /// <summary>
    /// Gets process health information
    /// </summary>
    public ProcessHealthInfo GetProcessHealth(ManagedPowerShellProcess managedProcess)
    {
        if (managedProcess == null)
        {
            return new ProcessHealthInfo
            {
                IsHealthy = false,
                Status = "Process is null",
                RuntimeDuration = TimeSpan.Zero
            };
        }

        try
        {
            var runtimeDuration = DateTime.UtcNow - managedProcess.StartedAt;
            
            if (managedProcess.HasExited)
            {
                return new ProcessHealthInfo
                {
                    IsHealthy = false,
                    Status = $"Process has exited (Exit Code: {managedProcess.Process.ExitCode})",
                    RuntimeDuration = runtimeDuration,
                    ProcessId = managedProcess.ProcessId
                };
            }

            // Check if process is responsive (basic check)
            var memoryUsage = managedProcess.Process.WorkingSet64;
            var isResponding = managedProcess.Process.Responding;

            return new ProcessHealthInfo
            {
                IsHealthy = isResponding,
                Status = isResponding ? "Running and responsive" : "Running but not responding",
                RuntimeDuration = runtimeDuration,
                ProcessId = managedProcess.ProcessId,
                MemoryUsageBytes = memoryUsage
            };
        }
        catch (Exception ex)
        {
            return new ProcessHealthInfo
            {
                IsHealthy = false,
                Status = $"Error checking process health: {ex.Message}",
                RuntimeDuration = DateTime.UtcNow - managedProcess.StartedAt,
                ProcessId = managedProcess.ProcessId
            };
        }
    }

    /// <summary>
    /// Starts a PowerShell process with optimal configuration
    /// </summary>
    private async Task<Process?> StartPowerShellProcessAsync()
    {
        var powershellPaths = new[]
        {
            "pwsh.exe",                                    // PowerShell 7+ (preferred)
            @"C:\Program Files\PowerShell\7\pwsh.exe",    // PowerShell 7 explicit path
            "powershell.exe"                               // Windows PowerShell (fallback)
        };

        foreach (var psPath in powershellPaths)
        {
            try
            {
                _logger.LogDebug("Attempting to start PowerShell using: {Path}", psPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = psPath,
                    Arguments = "-NoProfile -NoExit -ExecutionPolicy Unrestricted -Command -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.CurrentDirectory
                };

                var process = Process.Start(startInfo);

                if (process != null && !process.HasExited)
                {
                    // Small delay to ensure process is fully initialized
                    await Task.Delay(100);

                    if (!process.HasExited)
                    {
                        _logger.LogInformation("✅ Started PowerShell process using {Path} (PID: {ProcessId})", psPath, process.Id);
                        return process;
                    }
                }

                _logger.LogDebug("Process started but exited immediately: {Path}", psPath);
                process?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to start PowerShell using {Path}: {Error}", psPath, ex.Message);
            }
        }

        _logger.LogError("❌ Failed to start any PowerShell executable");
        return null;
    }

    /// <summary>
    /// Sets up output and error data handling for the process
    /// </summary>
    private void SetupProcessOutputHandling(ManagedPowerShellProcess managedProcess)
    {
        // Handle standard output
        managedProcess.Process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (managedProcess.LockObject)
                {
                    managedProcess.OutputBuffer.AppendLine(e.Data);
                }
                _logger.LogTrace("PS Output [{ProcessId}]: {Output}", managedProcess.ProcessId, e.Data);
            }
        };

        // Handle standard error
        managedProcess.Process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("PS Error [{ProcessId}]: {Error}", managedProcess.ProcessId, e.Data);
            }
        };

        // Start async reading
        managedProcess.Process.BeginOutputReadLine();
        managedProcess.Process.BeginErrorReadLine();
    }

    /// <summary>
    /// Waits for command completion by monitoring for end marker
    /// </summary>
    private async Task<string> WaitForCommandCompletionAsync(
        ManagedPowerShellProcess managedProcess,
        string endMarker,
        TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        var lastOutputTime = startTime;
        const int pollIntervalMs = 100;

        while ((DateTime.UtcNow - startTime) < timeout)
        {
            await Task.Delay(pollIntervalMs);

            string currentOutput;
            lock (managedProcess.LockObject)
            {
                currentOutput = managedProcess.OutputBuffer.ToString();
            }

            // Check if we have any new output to reset the stale timeout
            if (currentOutput.Length > 0)
            {
                lastOutputTime = DateTime.UtcNow;
            }

            // Check for command completion
            if (currentOutput.Contains(endMarker))
            {
                var result = currentOutput.Replace(endMarker, "").Trim();
                _logger.LogDebug("Command completed in process {ProcessId} after {Duration}ms", 
                    managedProcess.ProcessId, (DateTime.UtcNow - startTime).TotalMilliseconds);
                return result;
            }

            // Check if process has exited unexpectedly
            if (managedProcess.HasExited)
            {
                _logger.LogError("PowerShell process {ProcessId} exited unexpectedly during command execution", 
                    managedProcess.ProcessId);
                return $"ERROR: PowerShell process exited unexpectedly (Exit Code: {managedProcess.Process.ExitCode})";
            }

            // Check for stale output (no output for extended period might indicate hanging)
            if ((DateTime.UtcNow - lastOutputTime).TotalSeconds > 30)
            {
                _logger.LogWarning("No output received from process {ProcessId} for 30 seconds", managedProcess.ProcessId);
            }
        }

        var timeoutDuration = DateTime.UtcNow - startTime;
        _logger.LogWarning("Command timed out in process {ProcessId} after {Duration}ms", 
            managedProcess.ProcessId, timeoutDuration.TotalMilliseconds);
        
        return $"ERROR: Command timed out after {timeout.TotalSeconds} seconds";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing PowerShellProcessManager");
        _disposed = true;
    }
}

/// <summary>
/// Process health information
/// </summary>
public class ProcessHealthInfo
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan RuntimeDuration { get; set; }
    public string ProcessId { get; set; } = string.Empty;
    public long MemoryUsageBytes { get; set; }
}