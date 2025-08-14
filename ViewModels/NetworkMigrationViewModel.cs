using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class NetworkMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;

    // FIX: Changed the type from the non-existent 'NetworkTopology' to the correct 'NetworkHostNode'
    [ObservableProperty]
    private ObservableCollection<NetworkHostNode> _vSwitches = new();

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private ObservableCollection<string> _targetVSwitches = new();

    [ObservableProperty]
    private string? _selectedTargetVSwitch;

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private double _migrationProgress;

    [ObservableProperty]
    private string _migrationStatus = "Ready.";

    public NetworkMigrationViewModel (PowerShellService powerShellService, SharedConnectionService sharedConnectionService)
    {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
    }

    public async Task OnNavigatedToAsync () => await Task.CompletedTask;
    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task OnMigrateNetworks ()
    {
        IsMigrating = true;
        MigrationStatus = "Migrating networks...";
        await Task.Delay(2000); // Simulate work
        MigrationStatus = "Network migration complete.";
        IsMigrating = false;
    }
}