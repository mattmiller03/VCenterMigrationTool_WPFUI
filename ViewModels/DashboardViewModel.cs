using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class DashboardViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;
    private readonly ConnectionProfileService _profileService;

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

    public DashboardViewModel(PowerShellService powerShellService, ConnectionProfileService profileService)
    {
        _powerShellService = powerShellService;
        _profileService = profileService;
        Profiles = _profileService.Profiles;
    }

    [RelayCommand]
    private async Task OnConnectSource()
    {
        if (SelectedSourceProfile is null)
        {
            SourceConnectionStatus = "Please select a profile.";
            return;
        }

        string? password = _profileService.UnprotectPassword(SelectedSourceProfile);
        if (string.IsNullOrEmpty(password))
        {
            SourceConnectionStatus = "Password not saved for this profile.";
            return;
        }

        SourceConnectionStatus = $"Connecting to {SelectedSourceProfile.ServerAddress}...";
        IsJobRunning = true;

        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedSourceProfile.ServerAddress },
            { "Username", SelectedSourceProfile.Username },
            { "Password", password }
        };

        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

        if (result.Trim() == "Success")
        {
            SourceConnectionStatus = $"Connected to {SelectedSourceProfile.ServerAddress}";
        }
        else
        {
            SourceConnectionStatus = $"Failed to connect: {result.Replace("Failure:", "").Trim()}";
        }

        IsJobRunning = false;
    }

    [RelayCommand]
    private async Task OnConnectTarget()
    {
        if (SelectedTargetProfile is null)
        {
            TargetConnectionStatus = "Please select a profile.";
            return;
        }

        string? password = _profileService.UnprotectPassword(SelectedTargetProfile);
        if (string.IsNullOrEmpty(password))
        {
            TargetConnectionStatus = "Password not saved for this profile.";
            return;
        }

        TargetConnectionStatus = $"Connecting to {SelectedTargetProfile.ServerAddress}...";
        IsJobRunning = true;

        var scriptParams = new Dictionary<string, object>
        {
            { "VCenterServer", SelectedTargetProfile.ServerAddress },
            { "Username", SelectedTargetProfile.Username },
            { "Password", password }
        };

        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

        if (result.Trim() == "Success")
        {
            TargetConnectionStatus = $"Connected to {SelectedTargetProfile.ServerAddress}";
        }
        else
        {
            TargetConnectionStatus = $"Failed to connect: {result.Replace("Failure:", "").Trim()}";
        }

        IsJobRunning = false;
    }

    [RelayCommand]
    private async Task OnRunTestJob()
    {
        if (IsJobRunning)
            return;

        IsJobRunning = true;
        JobProgress = 0;
        ScriptOutput = string.Empty;

        for (int i = 0; i <= 100; i++)
        {
            CurrentJobText = $"Exporting configuration... Step {i}/100";
            JobProgress = i;
            ScriptOutput += $"Performing task {i}...\n";
            await Task.Delay(50); // Simulate work
        }

        CurrentJobText = "Test job completed.";
        IsJobRunning = false;
    }

    public async Task OnNavigatedToAsync() => await Task.CompletedTask;
    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
}