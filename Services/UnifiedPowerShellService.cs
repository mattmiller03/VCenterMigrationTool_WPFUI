using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Unified PowerShell service that consolidates all PowerShell functionality
/// Combines process management, session management, PowerCLI configuration, and script execution
/// </summary>
public class UnifiedPowerShellService : IDisposable
{
    private readonly ILogger<UnifiedPowerShellService> _logger;
    private readonly ConfigurationService _configurationService;
    private readonly PowerShellLoggingService _psLoggingService;
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private readonly ConcurrentDictionary<string, ManagedPowerShellProcess> _persistentSessions = new();
    private readonly Timer _cleanupTimer;
    private Runspace? _sharedRunspace;
    private bool _disposed = false;

    // PowerCLI configuration tracking
    private static bool _powerCLIInstalled = false;
    private static bool _powerCLIInstallationChecked = false;

    public UnifiedPowerShellService(
        ILogger<UnifiedPowerShellService> logger,
        ConfigurationService configurationService,
        PowerShellLoggingService psLoggingService)
    {
        _logger = logger;
        _configurationService = configurationService;
        _psLoggingService = psLoggingService;

        // Initialize cleanup timer
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // Validate PowerShell availability
        ValidatePowerShellAvailability();

        _logger.LogInformation("UnifiedPowerShellService initialized successfully");
    }

    #region Core PowerShell Management

    /// <summary>
    /// Managed PowerShell process with enhanced capabilities
    /// </summary>
    public class ManagedPowerShellProcess : IDisposable
    {
        public Process Process { get; }
        public StreamWriter StandardInput { get; }
        public StringBuilder OutputBuffer { get; } = new StringBuilder();
        public object LockObject { get; } = new object();
        public DateTime StartedAt { get; }
        public DateTime LastActivityAt { get; set; }
        public string ProcessId => Process?.Id.ToString() ?? "Unknown";
        public bool HasExited => Process?.HasExited ?? true;
        public bool IsPowerCLIConfigured { get; set; }
        public string ModuleType { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();

        internal ManagedPowerShellProcess(Process process, DateTime startedAt)
        {
            Process = process ?? throw new ArgumentNullException(nameof(process));
            StandardInput = process.StandardInput ?? throw new ArgumentNullException(nameof(process.StandardInput));
            StartedAt = startedAt;
            LastActivityAt = startedAt;
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
    /// Validates PowerShell availability on the system
    /// </summary>
    private void ValidatePowerShellAvailability()
    {
        try
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
                    if (TryValidatePowerShellPath(psPath))
                    {
                        _logger.LogInformation("✅ PowerShell validated: {Path}", psPath);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "PowerShell path validation failed: {Path}", psPath);
                }
            }

            var errorMessage = "No PowerShell executable found. Please install PowerShell 7+ or ensure Windows PowerShell is available.";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate PowerShell availability");
            throw;
        }
    }

    /// <summary>
    /// Tries to validate a specific PowerShell path
    /// </summary>
    private bool TryValidatePowerShellPath(string psPath)
    {
        if (psPath.Contains("\\"))
        {
            return File.Exists(psPath);
        }
        else
        {
            // Check PATH for executable
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            return paths.Any(path =>
            {
                try
                {
                    var fullPath = Path.Combine(path, psPath);
                    return File.Exists(fullPath);
                }
                catch
                {
                    return false;
                }
            });
        }
    }

    #endregion

    #region Process Management

    /// <summary>
    /// Creates a new managed PowerShell process
    /// </summary>
    public async Task<ManagedPowerShellProcess?> CreateProcessAsync()
    {
        try
        {
            _logger.LogDebug("Creating new PowerShell process...");

            var process = await StartPowerShellProcessAsync();
            if (process == null)
            {
                return null;
            }

            var managedProcess = new ManagedPowerShellProcess(process, DateTime.UtcNow);

            // Set up output and error handling
            SetupProcessOutputHandling(managedProcess);

            // Track the process
            _activeProcesses[process.Id] = process;

            _logger.LogInformation("✅ Created PowerShell process (PID: {ProcessId})", managedProcess.ProcessId);
            return managedProcess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create PowerShell process");
            return null;
        }
    }

    /// <summary>
    /// Creates a persistent PowerShell process for long-running operations
    /// </summary>
    public async Task<ManagedPowerShellProcess?> CreatePersistentProcessAsync(string sessionKey)
    {
        try
        {
            // Remove any existing session
            await DisposePersistentProcessAsync(sessionKey);

            var managedProcess = await CreateProcessAsync();
            if (managedProcess != null)
            {
                _persistentSessions[sessionKey] = managedProcess;
                _logger.LogInformation("✅ Created persistent PowerShell session: {SessionKey}", sessionKey);
            }

            return managedProcess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create persistent PowerShell process for session {SessionKey}", sessionKey);
            return null;
        }
    }

    /// <summary>
    /// Gets an existing persistent process
    /// </summary>
    public ManagedPowerShellProcess? GetPersistentProcess(string sessionKey)
    {
        if (_persistentSessions.TryGetValue(sessionKey, out var process) && !process.HasExited)
        {
            return process;
        }

        return null;
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
                        _logger.LogDebug("✅ Started PowerShell process using {Path} (PID: {ProcessId})", psPath, process.Id);
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
                    managedProcess.LastActivityAt = DateTime.UtcNow;
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

    #endregion

    #region PowerCLI Configuration

    /// <summary>
    /// PowerCLI configuration result
    /// </summary>
    public class PowerCLIConfigResult
    {
        public bool Success { get; set; }
        public string ModuleType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Configures PowerCLI modules in the specified process
    /// </summary>
    public async Task<PowerCLIConfigResult> ConfigurePowerCLIAsync(
        ManagedPowerShellProcess managedProcess,
        bool bypassModuleCheck = false)
    {
        if (managedProcess == null)
            throw new ArgumentNullException(nameof(managedProcess));

        var result = new PowerCLIConfigResult();

        try
        {
            _logger.LogInformation("Configuring PowerCLI modules in process {ProcessId}...", managedProcess.ProcessId);

            if (bypassModuleCheck)
            {
                _logger.LogInformation("Bypassing PowerCLI module configuration due to bypassModuleCheck=true");
                result.Success = true;
                result.ModuleType = "Bypass Mode";
                result.Message = "PowerCLI configuration bypassed - limited functionality available";
                result.Warnings.Add("PowerCLI modules not loaded - only basic PowerShell commands available");
                return result;
            }

            // Step 1: Import PowerCLI modules
            var importResult = await ImportPowerCLIModulesAsync(managedProcess);
            if (!importResult.Success)
            {
                result.Success = false;
                result.Message = importResult.Message;
                result.Errors.AddRange(importResult.Errors);
                return result;
            }

            result.ModuleType = importResult.ModuleType;
            result.Warnings.AddRange(importResult.Warnings);

            // Step 2: Configure PowerCLI settings
            var configResult = await ApplyPowerCLIConfigurationAsync(managedProcess, importResult.ModuleType);
            if (!configResult.Success)
            {
                result.Success = false;
                result.Message = $"Module import succeeded but configuration failed: {configResult.Message}";
                result.Errors.AddRange(configResult.Errors);
                return result;
            }

            result.Warnings.AddRange(configResult.Warnings);

            // Mark process as PowerCLI configured
            managedProcess.IsPowerCLIConfigured = true;
            managedProcess.ModuleType = result.ModuleType;

            // Success
            result.Success = true;
            result.Message = $"PowerCLI successfully configured using {result.ModuleType}";
            
            _logger.LogInformation("✅ PowerCLI configuration completed successfully using {ModuleType}", result.ModuleType);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring PowerCLI in process {ProcessId}", managedProcess.ProcessId);
            
            result.Success = false;
            result.Message = $"PowerCLI configuration failed: {ex.Message}";
            result.Errors.Add($"Exception: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Imports PowerCLI modules using multiple fallback strategies
    /// </summary>
    private async Task<PowerCLIConfigResult> ImportPowerCLIModulesAsync(ManagedPowerShellProcess managedProcess)
    {
        try
        {
            _logger.LogDebug("Importing PowerCLI modules in process {ProcessId}...", managedProcess.ProcessId);

            var importScript = PowerShellScriptBuilder.BuildPowerCLIImportScript();
            var output = await ExecuteCommandAsync(managedProcess, importScript, TimeSpan.FromSeconds(90));

            var result = new PowerCLIConfigResult();

            if (output.Contains("MODULES_LOADED:"))
            {
                // Extract module type from output
                var moduleTypeMatch = System.Text.RegularExpressions.Regex.Match(output, @"MODULES_LOADED:(.+?)(?:\r?\n|$)");
                var moduleType = moduleTypeMatch.Success ? moduleTypeMatch.Groups[1].Value.Trim() : "Unknown";

                result.Success = true;
                result.ModuleType = moduleType;
                result.Message = $"Successfully loaded PowerCLI modules: {moduleType}";

                _logger.LogInformation("✅ PowerCLI modules imported successfully: {ModuleType}", moduleType);
            }
            else
            {
                result.Success = false;
                result.Message = "Failed to import PowerCLI modules";
                result.Errors.Add("No MODULES_LOADED confirmation found in output");
                
                _logger.LogError("❌ Failed to import PowerCLI modules. Output: {Output}", output);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing PowerCLI modules in process {ProcessId}", managedProcess.ProcessId);
            
            return new PowerCLIConfigResult
            {
                Success = false,
                Message = $"PowerCLI module import failed: {ex.Message}",
                Errors = { $"Exception: {ex.Message}" }
            };
        }
    }

    /// <summary>
    /// Applies PowerCLI configuration settings
    /// </summary>
    private async Task<PowerCLIConfigResult> ApplyPowerCLIConfigurationAsync(
        ManagedPowerShellProcess managedProcess,
        string moduleType)
    {
        try
        {
            _logger.LogDebug("Applying PowerCLI configuration in process {ProcessId}...", managedProcess.ProcessId);

            var configScript = PowerShellScriptBuilder.BuildPowerCLIConfigurationScript(moduleType);
            var output = await ExecuteCommandAsync(managedProcess, configScript, TimeSpan.FromSeconds(30));

            var result = new PowerCLIConfigResult();

            if (output.Contains("CONFIG_SUCCESS"))
            {
                result.Success = true;
                result.Message = "PowerCLI configuration applied successfully";
                _logger.LogInformation("✅ PowerCLI configuration applied successfully");
            }
            else
            {
                result.Success = false;
                result.Message = "PowerCLI configuration may have failed";
                result.Warnings.Add("No CONFIG_SUCCESS confirmation found, but continuing");
                _logger.LogWarning("PowerCLI configuration completed but without explicit success confirmation");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying PowerCLI configuration in process {ProcessId}", managedProcess.ProcessId);
            
            return new PowerCLIConfigResult
            {
                Success = false,
                Message = $"PowerCLI configuration failed: {ex.Message}",
                Errors = { $"Exception: {ex.Message}" }
            };
        }
    }

    #endregion

    #region Command Execution

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
            
            // Update activity timestamp
            managedProcess.LastActivityAt = DateTime.UtcNow;
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in process {ProcessId}", managedProcess.ProcessId);
            return $"ERROR: Command execution failed - {ex.Message}";
        }
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

    #endregion

    #region Session Management

    /// <summary>
    /// Initializes a shared PowerShell runspace for session-based operations
    /// </summary>
    public async Task<bool> InitializeSharedRunspaceAsync()
    {
        try
        {
            if (_sharedRunspace != null && _sharedRunspace.RunspaceStateInfo.State == RunspaceState.Opened)
            {
                _logger.LogDebug("Shared runspace already initialized");
                return true;
            }

            _logger.LogInformation("Initializing shared PowerShell runspace...");

            var initialSessionState = InitialSessionState.CreateDefault();
            _sharedRunspace = RunspaceFactory.CreateRunspace(initialSessionState);
            _sharedRunspace.Open();

            _logger.LogInformation("✅ Shared PowerShell runspace initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize shared PowerShell runspace");
            return false;
        }
    }

    /// <summary>
    /// Executes a command in the shared runspace
    /// </summary>
    public async Task<string> ExecuteInSharedRunspaceAsync(string command)
    {
        if (_sharedRunspace == null)
        {
            await InitializeSharedRunspaceAsync();
        }

        if (_sharedRunspace == null)
        {
            return "ERROR: Failed to initialize shared runspace";
        }

        try
        {
            using var powerShell = PowerShell.Create();
            powerShell.Runspace = _sharedRunspace;
            powerShell.AddScript(command);

            var results = await Task.Run(() => powerShell.Invoke());
            
            if (powerShell.HadErrors)
            {
                var errors = string.Join("\n", powerShell.Streams.Error.Select(e => e.ToString()));
                return $"ERROR: {errors}";
            }

            return string.Join("\n", results.Select(r => r.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command in shared runspace");
            return $"ERROR: {ex.Message}";
        }
    }

    #endregion

    #region Process Lifecycle

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
        finally
        {
            // Remove from active processes tracking
            if (int.TryParse(managedProcess.ProcessId, out var processId))
            {
                _activeProcesses.TryRemove(processId, out _);
            }
        }
    }

    /// <summary>
    /// Disposes a persistent PowerShell process
    /// </summary>
    public async Task<bool> DisposePersistentProcessAsync(string sessionKey)
    {
        if (_persistentSessions.TryRemove(sessionKey, out var managedProcess))
        {
            var result = await TerminateProcessAsync(managedProcess, TimeSpan.FromSeconds(5));
            managedProcess.Dispose();
            _logger.LogInformation("✅ Disposed persistent PowerShell session: {SessionKey}", sessionKey);
            return result;
        }

        return true;
    }

    /// <summary>
    /// Periodic cleanup of orphaned processes
    /// </summary>
    private void PerformCleanup(object? state)
    {
        try
        {
            var cleanedCount = 0;
            var toRemove = new List<int>();

            foreach (var (processId, process) in _activeProcesses)
            {
                try
                {
                    if (process.HasExited)
                    {
                        toRemove.Add(processId);
                        cleanedCount++;
                    }
                }
                catch
                {
                    toRemove.Add(processId);
                    cleanedCount++;
                }
            }

            foreach (var processId in toRemove)
            {
                _activeProcesses.TryRemove(processId, out _);
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} terminated PowerShell processes", cleanedCount);
            }

            // Clean up stale persistent sessions
            var staleSessions = _persistentSessions.Where(kvp => kvp.Value.HasExited).ToList();
            foreach (var (sessionKey, managedProcess) in staleSessions)
            {
                if (_persistentSessions.TryRemove(sessionKey, out _))
                {
                    managedProcess.Dispose();
                    _logger.LogInformation("Cleaned up stale persistent session: {SessionKey}", sessionKey);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during PowerShell process cleanup");
        }
    }

    /// <summary>
    /// Gets the count of active PowerShell processes
    /// </summary>
    public int GetActiveProcessCount()
    {
        return _activeProcesses.Count + _persistentSessions.Count;
    }

    /// <summary>
    /// Forces cleanup of all active PowerShell processes
    /// </summary>
    public void CleanupAllProcesses()
    {
        try
        {
            _logger.LogInformation("Force cleaning up {Count} active PowerShell processes", _activeProcesses.Count);

            // Clean up regular processes
            foreach (var (processId, process) in _activeProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error cleaning up process {ProcessId}", processId);
                }
            }

            _activeProcesses.Clear();

            // Clean up persistent sessions
            foreach (var (sessionKey, managedProcess) in _persistentSessions)
            {
                try
                {
                    managedProcess.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error cleaning up persistent session {SessionKey}", sessionKey);
                }
            }

            _persistentSessions.Clear();

            _logger.LogInformation("✅ All PowerShell processes cleaned up successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during force cleanup of PowerShell processes");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing UnifiedPowerShellService - cleaning up all resources");

        try
        {
            // Stop cleanup timer
            _cleanupTimer?.Dispose();

            // Clean up all processes
            CleanupAllProcesses();

            // Dispose shared runspace
            if (_sharedRunspace != null)
            {
                try
                {
                    _sharedRunspace.Close();
                    _sharedRunspace.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing shared runspace");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during UnifiedPowerShellService disposal");
        }
        finally
        {
            _disposed = true;
            _logger.LogInformation("✅ UnifiedPowerShellService disposed successfully");
        }
    }

    #endregion
}