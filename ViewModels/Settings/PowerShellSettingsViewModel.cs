using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        /// <summary>
        /// Collection of required modules and their status
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<ModuleInfo> _requiredModules = new();

        /// <summary>
        /// Overall status of all required modules
        /// </summary>
        [ObservableProperty]
        private string _overallModuleStatus = "Ready to check modules";

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
            
            // Initialize required modules collection
            InitializeRequiredModules();
            }

        /// <summary>
        /// Initialize the required modules collection
        /// </summary>
        private void InitializeRequiredModules()
        {
            RequiredModules.Clear();
            RequiredModules.Add(ModuleInfo.CreatePowerCLI());
            RequiredModules.Add(ModuleInfo.CreateExecutionPolicy());
            RequiredModules.Add(ModuleInfo.CreateDotNetFramework());
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
            PrerequisiteCheckStatus = "Checking prerequisites...";
            OverallModuleStatus = "Checking modules...";

            try
            {
                // Check PowerShell version directly
                await CheckPowerShellVersion();
                
                // Check each required module directly
                await CheckRequiredModules();
                
                // Update overall status based on module results
                UpdateOverallStatus();

                PrerequisiteCheckStatus = "Prerequisites check completed.";
                _logger.LogInformation("Prerequisites check completed successfully");
            }
            catch (Exception ex)
            {
                PowerShellVersion = "Error during check";
                PrerequisiteCheckStatus = $"Error during prerequisites check: {ex.Message}";
                OverallModuleStatus = "Error checking modules";
                _logger.LogError(ex, "Error during prerequisites check");
            }
            finally
            {
                IsCheckingPrerequisites = false;
                DebugBypassStatus = $"Debug: PowerCLI bypass = {HybridPowerShellService.PowerCliConfirmedInstalled}";
            }
        }

        /// <summary>
        /// Check PowerShell version directly
        /// </summary>
        private async Task CheckPowerShellVersion()
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
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get PowerShell version directly");
            }
            
            PowerShellVersion = "Unable to determine";
        }

        /// <summary>
        /// Check all required modules directly
        /// </summary>
        private async Task CheckRequiredModules()
        {
            foreach (var module in RequiredModules)
            {
                module.Status = "Checking...";
                await CheckIndividualModule(module);
            }
        }

        /// <summary>
        /// Check an individual module's status
        /// </summary>
        private async Task CheckIndividualModule(ModuleInfo module)
        {
            try
            {
                switch (module.Name)
                {
                    case "VMware.PowerCLI":
                        await CheckPowerCLIModule(module);
                        break;
                    case "ExecutionPolicy":
                        await CheckExecutionPolicy(module);
                        break;
                    case ".NET Framework":
                        CheckDotNetFramework(module);
                        break;
                    default:
                        module.Status = "Unknown module type";
                        break;
                }
            }
            catch (Exception ex)
            {
                module.Status = $"Error: {ex.Message}";
                module.IsInstalled = false;
                _logger.LogError(ex, "Error checking module {ModuleName}", module.Name);
            }
        }

        /// <summary>
        /// Check PowerCLI module status directly
        /// </summary>
        private async Task CheckPowerCLIModule(ModuleInfo module)
        {
            try
            {
                // Check if VMware.PowerCLI is available
                var powerCliResult = await _powerShellService.RunCommandAsync(
                    "Get-Module -ListAvailable -Name 'VMware.PowerCLI' | Select-Object -First 1 | ForEach-Object { \"$($_.Name):$($_.Version)\" }");

                if (!string.IsNullOrWhiteSpace(powerCliResult) && powerCliResult.Contains("VMware.PowerCLI"))
                {
                    var parts = powerCliResult.Trim().Split(':');
                    if (parts.Length >= 2)
                    {
                        module.IsInstalled = true;
                        module.InstalledVersion = parts[1];
                        module.Status = "Installed";
                        
                        // Save PowerCLI status persistently
                        _powerShellService.SavePowerCliStatus(true);
                        IsPowerCliInstalled = true;
                    }
                }
                else
                {
                    // Check for individual VMware modules as fallback
                    var coreModuleResult = await _powerShellService.RunCommandAsync(
                        "Get-Module -ListAvailable -Name 'VMware.VimAutomation.Core' | Select-Object -First 1 | ForEach-Object { \"$($_.Name):$($_.Version)\" }");
                    
                    if (!string.IsNullOrWhiteSpace(coreModuleResult) && coreModuleResult.Contains("VMware.VimAutomation.Core"))
                    {
                        var parts = coreModuleResult.Trim().Split(':');
                        module.IsInstalled = true;
                        module.InstalledVersion = parts.Length >= 2 ? parts[1] : "Available";
                        module.Status = "Core modules installed";
                        
                        _powerShellService.SavePowerCliStatus(true);
                        IsPowerCliInstalled = true;
                    }
                    else
                    {
                        module.IsInstalled = false;
                        module.InstalledVersion = "Not Installed";
                        module.Status = "Not installed";
                        
                        _powerShellService.SavePowerCliStatus(false);
                        IsPowerCliInstalled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                module.IsInstalled = false;
                module.Status = $"Check failed: {ex.Message}";
                _powerShellService.SavePowerCliStatus(false);
                IsPowerCliInstalled = false;
            }
        }

        /// <summary>
        /// Check execution policy status
        /// </summary>
        private async Task CheckExecutionPolicy(ModuleInfo module)
        {
            try
            {
                var policyResult = await _powerShellService.RunCommandAsync("Get-ExecutionPolicy -Scope CurrentUser");
                
                if (!string.IsNullOrWhiteSpace(policyResult))
                {
                    var policy = policyResult.Trim();
                    module.InstalledVersion = policy;
                    
                    if (policy == "RemoteSigned" || policy == "Unrestricted" || policy == "Bypass")
                    {
                        module.IsInstalled = true;
                        module.Status = "Configured";
                    }
                    else
                    {
                        module.IsInstalled = false;
                        module.Status = "Restrictive policy";
                    }
                }
                else
                {
                    module.IsInstalled = false;
                    module.Status = "Unable to determine";
                }
            }
            catch (Exception ex)
            {
                module.IsInstalled = false;
                module.Status = $"Check failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Check .NET Framework status
        /// </summary>
        private void CheckDotNetFramework(ModuleInfo module)
        {
            try
            {
                // Check .NET Framework version through environment
                var version = Environment.Version;
                module.InstalledVersion = version.ToString();
                module.IsInstalled = version.Major >= 4;
                module.Status = module.IsInstalled ? "Available" : "Version too old";
            }
            catch (Exception ex)
            {
                module.IsInstalled = false;
                module.Status = $"Check failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Update overall status based on individual module results
        /// </summary>
        private void UpdateOverallStatus()
        {
            var installedCount = 0;
            var totalRequired = 0;
            
            foreach (var module in RequiredModules)
            {
                if (module.IsRequired)
                {
                    totalRequired++;
                    if (module.IsInstalled)
                        installedCount++;
                }
            }
            
            if (installedCount == totalRequired)
            {
                OverallModuleStatus = $"All {totalRequired} required modules are available";
            }
            else
            {
                OverallModuleStatus = $"{installedCount}/{totalRequired} required modules available";
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
                    ".\\Scripts\\Active\\Install-PowerCli.ps1",
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