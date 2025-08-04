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
using Wpf.Ui.Abstractions.Controls;
using VCenterMigrationTool.Views;

namespace VCenterMigrationTool.Services
{
    /// <summary>
    /// A service to execute PowerShell scripts and process their output.
    /// </summary>
    public class PowerShellService
    {
        /// <summary>
        /// Runs a script and returns its output streams as a single string.
        /// </summary>
        /// <param name="scriptPath">The relative path to the .ps1 script file.</param>
        /// <param name="parameters">A dictionary of parameters to pass to the script.</param>
        /// <returns>A string containing the script's output and any errors.</returns>
        public async Task<string> RunScriptAsync(string scriptPath, Dictionary<string, object> parameters)
        {
            var output = new StringBuilder();
            string fullScriptPath = Path.GetFullPath(scriptPath);

            if (!File.Exists(fullScriptPath))
            {
                return $"ERROR: Script not found at {fullScriptPath}";
            }

            // Run the script on a background thread to keep the UI responsive.
            await Task.Run(() =>
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript(File.ReadAllText(fullScriptPath));

                    if (parameters != null)
                    {
                        ps.AddParameters(parameters);
                    }

                    // Capture all output streams
                    ps.Streams.Information.DataAdded += (sender, args) => output.AppendLine(ps.Streams.Information[args.Index].MessageData.ToString());
                    ps.Streams.Warning.DataAdded += (sender, args) => output.AppendLine($"WARNING: {ps.Streams.Warning[args.Index].Message}");
                    ps.Streams.Error.DataAdded += (sender, args) => output.AppendLine($"ERROR: {ps.Streams.Error[args.Index].Exception}");

                    try
                    {
                        ps.Invoke();
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"FATAL SCRIPT ERROR: {ex.Message}");
                    }
                }
            });

            return output.ToString();
        }

        /// <summary>
        /// Runs a script that outputs a JSON string and deserializes it into a collection of C# objects.
        /// </summary>
        /// <typeparam name="T">The model type to deserialize the JSON into.</typeparam>
        /// <param name="scriptPath">The relative path to the .ps1 script file.</param>
        /// <param name="parameters">A dictionary of parameters to pass to the script.</param>
        /// <returns>An ObservableCollection of the specified type.</returns>
        public async Task<ObservableCollection<T>> RunScriptAndGetObjectsAsync<T>(string scriptPath, Dictionary<string, object> parameters)
        {
            var collection = new ObservableCollection<T>();
            string jsonOutput = "";
            string fullScriptPath = Path.GetFullPath(scriptPath);

            if (!File.Exists(fullScriptPath))
            {
                // Optionally log this error
                return collection;
            }

            await Task.Run(() =>
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript(File.ReadAllText(fullScriptPath));

                    if (parameters != null)
                    {
                        ps.AddParameters(parameters);
                    }

                    // Execute the script and get the results
                    var results = ps.Invoke();

                    if (ps.HadErrors)
                    {
                        // Log errors if necessary
                        foreach (var error in ps.Streams.Error)
                        {
                            Console.WriteLine(error.ToString());
                        }
                        return;
                    }

                    // The last object in the pipeline should be our JSON string
                    if (results.Any())
                    {
                        jsonOutput = results.Last().BaseObject.ToString();
                    }
                }
            });

            if (!string.IsNullOrEmpty(jsonOutput))
            {
                try
                {
                    // Use case-insensitive property matching for flexibility
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var items = JsonSerializer.Deserialize<ObservableCollection<T>>(jsonOutput, options);
                    if (items != null)
                    {
                        collection = items;
                    }
                }
                catch (JsonException ex)
                {
                    // Log the deserialization error
                    Console.WriteLine($"JSON Deserialization Error: {ex.Message}");
                }
            }

            return collection;
        }
    }
}