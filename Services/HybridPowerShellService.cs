using Microsoft.Extensions.Logging;
using System;
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

namespace VCenterMigrationTool.Services;

public class HybridPowerShellService
    {
    private readonly ILogger<HybridPowerShellService> _logger;

    public HybridPowerShellService (ILogger<HybridPowerShellService> logger)
        {
        _logger = logger;
        }

    /// <summary>
    /// Determines whether to use internal or external PowerShell based on script requirements
    /// </summary>
    private bool ShouldUseExternalPowerShell (string scriptPath, Dictionary<string, object> parameters)
        {
        // Use external PowerShell for PowerCLI operations or module management
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
        }

    public async Task<string> RunScriptAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        // Check if this is a command rather than a script file
        if (!scriptPath.Contains("Scripts\\") && !scriptPath.EndsWith(".ps1"))
            {
            return await RunCommandAsync(scriptPath, parameters);
            }

        // Decide execution method based on script requirements
        if (ShouldUseExternalPowerShell(scriptPath, parameters))
            {
            _logger.LogDebug("Using external PowerShell for script: {ScriptPath}", scriptPath);
            return await RunScriptExternalAsync(scriptPath, parameters, logPath);
            }
        else
            {
            _logger.LogDebug("Using internal PowerShell for script: {ScriptPath}", scriptPath);
            return await RunScriptInternalAsync(scriptPath, parameters, logPath);
            }
        }

    /// <summary>
    /// Run simple scripts using internal PowerShell (for prerequisites, basic operations)
    /// </summary>
    private async Task<string> RunScriptInternalAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        var output = new StringBuilder();
        string fullScriptPath = Path.GetFullPath(scriptPath);

        _logger.LogDebug("Starting internal PowerShell script execution: {ScriptPath}", fullScriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        await Task.Run(() =>
        {
            using var ps = PowerShell.Create();

            try
                {
                var scriptContent = File.ReadAllText(fullScriptPath);
                ps.AddScript(scriptContent);

                var filteredParameters = new Dictionary<string, object>(parameters);
                if (!string.IsNullOrEmpty(logPath) && !filteredParameters.ContainsKey("LogPath"))
                    {
                    filteredParameters.Add("LogPath", logPath);
                    }

                ps.AddParameters(filteredParameters);

                // Add stream handlers
                ps.Streams.Information.DataAdded += (sender, args) =>
                {
                    if (sender is PSDataCollection<InformationRecord> stream)
                        {
                        var message = stream[args.Index].MessageData.ToString();
                        _logger.LogInformation("PS Info: {Message}", message);
                        output.AppendLine(message);
                        }
                };

                ps.Streams.Warning.DataAdded += (sender, args) =>
                {
                    if (sender is PSDataCollection<WarningRecord> stream)
                        {
                        var warning = stream[args.Index].Message;
                        _logger.LogWarning("PS Warning: {Warning}", warning);
                        output.AppendLine($"WARNING: {warning}");
                        }
                };

                ps.Streams.Error.DataAdded += (sender, args) =>
                {
                    if (sender is PSDataCollection<ErrorRecord> stream)
                        {
                        var errorRecord = stream[args.Index];
                        _logger.LogError("PS Error: {ErrorDetails}", errorRecord.ToString());
                        output.AppendLine($"ERROR: {errorRecord.Exception?.Message}");
                        }
                };

                var results = ps.Invoke();

                if (results?.Count > 0)
                    {
                    foreach (var result in results)
                        {
                        output.AppendLine(result?.BaseObject?.ToString() ?? "<null>");
                        }
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error in internal PowerShell execution");
                output.AppendLine($"INTERNAL ERROR: {ex.Message}");
                }
        });

        return output.ToString();
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
            // Build parameter string
            var paramString = new StringBuilder();
            foreach (var param in parameters)
                {
                // Properly escape parameter values
                var value = param.Value?.ToString()?.Replace("'", "''") ?? "";
                paramString.Append($" -{param.Key} '{value}'");
                }

            if (!string.IsNullOrEmpty(logPath) && !parameters.ContainsKey("LogPath"))
                {
                paramString.Append($" -LogPath '{logPath}'");
                }

            // Try PowerShell 7 first, then fall back to Windows PowerShell
            var powershellPaths = new[]
            {
                "pwsh.exe",  // PowerShell 7 (preferred)
                "powershell.exe"  // Windows PowerShell (fallback)
            };

            Exception? lastException = null;

            foreach (var psPath in powershellPaths)
                {
                try
                    {
                    _logger.LogDebug("Attempting external execution with: {PowerShell}", psPath);

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
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
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
    /// Execute simple PowerShell commands using internal PowerShell
    /// </summary>
    public async Task<string> RunCommandAsync (string command, Dictionary<string, object>? parameters = null)
        {
        var output = new StringBuilder();

        _logger.LogDebug("Executing PowerShell command: {Command}", command);

        await Task.Run(() =>
        {
            using var ps = PowerShell.Create();

            try
                {
                ps.AddScript(command);

                if (parameters?.Count > 0)
                    {
                    ps.AddParameters(parameters);
                    }

                var results = ps.Invoke();

                if (results?.Count > 0)
                    {
                    foreach (var result in results)
                        {
                        output.AppendLine(result?.BaseObject?.ToString() ?? "<null>");
                        }
                    }

                if (ps.HadErrors)
                    {
                    foreach (var error in ps.Streams.Error)
                        {
                        output.AppendLine($"ERROR: {error.Exception?.Message}");
                        _logger.LogError("PS Command Error: {Error}", error.ToString());
                        }
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error executing PowerShell command: {Command}", command);
                output.AppendLine($"COMMAND ERROR: {ex.Message}");
                }
        });

        return output.ToString();
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
    /// Extract JSON from mixed script output
    /// </summary>
    private string ExtractJsonFromOutput (string output)
        {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        var lines = output.Split('\n', '\r', StringSplitOptions.RemoveEmptyEntries);

        // Look for single-line JSON
        foreach (var line in lines)
            {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}"))
                {
                // Quick validation - should contain expected JSON structure
                if (trimmedLine.Contains("\"") && trimmedLine.Length > 10)
                    {
                    return trimmedLine;
                    }
                }
            }

        // Look for multi-line JSON
        int jsonStart = output.IndexOf('{');
        int jsonEnd = output.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
            var candidate = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
            // Basic validation
            if (candidate.Contains("\"") && candidate.Length > 10)
                {
                return candidate;
                }
            }

        return string.Empty;
        }
    }