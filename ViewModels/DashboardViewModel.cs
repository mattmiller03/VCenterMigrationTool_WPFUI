using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private bool _isBusy = false;

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

        // Decrypt the password for the selected profile
        string? password = _profileService.UnprotectPassword(SelectedSourceProfile);
        if (string.IsNullOrEmpty(password))
        {
            // This part is a placeholder. In the future, we can pop up a dialog to ask for the password.
            SourceConnectionStatus = "Password not saved for this profile.";
            return;
        }

        SourceConnectionStatus = $"Connecting to {SelectedSourceProfile.ServerAddress}...";
        IsBusy = true;

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

        IsBusy = false;
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
        IsBusy = true;

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

        IsBusy = false;
    }


    [RelayCommand]
    private async Task OnExportConfiguration()
    {
        // This command's logic can be filled in later
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void OnImportConfiguration()
    {
        // This command's logic can be filled in later
    }

    public async Task OnNavigatedToAsync() => await Task.CompletedTask;

    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
}