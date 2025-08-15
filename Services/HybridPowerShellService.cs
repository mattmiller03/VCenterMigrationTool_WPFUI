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

namespace VCenterMigrationTool.Services;

public class HybridPowerShellService
{
    private readonly ILogger<HybridPowerShellService> _logger;

    public HybridPowerShellService(ILogger<HybridPowerShellService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines whether to use internal or external PowerShell based on script requirements
    /// </summary>
    private bool ShouldUseExternalPowerShell(string scriptPath, Dictionary<string, object> parameters)
    {
        // ALWAYS use external PowerShell due to SDK compatibility issues
        // The internal PowerShell SDK has dependency conflicts in this application
        return true;

        /* Original logic kept for reference:
        var externalScripts = new[]
        {
            "Install-PowerCli.ps1",
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
            "Test-VMNetwork.ps1"
        };

        var scriptName = Path.GetFileName(scriptPath);
        return externalScripts.Any(s => s.Equals(scriptName, StringComparison.OrdinalIgnoreCase));
        */
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
    /// Run complex scripts using external PowerShell (for PowerCLI operations)
    /// </summary>
    private async Task<string> RunScriptExternalAsync(string scriptPath, Dictionary<string, object> parameters,
        string? logPath = null)
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
            foreach (var param in parameters)
            {
                // Properly escape parameter values for PowerShell
                var value = param.Value?.ToString() ?? "";

                // Handle different parameter types
                if (param.Value is System.Security.SecureString secureString)
                {
                    // Convert SecureString to plain text for external process
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

                // Escape quotes and wrap in quotes for PowerShell
                var escapedValue = value.Replace("\"", "`\"");
                paramString.Append($" -{param.Key} \"{escapedValue}\"");
            }

            if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
            {
                var escapedLogPath = logPath.Replace("\"", "`\"");
                paramString.Append($" -LogPath \"{escapedLogPath}\"");
            }

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

            // Also add better logging to see which version is being used:
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

                    using var process = new Process { StartInfo = psi };

                    // Add logging to show which PowerShell version was actually used
                    _logger.LogInformation("Successfully started PowerShell process: {PowerShell}", psPath);

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
    /// Extract JSON from mixed script output
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
                using var doc = System.Text.Json.JsonDocument.Parse(firstJson);
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
                using var doc = System.Text.Json.JsonDocument.Parse(candidate);
                return candidate;
            }
            catch
            {
                // Not valid JSON
            }
        }

        return string.Empty;
    }
}