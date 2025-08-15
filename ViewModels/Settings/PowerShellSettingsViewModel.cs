using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        [RelayCommand]
        private async Task OnCheckPrerequisites ()
            {
            IsCheckingPrerequisites = true;
            PrerequisiteCheckStatus = "Running prerequisite check script...";

            try
                {
                string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

                // Use the enhanced script execution method
                var result = await _powerShellService.RunScriptAndGetObjectAsync<PrerequisitesResult>(
                    ".\\Scripts\\Get-Prerequisites.ps1",
                    new Dictionary<string, object> { { "LogPath", logPath } },
                    logPath);

                if (result != null)
                    {
                    PowerShellVersion = result.PowerShellVersion;
                    IsPowerCliInstalled = result.IsPowerCliInstalled;
                    PrerequisiteCheckStatus = IsPowerCliInstalled ?
                        "Prerequisites check completed. PowerCLI is installed." :
                        "Prerequisites check completed. PowerCLI module not found.";
                    }
                else
                    {
                    // Fallback: try getting raw output and parse manually
                    string rawOutput = await _powerShellService.RunScriptAsync(
                        ".\\Scripts\\Get-Prerequisites.ps1",
                        new Dictionary<string, object> { { "LogPath", logPath } },
                        logPath);

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

        private async Task ParseManualOutput (string output)
            {
            try
                {
                // Try to extract PowerShell version
                if (output.Contains("PowerShell Version:"))
                    {
                    var lines = output.Split('\n');
                    foreach (var line in lines)
                        {
                        if (line.Contains("PowerShell Version:"))
                            {
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                                {
                                PowerShellVersion = parts[1].Trim().Replace("]", "").Replace("[INFO]", "").Trim();
                                break;
                                }
                            }
                        }
                    }

                // Check for PowerCLI indicators in output
                IsPowerCliInstalled = output.Contains("PowerCLI found") ||
                                    output.Contains("VMware.PowerCLI module is installed") ||
                                    output.Contains("successfully imported");

                PrerequisiteCheckStatus = IsPowerCliInstalled ?
                    "Prerequisites parsed successfully. PowerCLI found." :
                    "Prerequisites parsed successfully. PowerCLI not found.";
                }
            catch
                {
                await TrySimpleFallbackCheck();
                }
            }

        private async Task TrySimpleFallbackCheck ()
            {
            try
                {
                // Simple PowerShell version check
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