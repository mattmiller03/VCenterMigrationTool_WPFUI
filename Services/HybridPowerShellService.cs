using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed = false;
    private readonly PowerShellLoggingService _psLoggingService;

    /// <summary>
    /// Static flag to track PowerCLI availability (set by settings page)
    /// </summary>
    public static bool PowerCliConfirmedInstalled { get; set; } = false;

    public HybridPowerShellService (
        ILogger<HybridPowerShellService> logger, 
        ConfigurationService configurationService,
        PowerShellLoggingService psLoggingService)
        {
        _logger = logger;
        _configurationService = configurationService;
        _psLoggingService = psLoggingService;

        // FIXED: Load PowerCLI status from persistent storage on startup
        LoadPowerCliStatus();
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

            // Quick check: if PowerCLI module is available, set the flag
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

    public async Task<string> RunScriptAsync (string scriptPath, Dictionary<string, object> parameters,
        string? logPath = null)
        {
        // Check if this is a command rather than a script file
        if (!scriptPath.Contains("Scripts\\") && !scriptPath.EndsWith(".ps1"))
            {
            return await RunCommandAsync(scriptPath, parameters);
            }

        // Always use external PowerShell due to SDK issues
        _logger.LogDebug("Using external PowerShell for script: {ScriptPath}", scriptPath);
        return await RunScriptExternalAsync(scriptPath, parameters, logPath);
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

    // Enhanced RunScriptExternalAsync with logging
    private async Task<string> RunScriptExternalAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        string fullScriptPath = Path.GetFullPath(scriptPath);
        string scriptName = Path.GetFileName(scriptPath);

        // Start PowerShell logging session
        var sessionId = _psLoggingService.StartScriptLogging(scriptName);

        _logger.LogDebug("Starting external PowerShell script execution: {ScriptPath}", fullScriptPath);
        _psLoggingService.WriteLog(new PowerShellLoggingService.LogEntry
            {
            Timestamp = DateTime.Now,
            Level = "INFO",
            Source = "SYSTEM",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = $"Script path: {fullScriptPath}"
            });

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, "Script file not found");
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        Process? process = null;

        try
            {
            // Build parameter string with proper escaping
            var paramString = BuildParameterString(parameters, logPath);
            var safeParamString = BuildSafeParameterString(parameters, logPath);

            _psLoggingService.WriteLog(new PowerShellLoggingService.LogEntry
                {
                Timestamp = DateTime.Now,
                Level = "DEBUG",
                Source = "SYSTEM",
                ScriptName = scriptName,
                SessionId = sessionId,
                Message = $"Parameters: {safeParamString}"
                });

            // Try different PowerShell executables
            var powershellPaths = new[]
            {
            "pwsh.exe",
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",
            @"C:\Users\" + Environment.UserName + @"\AppData\Local\Microsoft\WindowsApps\pwsh.exe",
            "powershell.exe"
        };

            Exception? lastException = null;

            foreach (var psPath in powershellPaths)
                {
                try
                    {
                    _logger.LogInformation("Trying PowerShell executable: {PowerShell}", psPath);

                    if (psPath.Contains("\\") && !File.Exists(psPath))
                        {
                        _logger.LogDebug("PowerShell executable not found at: {PowerShell}", psPath);
                        continue;
                        }

                    // Add logging module import to the command
                    var loggingPrefix = @"-Command ""& { . '" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Write-ScriptLog.ps1") + "'; ";
                    var loggingSuffix = @" }""";

                    var psi = new ProcessStartInfo
                        {
                        FileName = psPath,
                        Arguments = $"-NoProfile -ExecutionPolicy Unrestricted -File \"{fullScriptPath}\"{paramString}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                        };

                    _logger.LogInformation("Creating PowerShell process with command: {FileName}", psPath);

                    process = new Process { StartInfo = psi };

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            outputBuilder.AppendLine(args.Data);
                            _logger.LogDebug("PS Output: {Output}", args.Data);

                            // Log to PowerShell logging service
                            _psLoggingService.WriteScriptOutput(sessionId, scriptName, args.Data, "OUTPUT");
                            }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            errorBuilder.AppendLine(args.Data);
                            _logger.LogWarning("PS Error: {Error}", args.Data);

                            // Log errors to PowerShell logging service
                            _psLoggingService.WriteScriptError(sessionId, scriptName, args.Data);
                            }
                    };

                    // Start the process
                    process.Start();

                    int processId = process.Id;
                    _activeProcesses.TryAdd(processId, process);

                    _logger.LogInformation("Successfully started PowerShell process: {PowerShell} (PID: {ProcessId})", psPath, processId);
                    _psLoggingService.WriteLog(new PowerShellLoggingService.LogEntry
                        {
                        Timestamp = DateTime.Now,
                        Level = "INFO",
                        Source = "SYSTEM",
                        ScriptName = scriptName,
                        SessionId = sessionId,
                        Message = $"Process started: {psPath} (PID: {processId})"
                        });

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait with timeout and cancellation support
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                    try
                        {
                        await process.WaitForExitAsync(cts.Token);
                        _logger.LogInformation("PowerShell process completed with exit code: {ExitCode}", process.ExitCode);
                        }
                    catch (OperationCanceledException)
                        {
                        _logger.LogError("PowerShell process timed out after 10 minutes");
                        _psLoggingService.WriteLog(new PowerShellLoggingService.LogEntry
                            {
                            Timestamp = DateTime.Now,
                            Level = "ERROR",
                            Source = "SYSTEM",
                            ScriptName = scriptName,
                            SessionId = sessionId,
                            Message = "Script execution timed out after 10 minutes"
                            });
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

                    // Clean up this specific process
                    CleanupProcess(process, processId);

                    // End logging session
                    _psLoggingService.EndScriptLogging(sessionId, scriptName, process.ExitCode == 0,
                        $"Exit code: {process.ExitCode}");

                    return output;
                    }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
                    {
                    _logger.LogDebug("PowerShell executable not found: {PowerShell}", psPath);
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
                    lastException = ex;

                    if (process != null)
                        {
                        SafeCleanupProcess(process);
                        process = null;
                        }
                    continue;
                    }
                }

            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, "No suitable PowerShell executable found");
            throw new InvalidOperationException("No suitable PowerShell executable found", lastException);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing external PowerShell script: {Script}", scriptPath);
            _psLoggingService.EndScriptLogging(sessionId, scriptName, false, ex.Message);

            if (process != null)
                {
                SafeCleanupProcess(process);
                }

            return $"ERROR: {ex.Message}";
            }
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
    /// Execute simple PowerShell commands using external PowerShell (due to SDK issues)
    /// </summary>
    public async Task<string> RunCommandAsync (string command, Dictionary<string, object>? parameters = null)
        {
        _logger.LogDebug("Executing PowerShell command via external process: {Command}", command);

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

    /// <summary>
    /// Extract JSON from mixed script output - IMPROVED VERSION
    /// </summary>
    private string ExtractJsonFromOutput (string output)
        {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var lines = output.Split('\n', '\r', StringSplitOptions.RemoveEmptyEntries);

        // Look for lines that are complete JSON objects
        var jsonCandidates = new List<string>();

        foreach (var line in lines)
            {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}"))
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

        // Look for multi-line JSON (fallback)
        int jsonStart = output.IndexOf('{');
        int jsonEnd = output.IndexOf('}');

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
            _logger.LogInformation("Executing script with PSCredential object: {ScriptPath}", scriptPath);

            // Prepare the script content that creates the credential object
            var scriptContent = new StringBuilder();

            // Create the PSCredential object in PowerShell
            scriptContent.AppendLine("# Create PSCredential object");
            scriptContent.AppendLine(
                $"$securePassword = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force");
            scriptContent.AppendLine(
                $"$credential = New-Object System.Management.Automation.PSCredential('{username.Replace("'", "''")}', $securePassword)");
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
                    else
                        {
                        scriptContent.AppendLine($"${param.Key} = '{param.Value?.ToString()?.Replace("'", "''")}'");
                        }
                    }

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
    // <summary>
    /// Enhanced method specifically for vCenter connections using direct parameter passing
    /// This avoids creating temporary script files
    /// </summary>
    public async Task<string> RunVCenterScriptOptimizedAsync (string scriptPath, VCenterConnection connection, string password,
        Dictionary<string, object>? additionalParameters = null, string? logPath = null)
        {
        try
            {
            _logger.LogInformation("Executing vCenter script with optimized credential handling: {ScriptPath}", scriptPath);

            // Build parameters for direct execution
            var parameters = new Dictionary<string, object>();

            // Add vCenter server
            parameters["VCenterServer"] = connection.ServerAddress;

            // Instead of creating a PSCredential in a temp script, we'll pass username and password
            // as secure strings that the script can use to create its own PSCredential
            parameters["Username"] = connection.Username;

            // Convert password to SecureString for secure parameter passing
            var securePassword = new System.Security.SecureString();
            foreach (char c in password)
                {
                securePassword.AppendChar(c);
                }
            securePassword.MakeReadOnly();
            parameters["SecurePassword"] = securePassword;

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

            // Use the standard RunScriptAsync which doesn't create temp files
            return await RunScriptAsync(scriptPath, parameters, logPath);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing optimized vCenter script: {ScriptPath}", scriptPath);
            return $"ERROR: {ex.Message}";
            }
        }

        /// <summary>
        /// Build parameter string with proper escaping - CORRECTED VERSION
        /// </summary>
        private StringBuilder BuildParameterString (Dictionary<string, object> parameters, string? logPath)
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

            return paramString;
        }

        /// <summary>
    /// Build safe parameter string for logging - CORRECTED VERSION
    /// </summary>
    private StringBuilder BuildSafeParameterString (Dictionary<string, object> parameters, string? logPath)
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

        return safeParamString;
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
                }

            _disposed = true;
            }
        }

    ~HybridPowerShellService ()
        {
        Dispose(false);
        }
    // Add these methods to your existing HybridPowerShellService.cs

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
    #endregion
    }