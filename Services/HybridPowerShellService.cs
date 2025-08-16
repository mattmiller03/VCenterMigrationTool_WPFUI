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
    /// <summary>
    /// Static flag to track PowerCLI availability (set by settings page)
    /// </summary>
    public static bool PowerCliConfirmedInstalled { get; set; } = false;

    public HybridPowerShellService(ILogger<HybridPowerShellService> logger, ConfigurationService configurationService)
    {
        _logger = logger;
        _configurationService = configurationService;

        // FIXED: Load PowerCLI status from persistent storage on startup
        LoadPowerCliStatus();
        _cleanupTimer = new Timer(CleanupOrphanedProcesses, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

    /// <summary>
    /// Load PowerCLI installation status from persistent storage
    /// </summary>
    private void LoadPowerCliStatus()
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
    public void SavePowerCliStatus(bool isInstalled)
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
    private bool ShouldUseExternalPowerShell(string scriptPath, Dictionary<string, object> parameters)
    {
        // ALWAYS use external PowerShell due to SDK compatibility issues
        // The internal PowerShell SDK has dependency conflicts in this application
        return true;
    }

    public async Task<string> RunScriptAsync(string scriptPath, Dictionary<string, object> parameters,
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
    public async Task<string> RunScriptOptimizedAsync(string scriptPath, Dictionary<string, object> parameters,
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
    public async Task<T?> RunScriptAndGetObjectOptimizedAsync<T>(string scriptPath,
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
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsOptimizedAsync<T>(string scriptPath,
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

        return await RunScriptAndGetObjectsAsync<T>(scriptPath, optimizedParameters, logPath);
    }

    /// <summary>
    /// Determines if a script requires PowerCLI
    /// </summary>
    private bool IsPowerCliScript(string scriptPath)
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
            "ValidateVMBackups.ps1"
        };

        var scriptName = Path.GetFileName(scriptPath);
        return powerCliScripts.Any(s => s.Equals(scriptName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines if a parameter contains sensitive data that should not be logged
    /// </summary>
    private bool IsSensitiveParameter(string parameterName)
    {
        var sensitiveParams = new[] { "Password", "password", "pwd", "secret", "token", "key" };
        return sensitiveParams.Any(s => parameterName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// Enhanced external PowerShell execution with proper cleanup
    /// </summary>
    private async Task<string> RunScriptExternalAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        _logger.LogInformation("DEBUG: Starting external PowerShell execution for script: {ScriptPath}", scriptPath);
        string fullScriptPath = Path.GetFullPath(scriptPath);

        _logger.LogDebug("Starting external PowerShell script execution: {ScriptPath}", fullScriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        Process? process = null;
        var processId = 0;

        try
            {
            // Build parameter string with proper escaping
            var paramString = BuildParameterString(parameters, logPath);

            // Create safe parameter string for logging
            var safeParamString = BuildSafeParameterString(parameters, logPath);
            _logger.LogInformation("DEBUG: Safe parameter string: {SafeParamString}", safeParamString);

            // Try different PowerShell executables
            var powershellPaths = new[]
            {
                "pwsh.exe",  // PowerShell 7 in PATH (most common)
                @"C:\Program Files\PowerShell\7\pwsh.exe",  // Standard PowerShell 7 install
                @"C:\Program Files (x86)\PowerShell\7\pwsh.exe",  // 32-bit PowerShell 7
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Microsoft\WindowsApps\pwsh.exe",  // Store install
                "powershell.exe"  // Windows PowerShell (last resort)
            };

            Exception? lastException = null;

            foreach (var psPath in powershellPaths)
                {
                try
                    {
                    _logger.LogInformation("Trying PowerShell executable: {PowerShell}", psPath);

                    // For full paths, verify the executable exists
                    if (psPath.Contains("\\") && !File.Exists(psPath))
                        {
                        _logger.LogDebug("PowerShell executable not found at: {PowerShell}", psPath);
                        continue;
                        }

                    var psi = new ProcessStartInfo
                        {
                        FileName = psPath,
                        Arguments = $"-NoProfile -ExecutionPolicy Unrestricted -File \"{fullScriptPath}\"{paramString}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                        };

                    _logger.LogInformation("DEBUG: Creating PowerShell process with command: {FileName} {SafeArguments}", psPath, safeParamString);

                    process = new Process { StartInfo = psi };
                    processId = process.Id;

                    // Track the process for cleanup
                    _activeProcesses.TryAdd(processId, process);

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            outputBuilder.AppendLine(args.Data);
                            _logger.LogDebug("PS Output: {Output}", args.Data);
                            }
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                            {
                            errorBuilder.AppendLine(args.Data);
                            _logger.LogWarning("PS Error: {Error}", args.Data);
                            }
                    };

                    process.Start();
                    _logger.LogInformation("Successfully started PowerShell process: {PowerShell} (PID: {ProcessId})", psPath, process.Id);

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait with timeout and cancellation support
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                    try
                        {
                        await process.WaitForExitAsync(cts.Token);
                        _logger.LogInformation("DEBUG: PowerShell process completed with exit code: {ExitCode}", process.ExitCode);
                        }
                    catch (OperationCanceledException)
                        {
                        _logger.LogError("DEBUG: PowerShell process timed out after 10 minutes");
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

                    return output;
                    }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2)
                    {
                    // File not found - try next PowerShell version
                    _logger.LogDebug("PowerShell executable not found: {PowerShell}", psPath);
                    lastException = ex;
                    continue;
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Failed to execute with {PowerShell}, trying next option", psPath);
                    lastException = ex;
                    continue;
                    }
                finally
                    {
                    // Always clean up the process
                    if (process != null)
                        {
                        CleanupProcess(process, processId);
                        }
                    }
                }

            throw new InvalidOperationException("No suitable PowerShell executable found", lastException);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing external PowerShell script: {Script}", scriptPath);

            // Ensure process cleanup even on exception
            if (process != null)
                {
                CleanupProcess(process, processId);
                }

            return $"ERROR: {ex.Message}";
            }
        }

    /// <summary>
    /// Execute simple PowerShell commands using external PowerShell (due to SDK issues)
    /// </summary>
    public async Task<string> RunCommandAsync(string command, Dictionary<string, object>? parameters = null)
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
    public async Task<T?> RunScriptAndGetObjectAsync<T>(string scriptPath, Dictionary<string, object> parameters,
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
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T>(string scriptPath,
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
    private string ExtractJsonFromOutput(string output)
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
    public string GetPowerCliBypassStatus()
    {
        return $"PowerCliConfirmedInstalled: {PowerCliConfirmedInstalled}";
    }

    /// <summary>
    /// Debug method to check if a specific script would get the bypass flag
    /// </summary>
    public bool WouldScriptGetBypass(string scriptPath)
    {
        return PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath);
    }

    /// <summary>
    /// Creates a PSCredential object and passes it to PowerShell scripts
    /// This is the preferred method for script authentication
    /// </summary>
    public async Task<string> RunScriptWithCredentialObjectAsync(string scriptPath, string username, string password,
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
    public async Task<string> RunVCenterScriptAsync(string scriptPath, VCenterConnection connection, string password,
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
    public async Task<string> RunDualVCenterScriptAsync(string scriptPath,
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
            if (!process.HasExited)
            {
                _logger.LogWarning("Forcibly terminating PowerShell process {ProcessId}", process.Id);
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing PowerShell process {ProcessId}", process.Id);
        }
    }

    /// <summary>
    /// Clean up a specific process
    /// </summary>
    private void CleanupProcess (Process process, int processId)
        {
        try
            {
            // Remove from tracking
            _activeProcesses.TryRemove(processId, out _);

            // Ensure process is terminated
            if (!process.HasExited)
                {
                KillProcessSafely(process);
                }

            // Dispose the process object
            process?.Dispose();

            _logger.LogDebug("Cleaned up PowerShell process {ProcessId}", processId);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during process cleanup for {ProcessId}", processId);
            }
        }

    /// <summary>
    /// Periodic cleanup of orphaned processes
    /// </summary>
    private void CleanupOrphanedProcesses (object? state)
        {
        try
            {
            var orphanedProcesses = new List<KeyValuePair<int, Process>>();

            foreach (var kvp in _activeProcesses)
                {
                try
                    {
                    var process = kvp.Value;
                    if (process.HasExited)
                        {
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
                CleanupProcess(orphan.Value, orphan.Key);
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
    /// Force cleanup of all active PowerShell processes
    /// </summary>
    public void CleanupAllProcesses ()
        {
        _logger.LogInformation("Starting cleanup of all active PowerShell processes");

        var processesToCleanup = _activeProcesses.ToArray();

        foreach (var kvp in processesToCleanup)
            {
            CleanupProcess(kvp.Value, kvp.Key);
            }

        _logger.LogInformation("Completed cleanup of {Count} PowerShell processes", processesToCleanup.Length);
        }

    /// <summary>
    /// Get count of currently active PowerShell processes
    /// </summary>
    public int GetActiveProcessCount ()
        {
        return _activeProcesses.Count;
        }

    /// <summary>
    /// Build parameter string with proper escaping (existing method - keep as is)
    /// </summary>
    private StringBuilder BuildParameterString (Dictionary<string, object> parameters, string? logPath)
        {
        var paramString = new StringBuilder();

        foreach (var param in parameters)
            {
            var value = param.Value?.ToString() ?? "";

            if (param.Value is System.Security.SecureString secureString)
                {
                var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secureString);
                try
                    {
                    value = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? "";
                    }
                finally
                    {
                    System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                    }
                }

            if (param.Value is bool boolValue)
                {
                if (boolValue)
                    {
                    paramString.Append($" -{param.Key}");
                    }
                continue;
                }

            var escapedValue = value.Replace("\"", "`\"");
            paramString.Append($" -{param.Key} \"{escapedValue}\"");
            }

        if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
            {
            var escapedLogPath = logPath.Replace("\"", "`\"");
            paramString.Append($" -LogPath \"{escapedLogPath}\"");
            }

        return paramString;
        }

    /// <summary>
    /// Build safe parameter string for logging (existing method - keep as is)
    /// </summary>
    private StringBuilder BuildSafeParameterString (Dictionary<string, object> parameters, string? logPath)
        {
        var safeParamString = new StringBuilder();

        foreach (var param in parameters)
            {
            if (param.Value is bool boolValue && boolValue)
                {
                safeParamString.Append($" -{param.Key}");
                }
            else if (!IsSensitiveParameter(param.Key))
                {
                var value = param.Value?.ToString() ?? "";
                var escapedValue = value.Replace("\"", "`\"");
                safeParamString.Append($" -{param.Key} \"{escapedValue}\"");
                }
            else
                {
                safeParamString.Append($" -{param.Key} \"[REDACTED]\"");
                }
            }

        if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
            {
            var escapedLogPath = logPath.Replace("\"", "`\"");
            safeParamString.Append($" -LogPath \"{escapedLogPath}\"");
            }

        return safeParamString;
        }

    // Keep all your existing methods (RunScriptAsync, RunVCenterScriptAsync, etc.)
    // Just ensure they use the enhanced RunScriptExternalAsync method above

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

    #endregion

    // Include all your existing methods here:
    // - LoadPowerCliStatus()
    // - SavePowerCliStatus()
    // - RunScriptAsync()
    // - RunVCenterScriptAsync()
    // - RunDualVCenterScriptAsync()
    // - IsPowerCliScript()
    // - IsSensitiveParameter()
    // - etc.
    }
    