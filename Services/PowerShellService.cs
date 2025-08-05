// In Services/PowerShellService.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

/// <summary>
/// A service to execute PowerShell scripts and process their output.
/// </summary>
public class PowerShellService
{
    /// <summary>
    /// Runs a script and returns its console output streams as a single string.
    /// </summary>
    public async Task<string> RunScriptAsync(string scriptPath, Dictionary<string, object> parameters)
    {
        var output = new StringBuilder();
        string fullScriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(fullScriptPath))
        {
            return $"ERROR: Script not found at {fullScriptPath}";
        }

        await Task.Run(() =>
        {
            using PowerShell ps = PowerShell.Create();

            ps.AddScript(File.ReadAllText(fullScriptPath));
            ps.AddParameters(parameters);

            // Capture all output streams for logging
            ps.Streams.Information.DataAdded += (_, args) => output.AppendLine(ps.Streams.Information[args.Index].MessageData.ToString());
            ps.Streams.Warning.DataAdded += (_, args) => output.AppendLine($"WARNING: {ps.Streams.Warning[args.Index].Message}");
            ps.Streams.Error.DataAdded += (_, args) => output.AppendLine($"ERROR: {ps.Streams.Error[args.Index].Exception}");

            try
            {
                ps.Invoke();
            }
            catch (Exception ex)
            {
                output.AppendLine($"FATAL SCRIPT ERROR: {ex.Message}");
            }
        });

        return output.ToString();
    }

    /// <summary>
    /// Runs a script that outputs a JSON string and safely deserializes it into a collection of C# objects.
    /// </summary>
    public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T>(string scriptPath, Dictionary<string, object> parameters)
    {
        string jsonOutput = string.Empty;
        string fullScriptPath = Path.GetFullPath(scriptPath);

        if (!File.Exists(fullScriptPath))
        {
            // If the script doesn't exist, return an empty collection immediately.
            return new ObservableCollection<T>();
        }

        await Task.Run(() =>
        {
            using PowerShell ps = PowerShell.Create();

            ps.AddScript(File.ReadAllText(fullScriptPath));
            ps.AddParameters(parameters);

            // Execute the script and get the results
            var results = ps.Invoke();

            if (ps.HadErrors)
            {
                // If the script had errors, log them and stop.
                foreach (var error in ps.Streams.Error)
                {
                    Console.WriteLine($"PowerShell Error: {error}");
                }
                return;
            }

            // The last object in the pipeline should be our JSON string.
            // Safely get it to avoid errors.
            jsonOutput = results.LastOrDefault()?.BaseObject.ToString() ?? string.Empty;
        });

        // If we didn't get any JSON output, return an empty collection.
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            return new ObservableCollection<T>();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // Attempt to deserialize. If it fails or returns null, the catch block or the null-coalescing operator will handle it.
            var items = JsonSerializer.Deserialize<ObservableCollection<T>>(jsonOutput, options);

            return items ?? new ObservableCollection<T>();
        }
        catch (JsonException ex)
        {
            // If the JSON is malformed, log the error and return an empty collection.
            Console.WriteLine($"JSON Deserialization Error: {ex.Message}");
            return new ObservableCollection<T>();
        }
    }
}