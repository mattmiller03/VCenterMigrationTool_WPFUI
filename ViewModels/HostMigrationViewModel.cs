using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class HostMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;

    [ObservableProperty]
    private ObservableCollection<ClusterNode> _sourceTopology = new();

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _logOutput = "Awaiting migration task...";

    public HostMigrationViewModel (HybridPowerShellService powerShellService, SharedConnectionService sharedConnectionService)
    {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
    }

    public async Task OnNavigatedToAsync () => await Task.CompletedTask;
    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task OnMigrateHosts ()
    {
        IsBusy = true;
        LogOutput = "Starting host migration...";
        // Migration logic here
        await Task.Delay(2000);
        LogOutput = "Host migration completed.";
        IsBusy = false;
    }
}