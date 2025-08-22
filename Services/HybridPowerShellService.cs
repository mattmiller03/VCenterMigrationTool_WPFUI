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
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.Services;

public class HybridPowerShellService : IDisposable
    {
    private readonly ILogger<HybridPowerShellService> _logger;
    private readonly ConfigurationService _configurationService;
    private readonly PowerShellLoggingService _psLoggingService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly PersistentExternalConnectionService _persistentConnectionService;
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private readonly Timer _cleanupTimer;
    private IErrorHandlingService? _errorHandlingService;
    private bool _disposed = false;

    /// <summary>
    /// Validate that PowerShell 7+ is available on the system
    /// </summary>
    private void ValidatePowerShell7Available ()
        {
        try
            {
            var powershell7Paths = new[]
            {
                "pwsh.exe",
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Microsoft\WindowsApps\pwsh.exe",
                @"C:\Program Files\PowerShell\7-preview\pwsh.exe"
            };

            foreach (var psPath in powershell7Paths)
                {
                try
                    {
                    if (psPath.Contains("\\") && File.Exists(psPath))
                        {
                        _logger.LogInformation("PowerShell 7+ found at: {Path}", psPath);
                        return;
                        }
                    else if (!psPath.Contains("\\"))
                        {
                        // Check PATH for pwsh.exe
                        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                        var paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var path in paths)
                            {
                            try
                                {
                                var fullPath = Path.Combine(path, psPath);
                                if (File.Exists(fullPath))
                                    {
                                    _logger.LogInformation("PowerShell 7+ found in PATH: {Path}", fullPath);
                                    return;
                                    }
                                }
                            catch
                                {
                                // Skip invalid paths
                                }
                            }
                        }
                    }
                catch (Exception ex)
                    {
                    _logger.LogDebug(ex, "Error checking PowerShell 7+ path: {Path}", psPath);
                    }
                }

            // If we get here, no PowerShell 7+ was found
            var errorMessage = "PowerShell 7+ is required but not found on this system. " +
                              "Please install PowerShell 7+ from: https://github.com/PowerShell/PowerShell/releases";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to validate PowerShell 7+ availability");
            throw;
            }
        }

    // Add this method to set the error handling service (called from DI)
    public void SetErrorHandlingService(IErrorHandlingService errorHandlingService)
    {
        _errorHandlingService = errorHandlingService;
    }
    /// <summary>
    /// Static flag to track PowerCLI availability (set by settings page)
    /// </summary>
    public static bool PowerCliConfirmedInstalled { get; set; } = false;

    public HybridPowerShellService (
        ILogger<HybridPowerShellService> logger,
        ConfigurationService configurationService,
        PowerShellLoggingService psLoggingService,
        SharedConnectionService sharedConnectionService,
        PersistentExternalConnectionService persistentConnectionService,
        IErrorHandlingService errorHandlingService)
        {
        _logger = logger;
        _configurationService = configurationService;
        _psLoggingService = psLoggingService;
        _sharedConnectionService = sharedConnectionService;
        _persistentConnectionService = persistentConnectionService;
        _errorHandlingService = errorHandlingService;

        // FIXED: Load PowerCLI status from persistent storage on startup
        LoadPowerCliStatus();

        // Validate PowerShell 7+ is available
        ValidatePowerShell7Available();

        _cleanupTimer = new Timer(CleanupOrphanedProcesses, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

    /// <summary>
    /// Load PowerCLI installation status from persistent storage
    /// </summary>
    private void LoadPowerCliStatus ()
        {
        try
            {
            var appConfig = _configurationService.GetConfiguration();

            // We'll add a PowerCliConfirmed property to the config
            // For now, check if we can detect it automatically on startup

            // Quick check: if PowerCLI module is available in PowerShell 7+, set the flag
            Task.Run(async () =>
            {
                try
                    {
                    var quickCheck =
                        await RunCommandAsync(
                            "if (Get-Module -ListAvailable -Name 'VMware.PowerCLI') { 'true' } else { 'false' }");
                    if (quickCheck?.Trim().ToLower() == "true")
                        {
                        PowerCliConfirmedInstalled = true;
                        _logger.LogInformation("STARTUP: PowerCLI detected and confirmed installed on startup");
                        SavePowerCliStatus(true);
                        }
                    else
                        {
                        _logger.LogInformation("STARTUP: PowerCLI not detected on startup");
                        }
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "STARTUP: Could not check PowerCLI status on startup");
                    }
            });
            }
        catch (Exception ex)
            {
            _logger.LogWarning(ex, "Could not load PowerCLI status from configuration");
            }
        }

    /// <summary>
    /// Save PowerCLI installation status to persistent storage
    /// </summary>
    public void SavePowerCliStatus (bool isInstalled)
        {
        try
            {
            PowerCliConfirmedInstalled = isInstalled;
            _logger.LogInformation("PERSISTENCE: PowerCLI status saved as {Status}", isInstalled);

            // TODO: In a future update, we could save this to the configuration file
            // For now, the static flag + startup detection should work
            }
        catch (Exception ex)
            {
            _logger.LogWarning(ex, "Could not save PowerCLI status to configuration");
            }
        }

    /// <summary>
    /// Determines whether to use internal or external PowerShell based on script requirements
    /// </summary>
    private bool ShouldUseExternalPowerShell (string scriptPath, Dictionary<string, object> parameters)
        {
        // ALWAYS use external PowerShell due to SDK compatibility issues
        // The internal PowerShell SDK has dependency conflicts in this application
        return true;
        }

    // Helper method to extract a meaningful name from a command
    private string ExtractCommandName (string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Unknown-Command";

        // Get the first line and first few words
        var firstLine = command.Split('\n', '\r')[0].Trim();
        var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length > 0)
        {
            // For cmdlets like Get-Module, Test-Connection, etc.
            var firstWord = words[0];
            if (firstWord.Contains('-'))
            {
                return firstWord;
            }

            // For other commands, take first 2-3 words
            return string.Join("-", words.Take(Math.Min(3, words.Length)));
        }

        return "PowerShell-Command";
    }

    /// <summary>
    /// Run a PowerShell script using the PowerShell SDK for better parameter handling and reliability
    /// </summary>
    private async Task<string> RunScriptWithSDKAsync(string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
    {
        var scriptName = Path.GetFileName(scriptPath);
        var sessionId = _psLoggingService.StartScriptLogging(scriptName);
        
        try
        {
            _logger.LogInformation("Executing script with PowerShell SDK: {ScriptPath}", scriptPath);
            _psLoggingService.LogParameters(sessionId, scriptName, parameters);

            if (!File.Exists(scriptPath))
            {
                var error = $"Script not found: {scriptPath}";
                _logger.LogError(error);
                _psLoggingService.LogScriptError(sessionId, scriptName, error);
                return $"ERROR: {error}";
            }

            // Create PowerShell instance with runspace
            using var powerShell = PowerShell.Create();
            
            // Configure runspace to allow execution
            powerShell.Runspace.SessionStateProxy.SetVariable("ExecutionPolicy", "Bypass");
            
            // Set PSScriptRoot variable so scripts can import dependencies
            var scriptDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                powerShell.Runspace.SessionStateProxy.SetVariable("PSScriptRoot", scriptDirectory);
                _logger.LogDebug("Set PSScriptRoot to: {Directory}", scriptDirectory);
                
                // Pre-load Write-ScriptLog.ps1 if it exists in the same directory
                var logScriptPath = Path.Combine(scriptDirectory, "Write-ScriptLog.ps1");
                if (File.Exists(logScriptPath))
                {
                    var logScriptContent = await File.ReadAllTextAsync(logScriptPath);
                    powerShell.AddScript(logScriptContent);
                    _logger.LogDebug("Pre-loaded Write-ScriptLog.ps1 from: {LogScriptPath}", logScriptPath);
                }
            }
            else
            {
                _logger.LogWarning("Could not determine script directory for: {ScriptPath}", scriptPath);
            }
            
            // Read and execute script content
            var scriptContent = await File.ReadAllTextAsync(scriptPath);
            powerShell.AddScript(scriptContent);

            // Add parameters properly
            foreach (var param in parameters)
            {
                if (param.Key == "LogPath" && string.IsNullOrEmpty(logPath))
                {
                    logPath = param.Value?.ToString();
                }
                
                _logger.LogDebug("Adding parameter: {Key} = {Value}", param.Key, 
                    IsSensitiveParameter(param.Key) ? "***HIDDEN***" : param.Value);
                
                powerShell.AddParameter(param.Key, param.Value);
            }

            // Execute script asynchronously
            var results = new List<string>();
            var errors = new List<string>();

            var pipelineObjects = await Task.Run(() => powerShell.Invoke());

            // Collect output
            foreach (var obj in pipelineObjects)
            {
                if (obj != null)
                {
                    results.Add(obj.ToString());
                }
            }

            // Collect errors
            if (powerShell.HadErrors)
            {
                foreach (var error in powerShell.Streams.Error)
                {
                    errors.Add(error.ToString());
                    _logger.LogError("PowerShell Error: {Error}", error.ToString());
                }
            }

            // Log warnings
            foreach (var warning in powerShell.Streams.Warning)
            {
                _logger.LogWarning("PowerShell Warning: {Warning}", warning.Message);
            }

            // Combine output
            var output = string.Join(Environment.NewLine, results);
            
            if (errors.Count > 0)
            {
                var errorOutput = string.Join(Environment.NewLine, errors);
                _psLoggingService.LogScriptError(sessionId, scriptName, errorOutput);
                
                if (string.IsNullOrEmpty(output))
                {
                    output = $"ERROR: {errorOutput}";
                }
            }

            _psLoggingService.LogScriptOutput(sessionId, scriptName, output, "INFO");
            _psLoggingService.EndScriptLogging(sessionId, scriptName, errors.Count == 0, 
                errors.Count == 0 ? "Script completed successfully" : $"Script completed with {errors.Count} errors");

            return output;
        }
        catch (Exception ex)
        {
            var error = $"SDK execution failed: {ex.Message}";
            _logger.LogError(ex, error);
            _psLoggingService.LogScriptError(sessionId, scriptName, error);
            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, error);
            return $"ERROR: {error}";
        }
    }

    public async Task<string> RunScriptAsync (string scriptPath, Dictionary<string, object> parameters,
        string? logPath = null)
        {
        // Check if this is a command rather than a script file
        if (!scriptPath.Contains("Scripts\\") && !scriptPath.EndsWith(".ps1"))
            {
            return await RunCommandAsync(scriptPath, parameters);
            }

        // Auto-generate log path if not provided
        if (string.IsNullOrEmpty(logPath))
            {
            var scriptName = Path.GetFileNameWithoutExtension(scriptPath);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logPath = Path.Combine("Logs", $"{scriptName}_{timestamp}.log");
            }

        // Use PowerShell SDK for better reliability and parameter handling
        _logger.LogDebug("Using PowerShell SDK for script: {ScriptPath}", scriptPath);
        _logger.LogDebug("Script log will be written to: {LogPath}", logPath);
        return await RunScriptWithSDKAsync(scriptPath, parameters, logPath);
        }

    /// <summary>
    /// Enhanced method that automatically adds BypassModuleCheck when PowerCLI is confirmed
    /// </summary>
    public async Task<string> RunScriptOptimizedAsync (string scriptPath, Dictionary<string, object> parameters,
        string? logPath = null)
        {
        // Clone parameters to avoid modifying the original
        var optimizedParameters = new Dictionary<string, object>(parameters);

        // DEBUG: Log the current state
        _logger.LogInformation("DEBUG: PowerCliConfirmedInstalled = {PowerCliConfirmed}", PowerCliConfirmedInstalled);
        _logger.LogInformation("DEBUG: IsPowerCliScript({ScriptPath}) = {IsPowerCli}", scriptPath,
            IsPowerCliScript(scriptPath));

        // Add bypass flag for PowerCLI scripts when we know it's installed
        if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
            {
            optimizedParameters["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: Adding BypassModuleCheck=true for script: {ScriptPath}", scriptPath);

            // SECURE: Log parameters without sensitive data
            var safeParams = optimizedParameters
                .Where(p => !IsSensitiveParameter(p.Key))
                .Select(p => $"{p.Key}={p.Value}");
            _logger.LogInformation("DEBUG: Final parameters (excluding sensitive): {Parameters}",
                string.Join(", ", safeParams));
            }
        else
            {
            _logger.LogInformation("DEBUG: NOT adding BypassModuleCheck for script: {ScriptPath}", scriptPath);
            }

        return await RunScriptAsync(scriptPath, optimizedParameters, logPath);
        }

    /// <summary>
    /// Enhanced method for object deserialization with bypass optimization
    /// </summary>
    public async Task<T?> RunScriptAndGetObjectOptimizedAsync<T> (string scriptPath,
        Dictionary<string, object> parameters, string? logPath = null)
        {
        // Clone parameters to avoid modifying the original
        var optimizedParameters = new Dictionary<string, object>(parameters);

        // Add bypass flag for PowerCLI scripts when we know it's installed
        if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
            {
            optimizedParameters["BypassModuleCheck"] = true;
            _logger.LogDebug("Adding BypassModuleCheck=true for script: {ScriptPath}", scriptPath);
            }

        return await RunScriptAndGetObjectAsync<T>(scriptPath, optimizedParameters, logPath);
        }

    /// <summary>
    /// Enhanced method for collection deserialization with bypass optimization  
    /// </summary>
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsOptimizedAsync<T> (string scriptPath,
        Dictionary<string, object> parameters,
        string? logPath = null)
        {
        // Clone parameters to avoid modifying the original
        var optimizedParameters = new Dictionary<string, object>(parameters);

        // Add bypass flag for PowerCLI scripts when we know it's installed
        if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
            {
            optimizedParameters["BypassModuleCheck"] = true;
            _logger.LogDebug("Adding BypassModuleCheck=true for script: {ScriptPath}", scriptPath);
            }

        return await RunScriptAndGetObjectsAsync<T>(scriptPath, optimizedParameters, logPath);
        }

    /// <summary>
    /// Determines if a script requires PowerCLI
    /// </summary>
    private bool IsPowerCliScript (string scriptPath)
        {
        var powerCliScripts = new[]
        {
            "Test-vCenterConnection.ps1",
            "Get-VMs.ps1",
            "Get-VmsForMigration.ps1",
            "Get-TargetResources.ps1",
            "Get-EsxiHosts.ps1",
            "Get-NetworkTopology.ps1",
            "Get-Clusters.ps1",
            "Get-ClusterItems.ps1",
            "Get-VCenterObjects.ps1",
            "Migrate-VCenterObject.ps1",
            "Move-EsxiHost.ps1",
            "Move-VM.ps1",
            "Export-vCenterConfig.ps1",
            "Test-VMNetwork.ps1",
            // Add these new VM backup scripts
            "BackupVMConfigurations.ps1",
            "RestoreVMConfigurations.ps1",
            "ValidateVMBackups.ps1",
            "Get-VMsForBackup.ps1",
            "Get-VMNetworkAdapters.ps1",
            "write-scriptlog.ps1",
            "Backup-ESXiHostConfig.ps1"
        };

        var scriptName = Path.GetFileName(scriptPath);
        return powerCliScripts.Any(s => s.Equals(scriptName, StringComparison.OrdinalIgnoreCase));
        }

    /// <summary>
    /// Determines if a parameter contains sensitive data that should not be logged
    /// </summary>
    private bool IsSensitiveParameter (string parameterName)
        {
        var sensitiveParams = new[] { "Password", "password", "pwd", "secret", "token", "key" };
        return sensitiveParams.Any(s => parameterName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

    // Enhanced RunScriptExternalAsync with PowerShell Logging Integration
    private async Task<string> RunScriptExternalAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
            // Ensure LogPath is always provided from configuration
            parameters = EnsureLogPathInParameters(parameters);

            string fullScriptPath = Path.GetFullPath(scriptPath);
            string scriptName = Path.GetFileName(scriptPath);

        _logger.LogDebug("Starting external PowerShell script execution: {ScriptPath}", fullScriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        // Start PowerShell logging session with proper script name
        var sessionId = _psLoggingService.StartScriptLogging(scriptName);
        _psLoggingService.LogParameters(sessionId, scriptName, parameters);

        Process? process = null;

        try
            {
            // Build parameter string with proper escaping
            var paramString = BuildParameterString(parameters, logPath);
            var safeParamString = BuildSafeParameterString(parameters, logPath);

            _logger.LogDebug("Parameters: {Parameters}", safeParamString);
            _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Script parameters: {safeParamString}", "DEBUG");

            // Try PowerShell 7+ executables ONLY - never use PowerShell 5.x
            var powershellPaths = new[]
            {
            "pwsh.exe",
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Microsoft\WindowsApps\pwsh.exe",
            @"C:\Program Files\PowerShell\7-preview\pwsh.exe"
        };

            Exception? lastException = null;

            foreach (var psPath in powershellPaths)
                {
                try
                    {
                    _logger.LogInformation("Trying PowerShell executable: {PowerShell}", psPath);
                    _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Trying PowerShell: {psPath}", "INFO");

                    if (psPath.Contains("\\") && !File.Exists(psPath))
                        {
                        _logger.LogDebug("PowerShell executable not found at: {PowerShell}", psPath);
                        continue;
                        }

                    // Create PowerShell process with logging script integration
                    var scriptsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
                    var loggingScriptPath = Path.Combine(scriptsDirectory, "Write-ScriptLog.ps1");

                    var psi = new ProcessStartInfo
                        {
                        FileName = psPath,
                        Arguments = File.Exists(loggingScriptPath)
                            ? $"-NoProfile -ExecutionPolicy Unrestricted -Command \"" +
                              $". '{loggingScriptPath}'; " +
                              $"& '{fullScriptPath}'{paramString}\""
                            : $"-NoProfile -ExecutionPolicy Unrestricted -File \"{fullScriptPath}\"{paramString}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                        };

                    _logger.LogInformation("Creating PowerShell process with command: {FileName}", psPath);
                    _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Starting PowerShell process: {psPath}", "INFO");

                    process = new Process { StartInfo = psi };

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            outputBuilder.AppendLine(args.Data);
                            _logger.LogDebug("PS Output: {Output}", args.Data);
                            _psLoggingService.LogScriptOutput(sessionId, scriptName, args.Data, "OUTPUT");
                            }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            errorBuilder.AppendLine(args.Data);
                            _logger.LogWarning("PS Error: {Error}", args.Data);
                            _psLoggingService.LogScriptError(sessionId, scriptName, args.Data);
                            }
                    };

                    // Start the process
                    process.Start();

                    int processId = process.Id;
                    _activeProcesses.TryAdd(processId, process);

                    _logger.LogInformation("Successfully started PowerShell process: {PowerShell} (PID: {ProcessId})", psPath, processId);
                    _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Process started with PID: {processId}", "INFO");

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait with timeout and cancellation support
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                    try
                        {
                        await process.WaitForExitAsync(cts.Token);
                        _logger.LogInformation("PowerShell process completed with exit code: {ExitCode}", process.ExitCode);
                        _psLoggingService.LogScriptOutput(sessionId, scriptName, $"Process completed with exit code: {process.ExitCode}", "INFO");
                        }
                    catch (OperationCanceledException)
                        {
                        _logger.LogError("PowerShell process timed out after 10 minutes");
                        _psLoggingService.LogScriptError(sessionId, scriptName, "Script execution timed out after 10 minutes");
                        KillProcessSafely(process);
                        throw new TimeoutException("PowerShell script execution timed out after 10 minutes");
                        }

                    var output = outputBuilder.ToString();
                    var errors = errorBuilder.ToString();

                    _logger.LogDebug("External PowerShell ({PowerShell}) completed with exit code: {ExitCode}",
                        psPath, process.ExitCode);

                    // Include errors in output but don't treat them as fatal
                    if (!string.IsNullOrEmpty(errors))
                        {
                        output += "\nSTDERR:\n" + errors;
                        }

                    // End PowerShell logging session
                    var success = process.ExitCode == 0 && !output.Contains("ERROR:");
                    var summary = $"Exit code: {process.ExitCode}, Output length: {output.Length} chars";
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, success, summary);

                    // Clean up this specific process
                    CleanupProcess(process, processId);

                    return output;
                    }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
                    {
                    _logger.LogDebug("PowerShell executable not found: {PowerShell}", psPath);
                    _psLoggingService.LogScriptError(sessionId, scriptName, $"PowerShell executable not found: {psPath}");
                    lastException = ex;

                    if (process != null)
                        {
                        SafeCleanupProcess(process);
                        process = null;
                        }
                    continue;
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Failed to execute with {PowerShell}, trying next option", psPath);
                    _psLoggingService.LogScriptError(sessionId, scriptName, $"Failed with {psPath}: {ex.Message}");
                    lastException = ex;

                    if (process != null)
                        {
                        SafeCleanupProcess(process);
                        process = null;
                        }
                    continue;
                    }
                }

            // End logging session with failure
            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, "No PowerShell 7+ executable found");

            throw new InvalidOperationException(
                "No PowerShell 7+ executable found. This application requires PowerShell 7 or later. " +
                "Please install PowerShell 7+ from: https://github.com/PowerShell/PowerShell/releases",
                lastException);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing external PowerShell script: {Script}", scriptPath);
            _psLoggingService.LogScriptError(sessionId, scriptName, $"Script execution failed: {ex.Message}");
            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, ex.Message);

            if (process != null)
                {
                SafeCleanupProcess(process);
                }

            return $"ERROR: {ex.Message}";
            }
        }

    // Updated RunCommandAsync to also use proper logging
    public async Task<string> RunCommandAsync (string command, Dictionary<string, object>? parameters = null)
        {
        _logger.LogDebug("Executing PowerShell command via external process: {Command}", command);

        // Extract a meaningful name for the command
        var commandName = ExtractCommandName(command);
        var sessionId = _psLoggingService.StartScriptLogging(commandName);

        if (parameters?.Count > 0)
            {
            _psLoggingService.LogParameters(sessionId, commandName, parameters);
            }

        try
            {
            // Create a temporary script file for the command
            var tempScriptPath = Path.GetTempFileName() + ".ps1";

            try
                {
                // Build the script content
                var scriptContent = new StringBuilder();

                // Add parameters if provided
                if (parameters?.Count > 0)
                    {
                    foreach (var param in parameters)
                        {
                        var value = param.Value?.ToString() ?? "";
                        var escapedValue = value.Replace("'", "''");
                        scriptContent.AppendLine($"${param.Key} = '{escapedValue}'");
                        }

                    scriptContent.AppendLine();
                    }

                // Add the command
                scriptContent.AppendLine(command);

                // Write to temp file
                await File.WriteAllTextAsync(tempScriptPath, scriptContent.ToString());

                // Execute the temp script
                var result = await RunScriptExternalAsync(tempScriptPath, new Dictionary<string, object>());

                _psLoggingService.EndScriptLogging(sessionId, commandName, !result.StartsWith("ERROR:"),
                    $"Command execution completed");

                return result;
                }
            finally
                {
                // Clean up temp file
                try
                    {
                    if (File.Exists(tempScriptPath))
                        {
                        File.Delete(tempScriptPath);
                        }
                    }
                catch
                    {
                    // Ignore cleanup errors
                    }
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing PowerShell command: {Command}", command);
            _psLoggingService.LogScriptError(sessionId, commandName, $"Command execution failed: {ex.Message}");
            _psLoggingService.EndScriptLogging(sessionId, commandName, false, ex.Message);
            return $"COMMAND ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Run script and deserialize JSON output to object
    /// </summary>
    public async Task<T?> RunScriptAndGetObjectAsync<T> (string scriptPath, Dictionary<string, object> parameters,
        string? logPath = null)
        {
        string scriptOutput = await RunScriptAsync(scriptPath, parameters, logPath);

        if (string.IsNullOrWhiteSpace(scriptOutput))
            {
            return default;
            }

        // Extract JSON from mixed output
        var jsonResult = ExtractJsonFromOutput(scriptOutput);

        if (string.IsNullOrWhiteSpace(jsonResult))
            {
            _logger.LogWarning("No valid JSON found in script output for {Script}", scriptPath);
            return default;
            }

        try
            {
            return JsonSerializer.Deserialize<T>(jsonResult,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        catch (JsonException ex)
            {
            _logger.LogError(ex, "JSON deserialization error for script {Script}. JSON: {Json}", scriptPath,
                jsonResult);
            return default;
            }
        }

    /// <summary>
    /// Run script and deserialize JSON output to collection
    /// </summary>
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T> (string scriptPath,
        Dictionary<string, object> parameters, string? logPath = null)
        {
        string scriptOutput = await RunScriptAsync(scriptPath, parameters, logPath);

        if (string.IsNullOrWhiteSpace(scriptOutput))
            {
            return new ObservableCollection<T>();
            }

        // Extract JSON from mixed output
        var jsonResult = ExtractJsonFromOutput(scriptOutput);

        if (string.IsNullOrWhiteSpace(jsonResult))
            {
            _logger.LogWarning("No valid JSON found in script output for {Script}", scriptPath);
            return new ObservableCollection<T>();
            }

        try
            {
            var items = JsonSerializer.Deserialize<ObservableCollection<T>>(jsonResult,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items ?? new ObservableCollection<T>();
            }
        catch (JsonException ex)
            {
            _logger.LogError(ex, "JSON deserialization error for collection in script {Script}. JSON: {Json}",
                scriptPath, jsonResult);
            return new ObservableCollection<T>();
            }
        }

    public async Task<string> CheckPrerequisitesAsync (string? logPath = null)
    {
        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Get-Prerequisites.ps1");
        var parameters = new Dictionary<string, object>();

        // Use the configured log path if not provided
        if (string.IsNullOrEmpty(logPath))
        {
            var configuredLogPath = _configurationService.GetConfiguration().LogPath;
            logPath = configuredLogPath;
        }

        if (!string.IsNullOrEmpty(logPath))
        {
            parameters["LogPath"] = logPath;
        }

        // Execute with proper script name for logging
        return await RunScriptExternalAsync(scriptPath, parameters, logPath);
    }
    // Add this helper method to your HybridPowerShellService class
    private Dictionary<string, object> EnsureLogPathInParameters (Dictionary<string, object> parameters)
    {
        var updatedParameters = new Dictionary<string, object>(parameters);

        // If LogPath is not already specified, add the configured one
        if (!updatedParameters.ContainsKey("LogPath"))
        {
            var configuredLogPath = _configurationService.GetConfiguration().LogPath;
            if (!string.IsNullOrEmpty(configuredLogPath))
            {
                updatedParameters["LogPath"] = configuredLogPath;
                _logger.LogDebug("Auto-added configured LogPath: {LogPath}", configuredLogPath);
            }
        }

        return updatedParameters;
    }
    /// <summary>
    /// Extract JSON from mixed script output - IMPROVED VERSION
    /// </summary>
    private string ExtractJsonFromOutput (string output)
        {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var lines = output.Split('\n', '\r', StringSplitOptions.RemoveEmptyEntries);

        // Look for lines that are complete JSON objects or arrays
        var jsonCandidates = new List<string>();

        foreach (var line in lines)
            {
            var trimmedLine = line.Trim();
            if ((trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}")) ||
                (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]")))
                {
                // Quick validation - should contain expected JSON structure
                if (trimmedLine.Contains("\"") && trimmedLine.Length > 10)
                    {
                    jsonCandidates.Add(trimmedLine);
                    }
                }
            }

        // Return the FIRST valid JSON found (ignore duplicates)
        if (jsonCandidates.Count > 0)
            {
            var firstJson = jsonCandidates[0];

            // Additional validation - try to parse it
            try
                {
                using var doc = JsonDocument.Parse(firstJson);
                return firstJson; // Valid JSON
                }
            catch
                {
                // Not valid JSON, continue to multi-line search
                }
            }

        // Look for multi-line JSON objects or arrays (fallback)
        var jsonStart = -1;
        var jsonEnd = -1;
        var startChar = ' ';
        var endChar = ' ';

        // Try to find JSON object first
        var objStart = output.IndexOf('{');
        var objEnd = output.LastIndexOf('}');
        
        // Try to find JSON array
        var arrStart = output.IndexOf('[');
        var arrEnd = output.LastIndexOf(']');

        // Use whichever appears first in the output
        if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
        {
            jsonStart = objStart;
            jsonEnd = objEnd;
            startChar = '{';
            endChar = '}';
        }
        else if (arrStart >= 0)
        {
            jsonStart = arrStart;
            jsonEnd = arrEnd;
            startChar = '[';
            endChar = ']';
        }

        if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
            var candidate = output.Substring(jsonStart, jsonEnd - jsonStart + 1);

            // Validate this candidate
            try
                {
                using var doc = JsonDocument.Parse(candidate);
                return candidate;
                }
            catch
                {
                // Not valid JSON
                }
            }

        return string.Empty;
        }

    /// <summary>
    /// Debug method to check the current PowerCLI bypass status
    /// </summary>
    public string GetPowerCliBypassStatus ()
        {
        return $"PowerCliConfirmedInstalled: {PowerCliConfirmedInstalled}";
        }

    /// <summary>
    /// Debug method to check if a specific script would get the bypass flag
    /// </summary>
    public bool WouldScriptGetBypass (string scriptPath)
        {
        return PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath);
        }

    /// <summary>
    /// Creates a PSCredential object and passes it to PowerShell scripts
    /// This is the preferred method for script authentication
    /// </summary>
    public async Task<string> RunScriptWithCredentialObjectAsync (string scriptPath, string username, string password,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        try
            {
            _logger.LogInformation("Executing script with PSCredential object using SDK: {ScriptPath}", scriptPath);

            // Create parameters dictionary with credential object
            var parameters = new Dictionary<string, object>();

            // Create PSCredential object
            var securePassword = new System.Security.SecureString();
            foreach (char c in password)
            {
                securePassword.AppendChar(c);
            }
            securePassword.MakeReadOnly();
            var credential = new PSCredential(username, securePassword);

            // Add the credential parameter (scripts expect $Credentials with capital C)
            parameters["Credentials"] = credential;

            // Add additional parameters
            if (additionalParameters != null)
                {
                foreach (var param in additionalParameters)
                    {
                    if (param.Key != "Username" && param.Key != "Password") // Skip these as they're in credential
                        {
                        parameters[param.Key] = param.Value;
                        }
                    }
                }

            // Execute using SDK method with proper parameter passing
            return await RunScriptWithSDKAsync(scriptPath, parameters, logPath);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing script with PSCredential: {ScriptPath}", scriptPath);
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Enhanced method specifically for vCenter connections using PSCredential
    /// </summary>
    public async Task<string> RunVCenterScriptAsync (string scriptPath, VCenterConnection connection, string password,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        var parameters = new Dictionary<string, object>
            {
            ["VCenterServer"] = connection.ServerAddress
            };

        // Add any additional parameters
        if (additionalParameters != null)
            {
            foreach (var param in additionalParameters)
                {
                parameters[param.Key] = param.Value;
                }
            }

        // Add BypassModuleCheck if PowerCLI is confirmed
        if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
            {
            parameters["BypassModuleCheck"] = true;
            _logger.LogInformation("Added BypassModuleCheck for vCenter script: {ScriptPath}", scriptPath);
            }

        return await RunScriptWithCredentialObjectAsync(scriptPath, connection.Username, password, parameters, logPath);
        }

    /// <summary>
    /// Method for dual vCenter operations (source and target)
    /// </summary>
    public async Task<string> RunDualVCenterScriptAsync (string scriptPath,
        VCenterConnection sourceConnection, string sourcePassword,
        VCenterConnection targetConnection, string targetPassword,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        try
            {
            _logger.LogInformation("Executing dual vCenter script: {ScriptPath}", scriptPath);

            // Prepare the script content that creates both credential objects
            var scriptContent = new StringBuilder();

            // Create source PSCredential object
            scriptContent.AppendLine("# Create Source PSCredential object");
            scriptContent.AppendLine(
                $"$sourceSecurePassword = ConvertTo-SecureString '{sourcePassword.Replace("'", "''")}' -AsPlainText -Force");
            scriptContent.AppendLine(
                $"$sourceCredential = New-Object System.Management.Automation.PSCredential('{sourceConnection.Username.Replace("'", "''")}', $sourceSecurePassword)");
            scriptContent.AppendLine();

            // Create target PSCredential object
            scriptContent.AppendLine("# Create Target PSCredential object");
            scriptContent.AppendLine(
                $"$targetSecurePassword = ConvertTo-SecureString '{targetPassword.Replace("'", "''")}' -AsPlainText -Force");
            scriptContent.AppendLine(
                $"$targetCredential = New-Object System.Management.Automation.PSCredential('{targetConnection.Username.Replace("'", "''")}', $targetSecurePassword)");
            scriptContent.AppendLine();

            // Add server parameters
            scriptContent.AppendLine($"$SourceVCenter = '{sourceConnection.ServerAddress.Replace("'", "''")}'");
            scriptContent.AppendLine($"$TargetVCenter = '{targetConnection.ServerAddress.Replace("'", "''")}'");
            scriptContent.AppendLine();

            // Add any additional parameters
            if (additionalParameters != null)
                {
                foreach (var param in additionalParameters)
                    {
                    if (param.Value is bool boolValue)
                        {
                        scriptContent.AppendLine($"${param.Key} = ${boolValue.ToString().ToLower()}");
                        }
                    else if (param.Value is string stringValue)
                        {
                        scriptContent.AppendLine($"${param.Key} = '{stringValue.Replace("'", "''")}'");
                        }
                    else if (param.Value is Array arrayValue)
                        {
                        var arrayString = string.Join("','",
                            arrayValue.Cast<object>().Select(o => o.ToString()?.Replace("'", "''")));
                        scriptContent.AppendLine($"${param.Key} = @('{arrayString}')");
                        }
                    else
                        {
                        scriptContent.AppendLine($"${param.Key} = '{param.Value?.ToString()?.Replace("'", "''")}'");
                        }
                    }

                scriptContent.AppendLine();
                }

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
                {
                scriptContent.AppendLine("$BypassModuleCheck = $true");
                scriptContent.AppendLine();
                }

            // Add the script execution
            scriptContent.AppendLine($"# Execute the target script");
            scriptContent.AppendLine($". '{Path.GetFullPath(scriptPath)}'");

            // Create temporary script file
            var tempScriptPath = Path.GetTempFileName() + ".ps1";

            try
                {
                await File.WriteAllTextAsync(tempScriptPath, scriptContent.ToString());

                // Execute using existing external PowerShell method
                var result = await RunScriptExternalAsync(tempScriptPath, new Dictionary<string, object>(), logPath);

                return result;
                }
            finally
                {
                // Clean up temp file
                try
                    {
                    if (File.Exists(tempScriptPath))
                        {
                        File.Delete(tempScriptPath);
                        }
                    }
                catch
                    {
                    // Ignore cleanup errors
                    }
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing dual vCenter script: {ScriptPath}", scriptPath);
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Optimized vCenter script execution that passes credentials as direct parameters
    /// Avoids creating temporary script files
    /// </summary>
    public async Task<string> RunVCenterScriptDirectAsync (string scriptPath, VCenterConnection connection, string password,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        try
            {
            _logger.LogInformation("Executing vCenter script with direct parameter passing: {ScriptPath}", scriptPath);
            _logger.LogInformation("This method avoids temporary file creation for better performance");

            // Build parameters for direct execution
            var parameters = new Dictionary<string, object>
                {
                ["VCenterServer"] = connection.ServerAddress,
                ["Username"] = connection.Username,
                ["Password"] = password  // Pass as plain text - script will convert to SecureString
                };

            // Add any additional parameters
            if (additionalParameters != null)
                {
                foreach (var param in additionalParameters)
                    {
                    parameters[param.Key] = param.Value;
                    }
                }

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
                {
                parameters["BypassModuleCheck"] = true;
                _logger.LogInformation("Added BypassModuleCheck=true for script: {ScriptPath}", scriptPath);
                }

            // Add log path if provided
            if (!string.IsNullOrEmpty(logPath))
                {
                parameters["LogPath"] = logPath;
                }

            // Use the optimized RunScriptAsync that passes parameters directly
            // This avoids the temp file creation
            return await RunScriptOptimizedAsync(scriptPath, parameters, logPath);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing vCenter script with direct parameters: {ScriptPath}", scriptPath);
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Optimized dual vCenter script execution with direct parameters
    /// </summary>
    public async Task<string> RunDualVCenterScriptDirectAsync (string scriptPath,
        VCenterConnection sourceConnection, string sourcePassword,
        VCenterConnection targetConnection, string targetPassword,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        try
            {
            _logger.LogInformation("Executing dual vCenter script with direct parameters: {ScriptPath}", scriptPath);

            // Build parameters for direct execution
            var parameters = new Dictionary<string, object>
                {
                ["SourceVCenter"] = sourceConnection.ServerAddress,
                ["SourceUsername"] = sourceConnection.Username,
                ["SourcePassword"] = sourcePassword,
                ["TargetVCenter"] = targetConnection.ServerAddress,
                ["TargetUsername"] = targetConnection.Username,
                ["TargetPassword"] = targetPassword
                };

            // Add any additional parameters
            if (additionalParameters != null)
                {
                foreach (var param in additionalParameters)
                    {
                    parameters[param.Key] = param.Value;
                    }
                }

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
                {
                parameters["BypassModuleCheck"] = true;
                _logger.LogInformation("Added BypassModuleCheck=true for dual vCenter script");
                }

            // Add log path if provided
            if (!string.IsNullOrEmpty(logPath))
                {
                parameters["LogPath"] = logPath;
                }

            // Direct execution without temp file
            return await RunScriptOptimizedAsync(scriptPath, parameters, logPath);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing dual vCenter script: {ScriptPath}", scriptPath);
            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Build parameter string with proper escaping - CORRECTED VERSION
    /// </summary>
    private string BuildParameterString (Dictionary<string, object> parameters, string? logPath)
        {
        var paramString = new StringBuilder();

        foreach (var param in parameters)
            {
            // Skip null values
            if (param.Value == null) continue;

            // Handle SecureString
            if (param.Value is System.Security.SecureString secureString)
                {
                var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secureString);
                try
                    {
                    var value = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? "";
                    var escapedValue = value.Replace("\"", "`\"");
                    paramString.Append($" -{param.Key} \"{escapedValue}\"");
                    }
                finally
                    {
                    System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                    }
                continue;
                }

            // Handle boolean parameters - FIXED
            if (param.Value is bool boolValue)
                {
                // Pass boolean as PowerShell boolean literal without quotes
                // Use $true or $false which PowerShell understands
                paramString.Append($" -{param.Key}:${boolValue.ToString().ToLower()}");
                continue;
                }

            // Handle string and other types
            var stringValue = param.Value?.ToString() ?? "";
            var escaped = stringValue.Replace("\"", "`\"");
            paramString.Append($" -{param.Key} \"{escaped}\"");
            }

        // Add LogPath if provided and not already in parameters
        if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
            {
            var escapedLogPath = logPath.Replace("\"", "`\"");
            paramString.Append($" -LogPath \"{escapedLogPath}\"");
            }

        return paramString.ToString();
        }

    /// <summary>
    /// Build safe parameter string for logging - CORRECTED VERSION
    /// </summary>
    private string BuildSafeParameterString (Dictionary<string, object> parameters, string? logPath)
        {
        var safeParamString = new StringBuilder();

        foreach (var param in parameters)
            {
            // Skip null values
            if (param.Value == null) continue;

            // Handle boolean parameters - show the actual value
            if (param.Value is bool boolValue)
                {
                // Show the boolean value in logs for clarity
                safeParamString.Append($" -{param.Key}:${boolValue.ToString().ToLower()}");
                continue;
                }

            // Handle sensitive parameters
            if (IsSensitiveParameter(param.Key))
                {
                safeParamString.Append($" -{param.Key} \"[REDACTED]\"");
                }
            else
                {
                var value = param.Value?.ToString() ?? "";
                var escapedValue = value.Replace("\"", "`\"");
                safeParamString.Append($" -{param.Key} \"{escapedValue}\"");
                }
            }

        // Add LogPath if provided
        if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
            {
            var escapedLogPath = logPath.Replace("\"", "`\"");
            safeParamString.Append($" -LogPath \"{escapedLogPath}\"");
            }

        return safeParamString.ToString();
        }

    /// <summary>
    /// Clean up a specific process
    /// </summary>
    private void CleanupProcess (Process process, int processId)
        {
        try
            {
            if (process == null) return;

            // Remove from tracking
            _activeProcesses.TryRemove(processId, out _);

            // Ensure process is terminated
            try
                {
                if (!process.HasExited)
                    {
                    KillProcessSafely(process);
                    }
                }
            catch (InvalidOperationException)
                {
                // Process was never started or already exited
                _logger.LogDebug("Process {ProcessId} was already exited during cleanup", processId);
                }

            // Dispose the process object
            try
                {
                process.Dispose();
                }
            catch (Exception ex)
                {
                _logger.LogDebug(ex, "Error disposing process {ProcessId}", processId);
                }

            _logger.LogDebug("Cleaned up PowerShell process {ProcessId}", processId);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during process cleanup for {ProcessId}", processId);
            }
        }

    /// <summary>
    /// Safe cleanup method that handles all edge cases
    /// </summary>
    private void SafeCleanupProcess (Process? process)
        {
        if (process == null) return;

        try
            {
            // Try to get the process ID if possible
            int? processId = null;
            try
                {
                processId = process.Id;
                }
            catch (InvalidOperationException)
                {
                // Process was never started
                }

            // Remove from tracking if we have an ID
            if (processId.HasValue)
                {
                _activeProcesses.TryRemove(processId.Value, out _);
                }

            // Try to kill if not exited
            try
                {
                if (!process.HasExited)
                    {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000); // Wait up to 5 seconds
                    }
                }
            catch (InvalidOperationException)
                {
                // Process was never started or already exited
                }
            catch (Exception ex)
                {
                _logger.LogDebug(ex, "Error killing process during safe cleanup");
                }

            // Dispose the process object
            try
                {
                process.Dispose();
                }
            catch (Exception ex)
                {
                _logger.LogDebug(ex, "Error disposing process during safe cleanup");
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Unexpected error during safe process cleanup");
            }
        }

    /// <summary>
    /// Safely kill a PowerShell process
    /// </summary>
    private void KillProcessSafely (Process process)
        {
        try
            {
            if (process == null) return;

            // Check if process has a valid handle and hasn't exited
            if (!process.HasExited)
                {
                _logger.LogWarning("Forcibly terminating PowerShell process {ProcessId}", process.Id);
                process.Kill(entireProcessTree: true);
                }
            }
        catch (InvalidOperationException ex)
            {
            // Process was never started, already exited, or disposed
            _logger.LogDebug(ex, "Process was not in a state that could be killed");
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error killing PowerShell process");
            }
        }

    /// <summary>
    /// Periodic cleanup of orphaned processes - UPDATED VERSION
    /// </summary>
    private void CleanupOrphanedProcesses (object? state)
        {
        try
            {
            var orphanedProcesses = new List<KeyValuePair<int, Process>>();

            foreach (var kvp in _activeProcesses.ToArray()) // ToArray to avoid collection modification issues
                {
                try
                    {
                    var process = kvp.Value;

                    // Check if process is null or has exited
                    if (process == null)
                        {
                        orphanedProcesses.Add(kvp);
                        continue;
                        }

                    try
                        {
                        if (process.HasExited)
                            {
                            orphanedProcesses.Add(kvp);
                            }
                        }
                    catch (InvalidOperationException)
                        {
                        // Process was never started or already disposed
                        orphanedProcesses.Add(kvp);
                        }
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Error checking process status for {ProcessId}", kvp.Key);
                    orphanedProcesses.Add(kvp);
                    }
                }

            foreach (var orphan in orphanedProcesses)
                {
                SafeCleanupProcess(orphan.Value);
                }

            if (orphanedProcesses.Count > 0)
                {
                _logger.LogInformation("Cleaned up {Count} orphaned PowerShell processes", orphanedProcesses.Count);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during periodic process cleanup");
            }
        }

    /// <summary>
    /// Force cleanup of all active PowerShell processes - UPDATED VERSION
    /// </summary>
    public void CleanupAllProcesses ()
        {
        _logger.LogInformation("Starting cleanup of all active PowerShell processes");

        var processesToCleanup = _activeProcesses.ToArray(); // ToArray to avoid collection modification

        foreach (var kvp in processesToCleanup)
            {
            SafeCleanupProcess(kvp.Value);
            }

        // Clear the collection
        _activeProcesses.Clear();

        _logger.LogInformation("Completed cleanup of {Count} PowerShell processes", processesToCleanup.Length);
        }

    /// <summary>
    /// Get count of currently active PowerShell processes
    /// </summary>
    public int GetActiveProcessCount ()
        {
        return _activeProcesses.Count;
        }

    // Add this enhanced method to HybridPowerShellService
    public async Task<string> RunScriptWithErrorHandlingAsync(
        string scriptPath,
        Dictionary<string, object> parameters,
        string? logPath = null)
    {
        try
        {
            // First validate the operation
            var validationResult = await _errorHandlingService.ValidateOperationAsync(
                Path.GetFileName(scriptPath), parameters);

            if (!validationResult.IsValid)
            {
                var errorMessage = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                await _errorHandlingService.HandleScriptErrorAsync(scriptPath, errorMessage, parameters);
                return $"VALIDATION_ERROR: {errorMessage}";
            }

            // Run the script with the optimized method
            var result = await RunScriptOptimizedAsync(scriptPath, parameters, logPath);

            // Check if the result indicates an error
            if (result.StartsWith("ERROR:") || result.Contains("Exception"))
            {
                await _errorHandlingService.HandleScriptErrorAsync(scriptPath, result, parameters);
            }

            return result;
        }
        catch (Exception ex)
        {
            await _errorHandlingService.LogStructuredErrorAsync(
                Path.GetFileName(scriptPath), ex, parameters);
            await _errorHandlingService.HandleScriptErrorAsync(scriptPath, ex.Message, parameters);
            return $"ERROR: {ex.Message}";
        }
    }

    #region IDisposable Implementation

    public void Dispose ()
        {
        Dispose(true);
        GC.SuppressFinalize(this);
        }

    protected virtual void Dispose (bool disposing)
        {
        if (!_disposed)
            {
            if (disposing)
                {
                _logger.LogInformation("Disposing HybridPowerShellService and cleaning up processes");

                // Stop the cleanup timer
                _cleanupTimer?.Dispose();

                // Cleanup all active processes
                CleanupAllProcesses();

                // Dispose PowerShell logging service
                _psLoggingService?.Dispose();
                }

            _disposed = true;
            }
        }

    ~HybridPowerShellService ()
        {
        Dispose(false);
        }

    public async Task<List<ClusterInfo>> GetClustersAsync(string connectionType = "source")
    {
        try
        {
            // Use inline PowerShell approach like the ESXi Hosts page since it works reliably
            _logger.LogInformation("Getting clusters using inline PowerShell for {ConnectionType} connection", connectionType);
            
            // Get connection information for the specified connection type
            var connection = await _sharedConnectionService.GetConnectionAsync(connectionType);
            var password = await _sharedConnectionService.GetPasswordAsync(connectionType);
            
            if (connection == null || string.IsNullOrEmpty(connection.ServerAddress) || 
                string.IsNullOrEmpty(connection.Username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("No {ConnectionType} connection configured or missing credentials", connectionType);
                return new List<ClusterInfo>();
            }

            // Use inline PowerShell script similar to ESXi Hosts page approach
            var inlineScript = $@"
                # Connect to vCenter with provided credentials
                try {{
                    $securePassword = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    $credential = New-Object System.Management.Automation.PSCredential('{connection.Username.Replace("'", "''")}', $securePassword)
                    
                    # Import PowerCLI if not already loaded
                    if (-not (Get-Command 'Connect-VIServer' -ErrorAction SilentlyContinue)) {{
                        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                    }}
                    
                    # Connect to vCenter
                    $connection = Connect-VIServer -Server '{connection.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                    
                    # Get all clusters from ALL datacenters
                    $datacenters = Get-Datacenter -ErrorAction Stop
                    $result = @()
                    
                    foreach ($datacenter in $datacenters) {{
                        try {{
                            $clusters = Get-Cluster -Location $datacenter -ErrorAction SilentlyContinue
                            
                            if ($clusters) {{
                                foreach ($cluster in $clusters) {{
                                    $clusterObj = [PSCustomObject]@{{
                                        Name = $cluster.Name
                                        Id = $cluster.Id
                                        HAEnabled = $cluster.HAEnabled
                                        DrsEnabled = $cluster.DrsEnabled
                                        EVCMode = if ($cluster.EVCMode) {{ $cluster.EVCMode }} else {{ """" }}
                                        DatacenterName = $datacenter.Name
                                        FullName = ""$($datacenter.Name)/$($cluster.Name)""
                                    }}
                                    $result += $clusterObj
                                }}
                            }}
                        }} catch {{
                            # Skip this datacenter if there's an error
                            continue
                        }}
                    }}
                    
                    # Force array output even for single items and ensure proper JSON structure
                    if ($result.Count -eq 1) {{
                        # Single item - wrap in array to ensure JSON array format
                        Write-Output (@($result) | ConvertTo-Json -Compress)
                    }} elseif ($result.Count -gt 1) {{
                        # Multiple items - ensure array format
                        Write-Output ($result | ConvertTo-Json -Compress)
                    }} else {{
                        # No items - output empty array
                        Write-Output '[]'
                    }}
                    
                    # Disconnect
                    Disconnect-VIServer -Server $connection -Confirm:$false -Force -ErrorAction SilentlyContinue
                }}
                catch {{
                    Write-Error ""Failed to get clusters: $($_.Exception.Message)""
                    # Try to disconnect on error
                    try {{ Disconnect-VIServer -Server '*' -Confirm:$false -Force -ErrorAction SilentlyContinue }} catch {{}}
                    throw
                }}
            ";

            var scriptParameters = new Dictionary<string, object>
            {
                ["SuppressConsoleOutput"] = true
            };

            _logger.LogInformation("Executing inline cluster retrieval script for {Server}", connection.ServerAddress);
            var result = await RunInlineScriptAsync(inlineScript, scriptParameters);
            
            _logger.LogInformation("Inline script raw output length: {Length} characters", result?.Length ?? 0);

            if (string.IsNullOrEmpty(result))
            {
                _logger.LogWarning("No output from inline cluster script");
                return new List<ClusterInfo>();
            }

            // Extract JSON from the output
            var jsonResult = ExtractJsonFromOutput(result);
            if (string.IsNullOrEmpty(jsonResult))
            {
                _logger.LogWarning("No valid JSON found in inline script output. Raw output: {Output}", result);
                return new List<ClusterInfo>();
            }
            
            _logger.LogInformation("Extracted JSON from inline script: {Json}", jsonResult);

            try
            {
                var clusters = new List<ClusterInfo>();
                
                // Try to parse as array first, then as single object
                if (jsonResult.TrimStart().StartsWith("["))
                {
                    var clusterArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonResult, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                    if (clusterArray != null)
                    {
                        foreach (var clusterDict in clusterArray)
                        {
                            var clusterInfo = CreateClusterInfoFromDict(clusterDict);
                            clusters.Add(clusterInfo);
                            _logger.LogInformation("Parsed cluster from array: {Name}", clusterInfo.Name);
                        }
                    }
                }
                else
                {
                    // Single object
                    var clusterDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResult, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                    if (clusterDict != null)
                    {
                        var clusterInfo = CreateClusterInfoFromDict(clusterDict);
                        clusters.Add(clusterInfo);
                        _logger.LogInformation("Parsed single cluster: {Name}", clusterInfo.Name);
                    }
                }

                _logger.LogInformation("Successfully retrieved {Count} clusters using inline script", clusters.Count);
                return clusters;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed. JSON content: {Json}", jsonResult);
                return new List<ClusterInfo>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get clusters using inline script");
            return new List<ClusterInfo>();
        }
    }

    private ClusterInfo CreateClusterInfoFromDict(Dictionary<string, object> clusterDict)
    {
        return new ClusterInfo
        {
            Name = clusterDict.GetValueOrDefault("Name", "Unknown").ToString(),
            Id = clusterDict.GetValueOrDefault("Id", "").ToString(),
            HAEnabled = bool.Parse(clusterDict.GetValueOrDefault("HAEnabled", false).ToString()),
            DrsEnabled = bool.Parse(clusterDict.GetValueOrDefault("DrsEnabled", false).ToString()),
            EVCMode = clusterDict.GetValueOrDefault("EVCMode", "").ToString(),
            DatacenterName = clusterDict.GetValueOrDefault("DatacenterName", "").ToString(),
            FullName = clusterDict.GetValueOrDefault("FullName", "").ToString(),
            HostCount = 0,  // Would need additional calls to populate
            VmCount = 0,
            DatastoreCount = 0
        };
    }

    private async Task<string> RunInlineScriptAsync(string script, Dictionary<string, object> parameters)
    {
        // Reuse the existing RunScriptOptimizedAsync infrastructure but with inline script
        var tempScriptFile = Path.GetTempFileName() + ".ps1";
        try
        {
            await File.WriteAllTextAsync(tempScriptFile, script);
            return await RunScriptOptimizedAsync(tempScriptFile, parameters);
        }
        finally
        {
            try { File.Delete(tempScriptFile); } catch { }
        }
    }

    public async Task<List<ClusterItem>> GetClusterItemsAsync(Dictionary<string, object> parameters)
    {
        try
        {
            var connectionType = parameters.GetValueOrDefault("ConnectionType", "source")?.ToString() ?? "source";
            
            // Check if persistent connection exists
            var connection = await _sharedConnectionService.GetConnectionAsync(connectionType);
            if (connection == null)
            {
                _logger.LogWarning("No {ConnectionType} connection configured", connectionType);
                return new List<ClusterItem>();
            }

            // Build the PowerShell script content to run in persistent session
            var script = new StringBuilder();
            
            // Get cluster name parameter
            var clusterName = parameters.GetValueOrDefault("ClusterName")?.ToString();
            if (!string.IsNullOrEmpty(clusterName))
            {
                script.AppendLine($"$ClusterName = '{clusterName.Replace("'", "''")}'");
            }
            else
            {
                script.AppendLine("$ClusterName = $null");
            }

            // Add the Get-ClusterItems script content inline to avoid file dependencies
            script.AppendLine(@"
                $result = @()
                
                # Get Resource Pools using vSphere API
                try {
                    if ($ClusterName) {
                        $cluster = Get-View -ViewType ClusterComputeResource | Where-Object { $_.Name -eq $ClusterName } | Select-Object -First 1
                        if ($cluster) {
                            $resourcePools = Get-View -ViewType ResourcePool -SearchRoot $cluster.MoRef | Where-Object { $_.Name -ne 'Resources' }
                        } else {
                            $resourcePools = @()
                        }
                    } else {
                        $resourcePools = Get-View -ViewType ResourcePool | Where-Object { $_.Name -ne 'Resources' }
                    }
                    
                    if ($resourcePools) {
                        foreach ($rp in $resourcePools) {
                            $result += @{
                                Id = $rp.MoRef.Value
                                Name = $rp.Name
                                Type = 'ResourcePool'
                                Path = '/ResourcePools/' + $rp.Name
                                ItemCount = if ($rp.Vm) { $rp.Vm.Count } else { 0 }
                                IsSelected = $true
                                Status = 'Ready'
                            }
                        }
                    }
                } catch {
                    Write-Warning ""Error retrieving resource pools: $($_.Exception.Message)""
                }
                
                # Get VM Folders (datacenter-level, not cluster-level)
                try {
                    if ($ClusterName) {
                        $cluster = Get-Cluster -Name $ClusterName -ErrorAction SilentlyContinue
                        if ($cluster) {
                            $datacenter = Get-Datacenter -Cluster $cluster -ErrorAction SilentlyContinue
                            if ($datacenter) {
                                $vmFolders = Get-Folder -Type VM -Location $datacenter -ErrorAction SilentlyContinue | Where-Object { 
                                    $_.Name -ne 'vm' -and $_.Name -ne 'Datacenters' 
                                }
                            } else {
                                $vmFolders = Get-Folder -Type VM -ErrorAction SilentlyContinue | Where-Object { 
                                    $_.Name -ne 'vm' -and $_.Name -ne 'Datacenters' 
                                }
                            }
                        } else {
                            $vmFolders = @()
                        }
                    } else {
                        $vmFolders = Get-Folder -Type VM -ErrorAction SilentlyContinue | Where-Object { 
                            $_.Name -ne 'vm' -and $_.Name -ne 'Datacenters' 
                        }
                    }
                    
                    if ($vmFolders) {
                        foreach ($folder in $vmFolders) {
                            $result += @{
                                Id = $folder.Id
                                Name = $folder.Name
                                Type = 'Folder'
                                Path = '/vm/' + $folder.Name
                                ItemCount = ($folder | Get-ChildItem -ErrorAction SilentlyContinue).Count
                                IsSelected = $true
                                Status = 'Ready'
                            }
                        }
                    }
                } catch {
                    Write-Warning ""Error retrieving VM folders: $($_.Exception.Message)""
                }
                
                $result | ConvertTo-Json -Depth 2
            ");

            _logger.LogInformation("Calling cluster items query for {ConnectionType} connection, cluster: {ClusterName}", 
                connectionType, clusterName ?? "all");

            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script.ToString());

            // Parse JSON result from Get-ClusterItems.ps1 script
            var jsonResult = ExtractJsonFromOutput(result);
            if (string.IsNullOrEmpty(jsonResult))
            {
                _logger.LogWarning("No valid JSON found in Get-ClusterItems output");
                return new List<ClusterItem>();
            }

            try
            {
                // Get-ClusterItems.ps1 returns an array of objects directly
                var objects = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonResult,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var clusterItems = new List<ClusterItem>();

                if (objects != null)
                {
                    foreach (var obj in objects)
                    {
                        var item = new ClusterItem
                        {
                            Id = obj.GetValueOrDefault("Id", "").ToString(),
                            Name = obj.GetValueOrDefault("Name", "").ToString(),
                            Type = obj.GetValueOrDefault("Type", "").ToString(),
                            Path = obj.GetValueOrDefault("Path", "").ToString(),
                            ItemCount = int.Parse(obj.GetValueOrDefault("ItemCount", 0).ToString()),
                            IsSelected = bool.Parse(obj.GetValueOrDefault("IsSelected", true).ToString()),
                            Status = obj.GetValueOrDefault("Status", "Ready").ToString()
                        };

                        // Note: TypeIcon and TypeColor are computed properties based on Type
                        // No need to set them manually - they will be calculated automatically

                        clusterItems.Add(item);
                    }
                }

                _logger.LogInformation("Retrieved {Count} cluster items from vCenter", clusterItems.Count);
                return clusterItems;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse cluster items JSON: {Json}", jsonResult);
                return new List<ClusterItem>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get vCenter objects");
            return new List<ClusterItem>();
        }
    }

    public async Task<string> MigrateVCenterObjectAsync(Dictionary<string, object> parameters)
    {
        try
        {
            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Migrate-VCenterObject.ps1");

            // Ensure required parameters are present
            if (!parameters.ContainsKey("SourceVCenter") || !parameters.ContainsKey("TargetVCenter") ||
                !parameters.ContainsKey("ObjectType") || !parameters.ContainsKey("ObjectName"))
            {
                var missingParams = new List<string>();
                if (!parameters.ContainsKey("SourceVCenter")) missingParams.Add("SourceVCenter");
                if (!parameters.ContainsKey("TargetVCenter")) missingParams.Add("TargetVCenter");
                if (!parameters.ContainsKey("ObjectType")) missingParams.Add("ObjectType");
                if (!parameters.ContainsKey("ObjectName")) missingParams.Add("ObjectName");
                
                var errorMessage = $"Missing required parameters: {string.Join(", ", missingParams)}";
                _logger.LogError(errorMessage);
                
                return JsonSerializer.Serialize(new
                {
                    Success = false,
                    Error = errorMessage,
                    ObjectType = parameters.GetValueOrDefault("ObjectType", "Unknown"),
                    ObjectName = parameters.GetValueOrDefault("ObjectName", "Unknown")
                });
            }

            _logger.LogInformation("Starting migration of {ObjectType} '{ObjectName}' from {Source} to {Target}",
                parameters["ObjectType"], parameters["ObjectName"], 
                parameters["SourceVCenter"], parameters["TargetVCenter"]);

            // Add SuppressConsoleOutput to avoid cluttered output
            var migrationParams = new Dictionary<string, object>(parameters)
            {
                ["SuppressConsoleOutput"] = true
            };

            var result = await RunScriptOptimizedAsync(scriptPath, migrationParams);
            
            // Parse the result to check for success/failure
            var jsonResult = ExtractJsonFromOutput(result);
            if (!string.IsNullOrEmpty(jsonResult))
            {
                try
                {
                    var migrationResult = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResult);
                    var success = bool.Parse(migrationResult.GetValueOrDefault("Success", false).ToString());
                    
                    if (success)
                    {
                        _logger.LogInformation("Successfully migrated {ObjectType} '{ObjectName}'", 
                            parameters["ObjectType"], parameters["ObjectName"]);
                    }
                    else
                    {
                        _logger.LogWarning("Migration failed for {ObjectType} '{ObjectName}': {Error}",
                            parameters["ObjectType"], parameters["ObjectName"],
                            migrationResult.GetValueOrDefault("Error", "Unknown error"));
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Could not parse migration result JSON");
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate vCenter object {ObjectType} '{ObjectName}'", 
                parameters.GetValueOrDefault("ObjectType"), parameters.GetValueOrDefault("ObjectName"));
            
            return JsonSerializer.Serialize(new
            {
                Success = false,
                Error = ex.Message,
                ObjectType = parameters.GetValueOrDefault("ObjectType", "Unknown"),
                ObjectName = parameters.GetValueOrDefault("ObjectName", "Unknown")
            });
        }
    }

    #endregion
    }