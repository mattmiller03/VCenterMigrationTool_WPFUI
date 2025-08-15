using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings
    {
    public partial class PowerShellSettingsViewModel : ObservableObject
        {
        private readonly PowerShellService _powerShellService;
        private readonly ConfigurationService _configurationService;
        private readonly ILogger<PowerShellSettingsViewModel> _logger;

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

        public PowerShellSettingsViewModel (
            PowerShellService powerShellService,
            ConfigurationService configurationService,
            ILogger<PowerShellSettingsViewModel> logger)
            {
            _powerShellService = powerShellService;
            _configurationService = configurationService;
            _logger = logger;
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

        [RelayCommand]
        private async Task OnCheckPrerequisites ()
            {
            IsCheckingPrerequisites = true;
            PrerequisiteCheckStatus = "Running prerequisite check script...";

            try
                {
                _logger.LogInformation("Starting PowerShell prerequisites check");

                string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

                // Get the raw script output first
                string scriptOutput = await _powerShellService.RunScriptAsync(
                    ".\\Scripts\\Get-Prerequisites.ps1",
                    new Dictionary<string, object> { { "LogPath", logPath } },
                    logPath);

                _logger.LogDebug("Prerequisites script output: {Output}", scriptOutput);

                // Try to extract JSON from the output
                string jsonOutput = ExtractJsonFromOutput(scriptOutput);

                if (!string.IsNullOrWhiteSpace(jsonOutput))
                    {
                    // Try to deserialize the JSON
                    var result = await _powerShellService.RunScriptAndGetObjectAsync<PrerequisitesResult>(
                        ".\\Scripts\\Get-Prerequisites.ps1",
                        new Dictionary<string, object> { { "LogPath", logPath } },
                        logPath);

                    if (result != null)
                        {
                        PowerShellVersion = result.PowerShellVersion;
                        IsPowerCliInstalled = result.IsPowerCliInstalled;
                        PrerequisiteCheckStatus = IsPowerCliInstalled
                            ? "Prerequisites check completed. PowerCLI is installed."
                            : "Prerequisites check completed. PowerCLI module not found.";

                        _logger.LogInformation("Prerequisites check completed. PowerShell: {Version}, PowerCLI: {Installed}",
                            PowerShellVersion, IsPowerCliInstalled);
                        }
                    else
                        {
                        // Fallback: parse manually if deserialization fails
                        await ParseOutputManually(scriptOutput);
                        }
                    }
                else
                    {
                    // No JSON found, parse manually
                    await ParseOutputManually(scriptOutput);
                    }
                }
            catch (System.Exception ex)
                {
                _logger.LogError(ex, "Error during prerequisites check");
                PowerShellVersion = "Error during check";
                IsPowerCliInstalled = false;
                PrerequisiteCheckStatus = $"Error during prerequisites check: {ex.Message}";
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
                _logger.LogInformation("Starting PowerCLI installation");

                string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
                string installOutput = await _powerShellService.RunScriptAsync(
                    ".\\Scripts\\Install-PowerCli.ps1",
                    new Dictionary<string, object> { { "LogPath", logPath } },
                    logPath);

                _logger.LogInformation("PowerCLI installation output: {Output}", installOutput);

                PowerCliInstallStatus = "Verifying installation...";

                // Re-run prerequisite check to verify installation
                await OnCheckPrerequisites();

                PowerCliInstallStatus = IsPowerCliInstalled
                    ? "PowerCLI installation completed successfully."
                    : "PowerCLI installation may have failed. Check logs for details.";
                }
            catch (System.Exception ex)
                {
                _logger.LogError(ex, "Error during PowerCLI installation");
                PowerCliInstallStatus = $"Installation failed: {ex.Message}";
                }
            finally
                {
                IsInstallingPowerCli = false;
                }
            }

        /// <summary>
        /// Attempts to extract JSON from script output that may contain other text
        /// </summary>
        private string ExtractJsonFromOutput (string output)
            {
            if (string.IsNullOrWhiteSpace(output))
                return string.Empty;

            // Look for JSON patterns in the output
            var lines = output.Split('\n', '\r');

            foreach (var line in lines)
                {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}"))
                    {
                    return trimmedLine;
                    }
                }

            // If no single-line JSON found, try to find multi-line JSON
            int jsonStart = output.IndexOf('{');
            int jsonEnd = output.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                return output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }

            return string.Empty;
            }

        /// <summary>
        /// Fallback method to parse output manually when JSON deserialization fails
        /// </summary>
        private async Task ParseOutputManually (string output)
            {
            _logger.LogWarning("Falling back to manual parsing of prerequisites output");

            try
                {
                // Extract PowerShell version from output
                if (output.Contains("PowerShell Version:"))
                    {
                    var versionLine = System.Array.Find(output.Split('\n'), line => line.Contains("PowerShell Version:"));
                    if (!string.IsNullOrEmpty(versionLine))
                        {
                        var parts = versionLine.Split(':');
                        if (parts.Length > 1)
                            {
                            PowerShellVersion = parts[1].Trim().Replace("]", "").Replace("[INFO]", "").Trim();
                            }
                        }
                    }
                else
                    {
                    // Try to get PowerShell version directly
                    var versionResult = await _powerShellService.RunScriptAsync(
                        "$PSVersionTable.PSVersion.ToString()",
                        new Dictionary<string, object>());

                    if (!string.IsNullOrWhiteSpace(versionResult))
                        {
                        PowerShellVersion = versionResult.Trim();
                        }
                    else
                        {
                        PowerShellVersion = "Unable to determine";
                        }
                    }

                // Check for PowerCLI in output
                IsPowerCliInstalled = output.Contains("PowerCLI found") ||
                                    output.Contains("VMware.PowerCLI module found") ||
                                    output.Contains("successfully imported");

                PrerequisiteCheckStatus = IsPowerCliInstalled
                    ? "Prerequisites check completed (manual parsing). PowerCLI found."
                    : "Prerequisites check completed (manual parsing). PowerCLI not found.";

                _logger.LogInformation("Manual parsing completed. PowerShell: {Version}, PowerCLI: {Installed}",
                    PowerShellVersion, IsPowerCliInstalled);
                }
            catch (System.Exception ex)
                {
                _logger.LogError(ex, "Error during manual parsing");
                PowerShellVersion = "Parse error";
                IsPowerCliInstalled = false;
                PrerequisiteCheckStatus = "Error parsing prerequisites output.";
                }
            }
        }
    }