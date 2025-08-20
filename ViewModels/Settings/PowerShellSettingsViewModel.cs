using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings
    {
    public partial class PowerShellSettingsViewModel : ObservableObject, IDisposable
        {
        private readonly HybridPowerShellService _powerShellService;
        private readonly ConfigurationService _configurationService;
        private readonly ILogger<PowerShellSettingsViewModel> _logger;

        [ObservableProperty]
        private string _powerShellVersion = "Checking...";
        [ObservableProperty]
        private string _debugBypassStatus = "Debug: Bypass flag not set";

        [ObservableProperty]
        private int _activeProcessCount;

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

        [ObservableProperty]
        private string _installationPhase = "";

        [ObservableProperty]
        private string _processMonitoringStatus = "No active processes";

        private Timer? _processMonitorTimer;
        public void StartProcessMonitoring ()
        {
            _processMonitorTimer = new Timer(UpdateProcessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void StopProcessMonitoring ()
        {
            _processMonitorTimer?.Dispose();
            _processMonitorTimer = null;
        }

        public void Dispose()
        {
            StopProcessMonitoring();
        }
        private void UpdateProcessCount (object? state)
        {
            try
            {
                ActiveProcessCount = _powerShellService.GetActiveProcessCount();
                ProcessMonitoringStatus = ActiveProcessCount == 0
                    ? "No active PowerShell processes"
                    : $"{ActiveProcessCount} active PowerShell process{(ActiveProcessCount == 1 ? "" : "es")}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating process count");
                ProcessMonitoringStatus = "Error monitoring processes";
            }
        }
        public PowerShellSettingsViewModel (
            HybridPowerShellService powerShellService,
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

        // And add this method to your PowerShellSettingsViewModel.cs for testing
        [RelayCommand]
        private void OnDebugBypassStatus ()
        {
            var status = _powerShellService.GetPowerCliBypassStatus();
            var wouldBypass = _powerShellService.WouldScriptGetBypass("Test-vCenterConnection.ps1");

            _logger.LogInformation("Debug: {Status}", status);
            _logger.LogInformation("Debug: Test-vCenterConnection.ps1 would get bypass: {WouldBypass}", wouldBypass);

            // You could also update a UI property to show this info
            PrerequisiteCheckStatus = $"Debug: PowerCLI bypass = {HybridPowerShellService.PowerCliConfirmedInstalled}, Script would bypass = {wouldBypass}";
        }

        [RelayCommand]
        private async Task OnCheckPrerequisites ()
        {
            IsCheckingPrerequisites = true;
            PrerequisiteCheckStatus = "Running prerequisite check script...";

            try
            {
                // Use the new CheckPrerequisitesAsync method which automatically uses the configured log path
                string rawOutput = await _powerShellService.CheckPrerequisitesAsync();

                // Try to parse as JSON first
                var result = TryParsePrerequisitesJson(rawOutput);

                if (result != null)
                {
                    // Successfully parsed JSON
                    PowerShellVersion = result.PowerShellVersion;
                    IsPowerCliInstalled = result.IsPowerCliInstalled;

                    // Save the PowerCLI status persistently
                    _powerShellService.SavePowerCliStatus(result.IsPowerCliInstalled);

                    PrerequisiteCheckStatus = IsPowerCliInstalled ?
                        "Prerequisites check completed. PowerCLI is installed." :
                        "Prerequisites check completed. PowerCLI module not found.";

                    if (IsPowerCliInstalled)
                    {
                        _logger.LogInformation("PowerCLI confirmed installed - status saved persistently");
                    }
                }
                else
                {
                    // Fallback: parse manually from raw output
                    await ParseManualOutput(rawOutput);
                }
            }
            catch (Exception ex)
            {
                PowerShellVersion = "Error during check";
                IsPowerCliInstalled = false;

                // Clear and save bypass flag on error
                _powerShellService.SavePowerCliStatus(false);

                PrerequisiteCheckStatus = $"Error during prerequisites check: {ex.Message}";

                // Try a simple fallback check
                await TrySimpleFallbackCheck();
            }
            finally
            {
                IsCheckingPrerequisites = false;
                DebugBypassStatus = $"Debug: PowerCLI bypass = {HybridPowerShellService.PowerCliConfirmedInstalled}";
            }
        }

        // Helper method to try parsing JSON from the output
        private PrerequisitesResult? TryParsePrerequisitesJson (string output)
        {
            try
            {
                // Look for JSON in the output
                var lines = output.Split('\n', '\r', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("{") && trimmedLine.EndsWith("}"))
                    {
                        try
                        {
                            var result = JsonSerializer.Deserialize<PrerequisitesResult>(trimmedLine,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            return result;
                        }
                        catch
                        {
                            continue; // Try next line
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        [RelayCommand]
        private void CleanupProcesses ()
        {
            try
            {
                _powerShellService.CleanupAllProcesses();
                ProcessMonitoringStatus = "All PowerShell processes cleaned up";
                _logger.LogInformation("Manual PowerShell process cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during manual process cleanup");
                ProcessMonitoringStatus = "Error during cleanup";
            }
        }

        [RelayCommand]
        private async Task OnInstallPowerCli ()
            {
            IsInstallingPowerCli = true;
            PowerCliInstallStatus = "Preparing PowerCLI installation...";

            try
                {
                // Get the configured log path
                string logPath = _configurationService.GetConfiguration().LogPath;
                var parameters = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(logPath))
                    {
                    parameters["LogPath"] = logPath;
                    }

                // Update status during different phases
                PowerCliInstallStatus = "Connecting to PowerShell Gallery...";
                await Task.Delay(500); // Brief pause for UI update

                PowerCliInstallStatus = "Downloading VMware.PowerCLI module... (This may take 3-5 minutes)";

                // Use RunScriptOptimizedAsync with the parameters (this will auto-add LogPath if needed)
                string result = await _powerShellService.RunScriptOptimizedAsync(
                    ".\\Scripts\\Install-PowerCli.ps1",
                    parameters);

                // Parse the result to determine success/failure
                if (result.Contains("Success:"))
                    {
                    PowerCliInstallStatus = "PowerCLI installation completed successfully!";

                    // Automatically re-check prerequisites to update the UI
                    await Task.Delay(1000); // Give user a moment to see success message
                    await OnCheckPrerequisites(); // This will update IsPowerCliInstalled and save status
                    }
                else if (result.Contains("already installed"))
                    {
                    PowerCliInstallStatus = "PowerCLI was already installed.";
                    await OnCheckPrerequisites(); // Update the UI status and save
                    }
                else
                    {
                    // Installation failed
                    var errorMessage = "PowerCLI installation failed.";
                    if (result.Contains("Failure:"))
                        {
                        var failureIndex = result.IndexOf("Failure:");
                        var errorLine = result.Substring(failureIndex).Split('\n')[0];
                        errorMessage = errorLine.Replace("Failure:", "").Trim();
                        }

                    PowerCliInstallStatus = $"Installation failed: {errorMessage}";

                    // Clear and save bypass flag on installation failure
                    _powerShellService.SavePowerCliStatus(false);
                    }
                }
            catch (Exception ex)
                {
                PowerCliInstallStatus = $"Installation failed: {ex.Message}";
                _powerShellService.SavePowerCliStatus(false); // Clear on error
                _logger.LogError(ex, "Error during PowerCLI installation");
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

                // FIXED: Save bypass flag based on result
                _powerShellService.SavePowerCliStatus(IsPowerCliInstalled);

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

                // FIXED: Save bypass flag based on result
                _powerShellService.SavePowerCliStatus(IsPowerCliInstalled);

                PrerequisiteCheckStatus = "Fallback check completed.";
            }
            catch
            {
                PowerShellVersion = "Error occurred";
                IsPowerCliInstalled = false;
                _powerShellService.SavePowerCliStatus(false);
                PrerequisiteCheckStatus = "Could not determine prerequisites.";
            }
        }

        /// <summary>
        /// Optional: Add a method to provide real-time updates during installation
        /// </summary>
        private void UpdateInstallationStatus (string phase, string details)
            {
            InstallationPhase = phase;
            PowerCliInstallStatus = $"{phase}: {details}";
            }
        }
    }