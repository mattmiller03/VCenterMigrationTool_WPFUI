using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        SourceConnectionStatus = $"Connecting to {SelectedSourceProfile.ServerAddress}...";
        await Task.Delay(1500); // Simulate connection
        SourceConnectionStatus = $"Connected to {SelectedSourceProfile.ServerAddress}";
    }

    [RelayCommand]
    private async Task OnConnectTarget()
    {
        if (SelectedTargetProfile is null)
        {
            TargetConnectionStatus = "Please select a profile.";
            return;
        }
        TargetConnectionStatus = $"Connecting to {SelectedTargetProfile.ServerAddress}...";
        await Task.Delay(1500); // Simulate connection
        TargetConnectionStatus = $"Connected to {SelectedTargetProfile.ServerAddress}";
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