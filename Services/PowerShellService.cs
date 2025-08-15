using Microsoft.Extensions.Logging;
using Microsoft.PowerShell;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

public class PowerShellService
    {
    private readonly ILogger<PowerShellService> _logger;

    public PowerShellService (ILogger<PowerShellService> logger)
        {
        _logger = logger;
        }

    // Create a proper PowerShell session with full cmdlet support
    private PowerShell CreatePowerShellInstance ()
        {
        try
            {
            // Use CreateDefault() for full cmdlet support
            var initialState = InitialSessionState.CreateDefault();
            initialState.ExecutionPolicy = ExecutionPolicy.Unrestricted;

            // Import essential modules
            var essentialModules = new[]
            {
                "Microsoft.PowerShell.Management",    // Get-Date, Test-Path, etc.
                "Microsoft.PowerShell.Utility",       // Write-Output, ConvertTo-Json, etc.
                "Microsoft.PowerShell.Security",      // Get-ExecutionPolicy, etc.
                "PackageManagement",                   // Package management cmdlets
                "PowerShellGet"                        // Install-Module, Get-PSRepository, etc.
            };

            foreach (var module in essentialModules)
                {
                try
                    {
                    initialState.ImportPSModule(module);
                    _logger.LogDebug("Successfully imported module: {Module}", module);
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning("Could not import module {Module}: {Error}", module, ex.Message);
                    // Continue with other modules even if one fails
                    }
                }

            return PowerShell.Create(initialState);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error creating PowerShell instance with custom session state, falling back to default");
            // Fallback to basic PowerShell instance
            return PowerShell.Create();
            }
        }

    public async Task<string> RunScriptAsync (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        var output = new StringBuilder();

        // Check if this is a command rather than a script file
        if (!scriptPath.Contains("Scripts\\") && !scriptPath.EndsWith(".ps1"))
            {
            return await RunCommandAsync(scriptPath, parameters);
            }

        string fullScriptPath = Path.GetFullPath(scriptPath);

        _logger.LogDebug("Starting PowerShell script execution: {ScriptPath}", fullScriptPath);
        _logger.LogDebug("Script parameters: {@Parameters}", parameters);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return $"ERROR: Script not found at {fullScriptPath}";
            }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await Task.Run(() =>
        {
            using PowerShell ps = CreatePowerShellInstance();

            try
                {
                var scriptContent = File.ReadAllText(fullScriptPath);
                ps.AddScript(scriptContent);

                // Add parameters - avoid duplicate LogPath
                var filteredParameters = new Dictionary<string, object>(parameters);
                if (!string.IsNullOrEmpty(logPath) && !filteredParameters.ContainsKey("LogPath"))
                    {
                    filteredParameters.Add("LogPath", logPath);
                    }

                ps.AddParameters(filteredParameters);
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Failed to read script file: {ScriptPath}", fullScriptPath);
                output.AppendLine($"ERROR: Failed to read script file: {ex.Message}");
                return;
                }

            // Stream handlers
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

            ps.Streams.Verbose.DataAdded += (sender, args) =>
            {
                if (sender is PSDataCollection<VerboseRecord> stream)
                    {
                    var verbose = stream[args.Index].Message;
                    _logger.LogDebug("PS Verbose: {Verbose}", verbose);
                    output.AppendLine($"VERBOSE: {verbose}");
                    }
            };

            ps.Streams.Debug.DataAdded += (sender, args) =>
            {
                if (sender is PSDataCollection<DebugRecord> stream)
                    {
                    var debug = stream[args.Index].Message;
                    _logger.LogDebug("PS Debug: {Debug}", debug);
                    output.AppendLine($"DEBUG: {debug}");
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

            try
                {
                _logger.LogDebug("Invoking PowerShell script...");
                var results = ps.Invoke();
                _logger.LogDebug("Script execution completed. Result count: {ResultCount}", results?.Count ?? 0);

                if (results is not null && results.Count > 0)
                    {
                    foreach (var result in results)
                        {
                        output.AppendLine(result?.BaseObject?.ToString() ?? "<null>");
                        }
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "A fatal error occurred while invoking PowerShell script {ScriptPath}", scriptPath);
                output.AppendLine($"FATAL SCRIPT ERROR: {ex.Message}");
                }
        });

        stopwatch.Stop();
        _logger.LogDebug("PowerShell script execution completed in {ElapsedMs}ms: {ScriptPath}", stopwatch.ElapsedMilliseconds, fullScriptPath);
        return output.ToString();
        }

    // Execute PowerShell commands directly
    public async Task<string> RunCommandAsync (string command, Dictionary<string, object>? parameters = null)
        {
        var output = new StringBuilder();

        _logger.LogDebug("Executing PowerShell command: {Command}", command);

        await Task.Run(() =>
        {
            using PowerShell ps = CreatePowerShellInstance();

            try
                {
                ps.AddScript(command);

                if (parameters != null && parameters.Count > 0)
                    {
                    ps.AddParameters(parameters);
                    }

                var results = ps.Invoke();

                if (results is not null && results.Count > 0)
                    {
                    foreach (var result in results)
                        {
                        output.AppendLine(result?.BaseObject?.ToString() ?? "<null>");
                        }
                    }

                // Check for errors
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

    public async Task<T?> RunScriptAndGetObjectAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        string jsonOutput = string.Empty;
        string fullScriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return default;
            }

        await Task.Run(() =>
        {
            using PowerShell ps = CreatePowerShellInstance();
            try
                {
                ps.AddScript(File.ReadAllText(fullScriptPath));

                // Add parameters - avoid duplicate LogPath
                var filteredParameters = new Dictionary<string, object>(parameters);
                if (!string.IsNullOrEmpty(logPath) && !filteredParameters.ContainsKey("LogPath"))
                    {
                    filteredParameters.Add("LogPath", logPath);
                    }

                ps.AddParameters(filteredParameters);

                var results = ps.Invoke();

                if (results is not null && !ps.HadErrors)
                    {
                    jsonOutput = results.LastOrDefault()?.BaseObject?.ToString() ?? string.Empty;
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Exception during PowerShell script execution: {ScriptPath}", scriptPath);
                }
        });

        if (string.IsNullOrWhiteSpace(jsonOutput))
            {
            return default;
            }
        try
            {
            return JsonSerializer.Deserialize<T>(jsonOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        catch (JsonException ex)
            {
            _logger.LogError(ex, "JSON Deserialization Error for script {ScriptPath}", scriptPath);
            return default;
            }
        }

    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T> (string scriptPath, Dictionary<string, object> parameters, string? logPath = null)
        {
        string jsonOutput = string.Empty;
        string fullScriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(fullScriptPath))
            {
            _logger.LogError("Script not found at path: {ScriptPath}", fullScriptPath);
            return new ObservableCollection<T>();
            }

        await Task.Run(() =>
        {
            using PowerShell ps = CreatePowerShellInstance();
            try
                {
                ps.AddScript(File.ReadAllText(fullScriptPath));

                // Add parameters - avoid duplicate LogPath
                var filteredParameters = new Dictionary<string, object>(parameters);
                if (!string.IsNullOrEmpty(logPath) && !filteredParameters.ContainsKey("LogPath"))
                    {
                    filteredParameters.Add("LogPath", logPath);
                    }

                ps.AddParameters(filteredParameters);

                var results = ps.Invoke();

                if (results is not null && !ps.HadErrors)
                    {
                    jsonOutput = results.LastOrDefault()?.BaseObject?.ToString() ?? string.Empty;
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Exception during PowerShell script execution: {ScriptPath}", scriptPath);
                }
        });

        if (string.IsNullOrWhiteSpace(jsonOutput))
            {
            return new ObservableCollection<T>();
            }
        try
            {
            var items = JsonSerializer.Deserialize<ObservableCollection<T>>(jsonOutput, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return items ?? new ObservableCollection<T>();
            }
        catch (JsonException ex)
            {
            _logger.LogError(ex, "JSON Deserialization Error for script {ScriptPath}", scriptPath);
            return new ObservableCollection<T>();
            }
        }
    }