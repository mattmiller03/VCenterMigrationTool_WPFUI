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
    private string _sourceConnectionStatus = "Not Connected";

    [ObservableProperty]
    private string _targetConnectionStatus = "Not Connected";

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

    [RelayCommand]
    private async Task OnConnectSource ()
        {
        if (SelectedSourceProfile is null) return;

        IsJobRunning = true;
        SourceConnectionStatus = $"Connecting to {SelectedSourceProfile.ServerAddress}...";
        ScriptOutput = string.Empty;

        // CRITICAL DEBUG: Check bypass status at the very beginning
        _logger.LogInformation("=== DASHBOARD CONNECTION DEBUG START ===");
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
                SourceConnectionStatus = "Connection cancelled.";
                IsJobRunning = false;
                return;
                }

            finalPassword = promptedPassword;
            }
        else
            {
            finalPassword = password;
            }

        // FIXED: Use string password directly instead of SecureString
        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedSourceProfile.ServerAddress },
            { "Username", SelectedSourceProfile.Username },
            { "Password", finalPassword }
        };

        _logger.LogInformation("DEBUG: [Dashboard] Initial parameters created");

        // CRITICAL: Add BypassModuleCheck BEFORE any other processing
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: [Dashboard] ADDED BypassModuleCheck=true to parameters");
            }
        else
            {
            _logger.LogInformation("DEBUG: [Dashboard] NOT adding BypassModuleCheck (PowerCLI not confirmed)");
            }

        // DEBUG: Check each parameter individually
        _logger.LogInformation("DEBUG: [Dashboard] Final parameter verification:");
        foreach (var param in scriptParams)
            {
            if (param.Key.ToLower().Contains("password"))
                {
                _logger.LogInformation("DEBUG: [Dashboard] Parameter {Key} = [REDACTED] (Type: {Type})",
                    param.Key, param.Value?.GetType().Name ?? "null");
                }
            else
                {
                _logger.LogInformation("DEBUG: [Dashboard] Parameter {Key} = {Value} (Type: {Type})",
                    param.Key, param.Value, param.Value?.GetType().Name ?? "null");
                }
            }

        _logger.LogInformation("DEBUG: [Dashboard] About to call RunScriptAsync");

        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams, logPath);

        _logger.LogInformation("DEBUG: [Dashboard] Script execution completed");
        _logger.LogInformation("=== DASHBOARD CONNECTION DEBUG END ===");

        if (result.Trim() == "Success")
            {
            SourceConnectionStatus = $"Connected to {SelectedSourceProfile.ServerAddress}";
            _sharedConnectionService.SourceConnection = SelectedSourceProfile;
            }
        else
            {
            SourceConnectionStatus = $"Failed: {result.Replace("Failure:", "").Trim()}";
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
        TargetConnectionStatus = $"Connecting to {SelectedTargetProfile.ServerAddress}...";
        ScriptOutput = string.Empty;

        // CRITICAL DEBUG: Check bypass status at the very beginning
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
                TargetConnectionStatus = "Connection cancelled.";
                IsJobRunning = false;
                return;
                }

            finalPassword = promptedPassword;
            }
        else
            {
            finalPassword = password;
            }

        // FIXED: Use string password directly instead of SecureString
        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedTargetProfile.ServerAddress },
            { "Username", SelectedTargetProfile.Username },
            { "Password", finalPassword }
        };

        _logger.LogInformation("DEBUG: [Target] Initial parameters created");

        // CRITICAL: Add BypassModuleCheck BEFORE any other processing
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: [Target] ADDED BypassModuleCheck=true to parameters");
            }
        else
            {
            _logger.LogInformation("DEBUG: [Target] NOT adding BypassModuleCheck (PowerCLI not confirmed)");
            }

        // DEBUG: Check each parameter individually
        _logger.LogInformation("DEBUG: [Target] Final parameter verification:");
        foreach (var param in scriptParams)
            {
            if (param.Key.ToLower().Contains("password"))
                {
                _logger.LogInformation("DEBUG: [Target] Parameter {Key} = [REDACTED] (Type: {Type})",
                    param.Key, param.Value?.GetType().Name ?? "null");
                }
            else
                {
                _logger.LogInformation("DEBUG: [Target] Parameter {Key} = {Value} (Type: {Type})",
                    param.Key, param.Value, param.Value?.GetType().Name ?? "null");
                }
            }

        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams, logPath);

        _logger.LogInformation("=== TARGET CONNECTION DEBUG END ===");

        if (result.Trim() == "Success")
            {
            TargetConnectionStatus = $"Connected to {SelectedTargetProfile.ServerAddress}";
            _sharedConnectionService.TargetConnection = SelectedTargetProfile;
            }
        else
            {
            TargetConnectionStatus = $"Failed: {result.Replace("Failure:", "").Trim()}";
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