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

        // --- FIX: Add this public method ---
        // This allows the main SettingsViewModel to trigger the check when the page loads.
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

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
            var result = await _powerShellService.RunScriptAndGetObjectAsync<PrerequisitesResult>(
                ".\\Scripts\\Get-Prerequisites.ps1", new Dictionary<string, object>(), logPath);

            if (result != null)
                {
                PowerShellVersion = result.PowerShellVersion;
                IsPowerCliInstalled = result.IsPowerCliInstalled;
                PrerequisiteCheckStatus = IsPowerCliInstalled ? "Prerequisites check completed." : "PowerCLI module not found.";
                }
            else
                {
                PowerShellVersion = "Failed to get version";
                PrerequisiteCheckStatus = "Failed to retrieve prerequisite information.";
                }
            IsCheckingPrerequisites = false;
            }

        [RelayCommand]
        private async Task OnInstallPowerCli ()
            {
            IsInstallingPowerCli = true;
            PowerCliInstallStatus = "Installing VMware.PowerCLI module... This may take several minutes.";

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
            await _powerShellService.RunScriptAsync(".\\Scripts\\Install-PowerCli.ps1", new Dictionary<string, object>(), logPath);

            PowerCliInstallStatus = "Verifying installation...";
            await OnCheckPrerequisites(); // Re-run check

            PowerCliInstallStatus = IsPowerCliInstalled ? "PowerCLI installation verified." : "Installation may have failed.";
            IsInstallingPowerCli = false;
            }
        }
    }