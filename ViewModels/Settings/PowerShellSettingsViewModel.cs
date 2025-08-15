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
        // UPDATED: Use HybridPowerShellService instead of PowerShellService
        private readonly HybridPowerShellService _powerShellService;
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

        // UPDATED: Constructor now takes HybridPowerShellService
        public PowerShellSettingsViewModel (HybridPowerShellService powerShellService, ConfigurationService configurationService)
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
                var parameters = new Dictionary<string, object> { { "LogPath", logPath } };

                // Try to get structured result first
                var result = await _powerShellService.RunScriptAndGetObjectAsync<PrerequisitesResult>(
                    ".\\Scripts\\Get-Prerequisites.ps1",
                    parameters);

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
                    // Fallback: get raw output and parse manually
                    string rawOutput = await _powerShellService.RunScriptAsync(
                        ".\\Scripts\\Get-Prerequisites.ps1",
                        parameters);

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
                var parameters = new Dictionary<string, object> { { "LogPath", logPath } };

                // The hybrid service will automatically use external PowerShell for Install-PowerCli.ps1
                string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Install-PowerCli.ps1", parameters);

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
        /// Parse output manually when JSON deserialization fails
        /// </summary>
        private async Task ParseManualOutput (string output)
            {
            try
                {
                var lines = output.Split('\n', '\r');

                foreach (var line in lines)
                    {
                    var cleanLine = line.Trim();

                    if (cleanLine.Contains("PowerShell Version:"))
                        {
                        var parts = cleanLine.Split(':');
                        if (parts.Length > 1)
                            {
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
                    }

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
                var versionResult = await _powerShellService.RunCommandAsync("$PSVersionTable.PSVersion.ToString()");

                if (!string.IsNullOrWhiteSpace(versionResult))
                    {
                    var cleanVersion = versionResult.Trim().Split('\n')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(cleanVersion) && !cleanVersion.Contains("ERROR"))
                        {
                        PowerShellVersion = cleanVersion;
                        }
                    }
                }
            catch
                {
                // If direct check fails, leave as is
                }
            }

        private async Task TrySimpleFallbackCheck ()
            {
            try
                {
                await TryDirectVersionCheck();

                if (PowerShellVersion == "Checking...")
                    {
                    PowerShellVersion = "Unable to determine";
                    }

                var powerCliResult = await _powerShellService.RunCommandAsync(
                    "if (Get-Module -ListAvailable -Name 'VMware.PowerCLI') { 'true' } else { 'false' }");

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