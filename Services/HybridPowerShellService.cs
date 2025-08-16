using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.Services;

public class HybridPowerShellService
    {
    private readonly ILogger<HybridPowerShellService> _logger;
    private readonly ConfigurationService _configurationService;

    /// <summary>
    /// Static flag to track PowerCLI availability (set by settings page)
    /// </summary>
    public static bool PowerCliConfirmedInstalled { get; set; } = false;

    public HybridPowerShellService (ILogger<HybridPowerShellService> logger, ConfigurationService configurationService)
        {
        _logger = logger;
        _configurationService = configurationService;

        // FIXED: Load PowerCLI status from persistent storage on startup
        LoadPowerCliStatus();
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
                    var quickCheck = await RunCommandAsync("if (Get-Module -ListAvailable -Name 'VMware.PowerCLI') { 'true' } else { 'false' }");
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

    public async Task<string> RunScriptAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
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
    public async Task<string> RunScriptOptimizedAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        // Clone parameters to avoid modifying the original
        var optimizedParameters = new Dictionary<string, object>(parameters);

        // DEBUG: Log the current state
        _logger.LogInformation("DEBUG: PowerCliConfirmedInstalled = {PowerCliConfirmed}", PowerCliConfirmedInstalled);
        _logger.LogInformation("DEBUG: IsPowerCliScript({ScriptPath}) = {IsPowerCli}", scriptPath, IsPowerCliScript(scriptPath));

        // Add bypass flag for PowerCLI scripts when we know it's installed
        if (PowerCliConfirmedInstalled && IsPowerCliScript(scriptPath))
            {
            optimizedParameters["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: Adding BypassModuleCheck=true for script: {ScriptPath}", scriptPath);

            // SECURE: Log parameters without sensitive data
            var safeParams = optimizedParameters
                .Where(p => !IsSensitiveParameter(p.Key))
                .Select(p => $"{p.Key}={p.Value}");
            _logger.LogInformation("DEBUG: Final parameters (excluding sensitive): {Parameters}", string.Join(", ", safeParams));
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
    public async Task<T?> RunScriptAndGetObjectOptimizedAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
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
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsOptimizedAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
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
            "ValidateVMBackups.ps1"
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

    /// <summary>
    /// Run complex scripts using external PowerShell (for PowerCLI operations)
    /// </summary>
    private async Task<string> RunScriptExternalAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        string fullScriptPath = Path.GetFullPath(scriptPath);

        _logger.LogDebug("Starting external PowerShell script execution: {ScriptPath}", fullScriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        try
            {
            // Build parameter string with proper escaping
            var paramString = new StringBuilder();

            // SECURE: Log parameter count but not sensitive values
            var paramCount = parameters.Count;
            var sensitiveParamCount = parameters.Keys.Count(IsSensitiveParameter);
            _logger.LogInformation("DEBUG: Building parameter string from {ParameterCount} parameters ({SensitiveCount} sensitive)",
                paramCount, sensitiveParamCount);

            foreach (var param in parameters)
                {
                // Properly escape parameter values for PowerShell
                var value = param.Value?.ToString() ?? "";

                // SECURE: Only log non-sensitive parameters
                if (!IsSensitiveParameter(param.Key))
                    {
                    _logger.LogInformation("DEBUG: Parameter {Key} = {Value} (Type: {Type})", param.Key, value, param.Value?.GetType().Name ?? "null");
                    }
                else
                    {
                    _logger.LogInformation("DEBUG: Parameter {Key} = [REDACTED] (Type: {Type})", param.Key, param.Value?.GetType().Name ?? "null");
                    }

                // Handle different parameter types
                if (param.Value is System.Security.SecureString secureString)
                    {
                    // Convert SecureString to plain text for external process
                    var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secureString);
                    try
                        {
                        value = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? "";
                        _logger.LogInformation("DEBUG: Converted SecureString parameter {Key}", param.Key);
                        }
                    finally
                        {
                        System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                        }
                    }

                // FIXED: Handle boolean parameters correctly (like BypassModuleCheck)
                if (param.Value is bool boolValue)
                    {
                    // For PowerShell switches, we add the parameter name without a value when true
                    if (boolValue)
                        {
                        paramString.Append($" -{param.Key}");
                        _logger.LogInformation("DEBUG: Added switch parameter: -{Key}", param.Key);
                        }
                    // If false, we don't add the parameter at all
                    continue;
                    }

                // Escape quotes and wrap in quotes for PowerShell
                var escapedValue = value.Replace("\"", "`\"");
                paramString.Append($" -{param.Key} \"{escapedValue}\"");
                }

            // FIXED: Don't add LogPath if it's already in parameters
            if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
                {
                var escapedLogPath = logPath.Replace("\"", "`\"");
                paramString.Append($" -LogPath \"{escapedLogPath}\"");
                }

            // SECURE: Create a safe version of the command for logging (without sensitive data)
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

            _logger.LogInformation("DEBUG: Safe parameter string: {SafeParamString}", safeParamString.ToString());

            // Prioritize PowerShell 7 with multiple fallback paths
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

                    // SECURE: Log safe command (without sensitive data)
                    var safeCommand = $"-NoProfile -ExecutionPolicy Unrestricted -File \"{fullScriptPath}\"{safeParamString}";
                    _logger.LogInformation("DEBUG: Executing safe command: {FileName} {SafeArguments}", psPath, safeCommand);

                    using var process = new Process { StartInfo = psi };

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
                    _logger.LogInformation("Successfully started PowerShell process: {PowerShell}", psPath);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Wait with timeout (10 minutes for large operations like PowerCLI install)
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10));
                    try
                        {
                        await process.WaitForExitAsync(cts.Token);
                        }
                    catch (OperationCanceledException)
                        {
                        process.Kill();
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
                }

            throw new InvalidOperationException("No suitable PowerShell executable found", lastException);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error executing external PowerShell script: {Script}", scriptPath);
            return $"ERROR: {ex.Message}";
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
    public async Task<T?> RunScriptAndGetObjectAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
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
            _logger.LogError(ex, "JSON deserialization error for script {Script}. JSON: {Json}", scriptPath, jsonResult);
            return default;
            }
        }

    /// <summary>
    /// Run script and deserialize JSON output to collection
    /// </summary>
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
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
            _logger.LogError(ex, "JSON deserialization error for collection in script {Script}. JSON: {Json}", scriptPath, jsonResult);
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
    }