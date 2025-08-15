using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly IDialogService _dialogService;

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
        IDialogService dialogService)
        {
        _powerShellService = powerShellService;
        _profileService = profileService;
        _credentialService = credentialService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _dialogService = dialogService;

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

        string? password = _credentialService.GetPassword(SelectedSourceProfile);
        SecureString securePassword = new();

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

            foreach (char c in promptedPassword)
                {
                securePassword.AppendChar(c);
                }
            }
        else
            {
            foreach (char c in password)
                {
                securePassword.AppendChar(c);
                }
            }
        securePassword.MakeReadOnly();

        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedSourceProfile.ServerAddress },
            { "Username", SelectedSourceProfile.Username },
            { "Password", securePassword }
        };

        // FIXED: Use the optimized method that adds BypassModuleCheck automatically
        string result = await _powerShellService.RunScriptOptimizedAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

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

        string? password = _credentialService.GetPassword(SelectedTargetProfile);
        SecureString securePassword = new();

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

            foreach (char c in promptedPassword)
                {
                securePassword.AppendChar(c);
                }
            }
        else
            {
            foreach (char c in password)
                {
                securePassword.AppendChar(c);
                }
            }
        securePassword.MakeReadOnly();

        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedTargetProfile.ServerAddress },
            { "Username", SelectedTargetProfile.Username },
            { "Password", securePassword }
        };

        // FIXED: Use the optimized method that adds BypassModuleCheck automatically
        string result = await _powerShellService.RunScriptOptimizedAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

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

        // FIXED: Use the optimized method for export script as well
        var result = await _powerShellService.RunScriptOptimizedAsync(".\\Scripts\\Export-vCenterConfig.ps1", scriptParams);

        ScriptOutput = result;
        CurrentJobText = "Test job completed.";
        JobProgress = 100;
        IsJobRunning = false;
        }
    }