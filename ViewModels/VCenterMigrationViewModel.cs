using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VCenterMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;

    [ObservableProperty] private bool _migrateRoles;
    [ObservableProperty] private bool _migrateFolders;
    [ObservableProperty] private bool _migrateTags;
    [ObservableProperty] private ObservableCollection<ClusterInfo> _sourceClusters = new();
    [ObservableProperty] private ClusterInfo? _selectedSourceCluster;
    [ObservableProperty] private bool _isLoadingClusterItems;
    [ObservableProperty] private ObservableCollection<ClusterItem> _clusterItems = new();
    [ObservableProperty] private bool _isMigrating;
    [ObservableProperty] private ObservableCollection<MigrationTask> _migrationTasks = new();
    [ObservableProperty] private string _overallStatus = "Ready.";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _currentTaskDetails = "No active tasks.";

    public VCenterMigrationViewModel (PowerShellService powerShellService, SharedConnectionService sharedConnectionService)
    {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
    }

    public async Task OnNavigatedToAsync () => await Task.CompletedTask;
    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task OnStartMigration ()
    {
        IsMigrating = true;
        OverallStatus = "Migration in progress...";
        // Migration logic will go here
        await Task.Delay(2000);
        OverallStatus = "Migration complete.";
        IsMigrating = false;
    }
}