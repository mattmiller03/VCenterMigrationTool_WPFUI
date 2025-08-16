using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace VCenterMigrationTool.ViewModels;

public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private string _scriptOutput = "Script output will be displayed here...";

    public ObservableCollection<VCenterConnection> Profiles { get; }

    [ObservableProperty]
    private VCenterConnection? _selectedSourceProfile;

    [ObservableProperty]
    private VCenterConnection? _selectedTargetProfile;

    [ObservableProperty]
    private string _sourceConnectionStatus = "Connection not active";

    [ObservableProperty]
    private string _targetConnectionStatus = "Connection not active";

    [ObservableProperty]
    private bool _isJobRunning;

    [ObservableProperty]
    private string _currentJobText = "No active jobs.";

    [ObservableProperty]
    private int _jobProgress;

    public DashboardViewModel (
        HybridPowerShellService powerShellService,
        ConnectionProfileService profileService,
        CredentialService credentialService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        IDialogService dialogService,
        ILogger<DashboardViewModel> logger)
        {
        _powerShellService = powerShellService;
        _profileService = profileService;
        _credentialService = credentialService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _dialogService = dialogService;
        _logger = logger;

        Profiles = _profileService.Profiles;
        }

    public async Task OnNavigatedToAsync ()
        {
        await Task.CompletedTask;
        }

    public async Task OnNavigatedFromAsync ()
        {
        await Task.CompletedTask;
        }

    /// <summary>
    /// Converts technical PowerShell results into user-friendly status messages
    /// </summary>
    private string GetSimpleConnectionStatus (string result, string serverAddress)
        {
        if (string.IsNullOrWhiteSpace(result))
            {
            return "Connection failed";
            }

        var trimmedResult = result.Trim();

        // Success case
        if (trimmedResult == "Success")
            {
            return "Connection active";
            }

        // Failed cases - provide simple, user-friendly messages
        if (trimmedResult.StartsWith("Failure:"))
            {
            var errorMessage = trimmedResult.Replace("Failure:", "").Trim();

            // Categorize common error types into simple messages
            if (errorMessage.Contains("Could not resolve") ||
                errorMessage.Contains("endpoint listening") ||
                errorMessage.Contains("network") ||
                errorMessage.Contains("host"))
                {
                return "Connection failed - Server unreachable";
                }
            else if (errorMessage.Contains("authentication") ||
                     errorMessage.Contains("login") ||
                     errorMessage.Contains("credential") ||
                     errorMessage.Contains("password"))
                {
                return "Connection failed - Authentication error";
                }
            else if (errorMessage.Contains("timeout") ||
                     errorMessage.Contains("timed out"))
                {
                return "Connection failed - Timeout";
                }
            else if (errorMessage.Contains("certificate") ||
                     errorMessage.Contains("SSL") ||
                     errorMessage.Contains("TLS"))
                {
                return "Connection failed - Certificate error";
                }
            else
                {
                return "Connection failed";
                }
            }

        // Any other case
        return "Connection failed";
        }


    [RelayCommand]
    private async Task OnConnectSource ()
        {
        if (SelectedSourceProfile is null) return;

        IsJobRunning = true;
        SourceConnectionStatus = "Connecting...";
        ScriptOutput = string.Empty;

        _logger.LogInformation("=== DASHBOARD SOURCE CONNECTION DEBUG START ===");
        _logger.LogInformation("DEBUG: [Dashboard] PowerCliConfirmedInstalled = {PowerCliConfirmed}",
            HybridPowerShellService.PowerCliConfirmedInstalled);

        string? password = _credentialService.GetPassword(SelectedSourceProfile);
        string finalPassword;

        if (string.IsNullOrEmpty(password))
            {
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {SelectedSourceProfile.Username}@{SelectedSourceProfile.ServerAddress}:"
            );

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                SourceConnectionStatus = "Connection not active";
                IsJobRunning = false;
                return;
                }

            finalPassword = promptedPassword;
            }
        else
            {
            finalPassword = password;
            }

        _logger.LogInformation("DEBUG: [Dashboard] About to call RunVCenterScriptAsync");

        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

        // Use the new credential-based method
        string result = await _powerShellService.RunVCenterScriptAsync(
            ".\\Scripts\\Test-vCenterConnection.ps1",
            SelectedSourceProfile,
            finalPassword,
            null, // no additional parameters
            logPath);

        _logger.LogInformation("DEBUG: [Dashboard] Script execution completed");
        _logger.LogInformation("=== DASHBOARD SOURCE CONNECTION DEBUG END ===");

        // Use simplified status messages
        SourceConnectionStatus = GetSimpleConnectionStatus(result, SelectedSourceProfile.ServerAddress);

        // Update shared connection service
        if (result.Contains("SUCCESS") || result.Trim() == "Success")
            {
            _sharedConnectionService.SourceConnection = SelectedSourceProfile;
            }
        else
            {
            _sharedConnectionService.SourceConnection = null;
            }

        ScriptOutput = result;
        IsJobRunning = false;
        }


    [RelayCommand]
    private async Task OnConnectTarget ()
        {
        if (SelectedTargetProfile is null) return;

        IsJobRunning = true;
        TargetConnectionStatus = "Connecting...";
        ScriptOutput = string.Empty;

        _logger.LogInformation("=== TARGET CONNECTION DEBUG START ===");
        _logger.LogInformation("DEBUG: [Target] PowerCliConfirmedInstalled = {PowerCliConfirmed}",
            HybridPowerShellService.PowerCliConfirmedInstalled);

        string? password = _credentialService.GetPassword(SelectedTargetProfile);
        string finalPassword;

        if (string.IsNullOrEmpty(password))
            {
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {SelectedTargetProfile.Username}@{SelectedTargetProfile.ServerAddress}:"
            );

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                TargetConnectionStatus = "Connection not active";
                IsJobRunning = false;
                return;
                }

            finalPassword = promptedPassword;
            }
        else
            {
            finalPassword = password;
            }

        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

        // Use the new credential-based method
        string result = await _powerShellService.RunVCenterScriptAsync(
            ".\\Scripts\\Test-vCenterConnection.ps1",
            SelectedTargetProfile,
            finalPassword,
            null, // no additional parameters
            logPath);

        _logger.LogInformation("=== TARGET CONNECTION DEBUG END ===");

        // Use simplified status messages
        TargetConnectionStatus = GetSimpleConnectionStatus(result, SelectedTargetProfile.ServerAddress);

        // Update shared connection service
        if (result.Contains("SUCCESS") || result.Trim() == "Success")
            {
            _sharedConnectionService.TargetConnection = SelectedTargetProfile;
            }
        else
            {
            _sharedConnectionService.TargetConnection = null;
            }

        ScriptOutput = result;
        IsJobRunning = false;
        }

    [RelayCommand]
    private async Task OnRunTestJob ()
        {
        if (IsJobRunning) return;

        var source = _sharedConnectionService.SourceConnection;
        if (source is null)
            {
            ScriptOutput = "Error: Please connect to a source vCenter first.";
            return;
            }

        IsJobRunning = true;
        JobProgress = 0;
        CurrentJobText = $"Running Export-vCenterConfig.ps1 on {source.ServerAddress}...";
        ScriptOutput = string.Empty;

        string? password = _credentialService.GetPassword(source);
        if (string.IsNullOrEmpty(password))
            {
            ScriptOutput = "Error: Could not retrieve password for the active connection.";
            IsJobRunning = false;
            return;
            }

        string exportPath = _configurationService.GetConfiguration().ExportPath!;

        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", source.ServerAddress },
            { "User", source.Username },
            { "Password", password! },
            { "ExportPath", exportPath }
        };

        // FIXED: Add BypassModuleCheck manually if needed
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: [TestJob] Added BypassModuleCheck=true for export script");
            }

        var result = await _powerShellService.RunScriptAsync(".\\Scripts\\Export-vCenterConfig.ps1", scriptParams);

        ScriptOutput = result;
        CurrentJobText = "Test job completed.";
        JobProgress = 100;
        IsJobRunning = false;
        }
    }