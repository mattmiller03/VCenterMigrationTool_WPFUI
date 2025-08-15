using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings
    {
    public partial class PowerShellSettingsViewModel : ObservableObject
        {
        private readonly PowerShellService _powerShellService;
        private readonly ConfigurationService _configurationService;

        [ObservableProperty]
        private string _powerShellVersion = "Checking...";

        [ObservableProperty]
        private bool _isPowerCliInstalled;

        [ObservableProperty]
        private bool _isCheckingPrerequisites;

        [ObservableProperty]
        private bool _isInstallingPowerCli;

        [ObservableProperty]
        private string _prerequisiteCheckStatus = "Ready to check prerequisites.";

        [ObservableProperty]
        private string _powerCliInstallStatus = "Ready to install PowerCLI.";

        public PowerShellSettingsViewModel (PowerShellService powerShellService, ConfigurationService configurationService)
            {
            _powerShellService = powerShellService;
            _configurationService = configurationService;
            }

        /// <summary>
        /// This allows the main SettingsViewModel to trigger the check when the page loads.
        /// </summary>
        public async Task InitializeAsync ()
            {
            // We only run the check if it hasn't been run before to avoid unnecessary calls.
            if (PowerShellVersion == "Checking...")
                {
                await OnCheckPrerequisites();
                }
            }
            // Add this temporary debug method to your PowerShellSettingsViewModel

            [RelayCommand]
            private async Task OnDebugPrerequisites ()
            {
                try
                {
                    string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

                    string rawOutput = await _powerShellService.RunScriptAsync(
                        ".\\Scripts\\Get-Prerequisites.ps1",
                        new Dictionary<string, object> { { "LogPath", logPath } },
                        logPath);

                    // Show the raw output in a message box for debugging
                    System.Windows.MessageBox.Show($"Raw Output:\n\n{rawOutput}", "Debug Output",
                        System.Windows.MessageBoxButton.OK);

                    // Also try direct version check
                    var directVersion = await _powerShellService.RunScriptAsync(
                        "$PSVersionTable.PSVersion.ToString()",
                        new Dictionary<string, object>());

                    System.Windows.MessageBox.Show($"Direct Version Check:\n\n{directVersion}", "Direct Version",
                        System.Windows.MessageBoxButton.OK);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show($"Debug Error: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK);
                }
            }
        [RelayCommand]
        private async Task OnCheckPrerequisites ()
            {
            IsCheckingPrerequisites = true;
            PrerequisiteCheckStatus = "Running prerequisite check script...";

            try
                {
                string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

                // Get raw output first for debugging
                string rawOutput = await _powerShellService.RunScriptAsync(
                    ".\\Scripts\\Get-Prerequisites.ps1",
                    new Dictionary<string, object> { { "LogPath", logPath } },
                    logPath);

                // Try to parse JSON from the output
                var jsonResult = ExtractJsonFromOutput(rawOutput);
                bool jsonParsed = false;

                if (!string.IsNullOrWhiteSpace(jsonResult))
                    {
                    try
                        {
                        var result = JsonSerializer.Deserialize<PrerequisitesResult>(jsonResult,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (result != null)
                            {
                            PowerShellVersion = result.PowerShellVersion;
                            IsPowerCliInstalled = result.IsPowerCliInstalled;
                            PrerequisiteCheckStatus = IsPowerCliInstalled ?
                                "Prerequisites check completed. PowerCLI is installed." :
                                "Prerequisites check completed. PowerCLI module not found.";
                            jsonParsed = true;
                            }
                        }
                    catch (JsonException)
                        {
                        // JSON parsing failed, fall back to manual parsing
                        jsonParsed = false;
                        }
                    }

                if (!jsonParsed)
                    {
                    // Fallback: parse manually from the raw output
                    await ParseManualOutput(rawOutput);
                    }
                }
            catch (System.Exception ex)
                {
                PowerShellVersion = "Error during check";
                IsPowerCliInstalled = false;
                PrerequisiteCheckStatus = $"Error during prerequisites check: {ex.Message}";

                // Try a simple fallback check
                await TrySimpleFallbackCheck();
                }
            finally
                {
                IsCheckingPrerequisites = false;
                }
            }

        [RelayCommand]
        private async Task OnInstallPowerCli ()
            {
            IsInstallingPowerCli = true;
            PowerCliInstallStatus = "Installing VMware.PowerCLI module... This may take several minutes.";

            try
                {
                string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
                await _powerShellService.RunScriptAsync(".\\Scripts\\Install-PowerCli.ps1",
                    new Dictionary<string, object> { { "LogPath", logPath } }, logPath);

                PowerCliInstallStatus = "Verifying installation...";
                await OnCheckPrerequisites(); // Re-run check

                PowerCliInstallStatus = IsPowerCliInstalled ?
                    "PowerCLI installation completed successfully." :
                    "PowerCLI installation may have failed. Check logs for details.";
                }
            catch (System.Exception ex)
                {
                PowerCliInstallStatus = $"Installation failed: {ex.Message}";
                }
            finally
                {
                IsInstallingPowerCli = false;
                }
            }

        /// <summary>
        /// Extracts JSON from mixed output that may contain logs and other text
        /// </summary>
        private string ExtractJsonFromOutput (string output)
            {
            if (string.IsNullOrWhiteSpace(output))
                return string.Empty;

            var lines = output.Split('\n', '\r');

            // Look for single-line JSON
            foreach (var line in lines)
                {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}") && trimmedLine.Contains("PowerShellVersion"))
                    {
                    return trimmedLine;
                    }
                }

            // Look for multi-line JSON
            int jsonStart = output.IndexOf('{');
            int jsonEnd = output.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                var candidate = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                if (candidate.Contains("PowerShellVersion"))
                    {
                    return candidate;
                    }
                }

            return string.Empty;
            }

        private async Task ParseManualOutput (string output)
            {
            try
                {
                // Enhanced parsing for PowerShell version
                var lines = output.Split('\n', '\r');

                foreach (var line in lines)
                    {
                    var cleanLine = line.Trim();

                    // Look for various patterns of PowerShell version output
                    if (cleanLine.Contains("PowerShell Version:"))
                        {
                        var parts = cleanLine.Split(':');
                        if (parts.Length > 1)
                            {
                            // Clean up the version string
                            var versionPart = parts[1].Trim()
                                .Replace("]", "")
                                .Replace("[INFO]", "")
                                .Replace("[", "")
                                .Trim();

                            if (!string.IsNullOrWhiteSpace(versionPart))
                                {
                                PowerShellVersion = versionPart;
                                break;
                                }
                            }
                        }
                    // Also look for direct version patterns like "7.4.1"
                    else if (cleanLine.Contains("PowerShell") && System.Text.RegularExpressions.Regex.IsMatch(cleanLine, @"\d+\.\d+\.\d+"))
                        {
                        var versionMatch = System.Text.RegularExpressions.Regex.Match(cleanLine, @"\d+\.\d+\.\d+");
                        if (versionMatch.Success)
                            {
                            PowerShellVersion = versionMatch.Value;
                            break;
                            }
                        }
                    }

                // If we still don't have a version, try the fallback
                if (PowerShellVersion == "Checking..." || PowerShellVersion == "Unknown")
                    {
                    await TryDirectVersionCheck();
                    }

                // Check for PowerCLI indicators in output
                IsPowerCliInstalled = output.Contains("PowerCLI found") ||
                                    output.Contains("VMware.PowerCLI module is installed") ||
                                    output.Contains("successfully imported") ||
                                    output.Contains("PowerCLI successfully imported");

                PrerequisiteCheckStatus = IsPowerCliInstalled ?
                    "Prerequisites parsed successfully. PowerCLI found." :
                    "Prerequisites parsed successfully. PowerCLI not found.";
                }
            catch
                {
                await TrySimpleFallbackCheck();
                }
            }

        private async Task TryDirectVersionCheck ()
            {
            try
                {
                // Direct PowerShell version check
                var versionResult = await _powerShellService.RunScriptAsync(
                    "$PSVersionTable.PSVersion.ToString()",
                    new Dictionary<string, object>());

                if (!string.IsNullOrWhiteSpace(versionResult))
                    {
                    // Clean up the result
                    var cleanVersion = versionResult.Trim().Split('\n')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(cleanVersion))
                        {
                        PowerShellVersion = cleanVersion;
                        }
                    }
                }
            catch
                {
                // If direct check fails, leave it as is
                }
            }

        private async Task TrySimpleFallbackCheck ()
            {
            try
                {
                // Simple PowerShell version check
                await TryDirectVersionCheck();

                if (PowerShellVersion == "Checking...")
                    {
                    PowerShellVersion = "Unable to determine";
                    }

                // Simple PowerCLI check
                var powerCliResult = await _powerShellService.RunScriptAsync(
                    "if (Get-Module -ListAvailable -Name 'VMware.PowerCLI') { 'true' } else { 'false' }",
                    new Dictionary<string, object>());

                IsPowerCliInstalled = powerCliResult?.Trim().ToLower() == "true";

                PrerequisiteCheckStatus = "Fallback check completed.";
                }
            catch
                {
                PowerShellVersion = "Error occurred";
                IsPowerCliInstalled = false;
                PrerequisiteCheckStatus = "Could not determine prerequisites.";
                }
            }
        }
    }