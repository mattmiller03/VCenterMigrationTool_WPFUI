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

public partial class VCenterMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _logOutput = "Migration log will be displayed here...";

    // --- Standard Migration Options ---
    [ObservableProperty] private bool _migrateRolesAndPermissions = true;
    [ObservableProperty] private bool _migrateVmFolders = true;
    [ObservableProperty] private bool _migrateTagsAndCategories = true;

    // --- New Cluster-Specific Options ---
    [ObservableProperty] private ObservableCollection<ClusterInfo> _clusters = new();
    [ObservableProperty] private ClusterInfo? _selectedCluster;
    [ObservableProperty] private ObservableCollection<ClusterItem> _clusterItems = new();

    public VCenterMigrationViewModel(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    // This method is called when the user selects a different cluster from the dropdown
    partial void OnSelectedClusterChanged(ClusterInfo? value)
    {
        LoadClusterItemsAsync();
    }

    [RelayCommand]
    private async Task LoadClusters()
    {
        // This command will be triggered when the page loads
        IsBusy = true;
        LogOutput = "Loading clusters from source vCenter...";
        // In a real scenario, you'd get connection info from a shared service
        var scriptParams = new Dictionary<string, object> { /* ... */ };
        Clusters = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterInfo>(".\\Scripts\\Get-Clusters.ps1", scriptParams);
        LogOutput = $"Found {Clusters.Count} clusters.";
        IsBusy = false;
    }

    private async Task LoadClusterItemsAsync()
    {
        if (SelectedCluster is null)
        {
            ClusterItems.Clear();
            return;
        }

        IsBusy = true;
        LogOutput = $"Loading resources for cluster '{SelectedCluster.Name}'...";
        var scriptParams = new Dictionary<string, object>
        {
            { "ClusterName", SelectedCluster.Name }
            /* ... other connection params ... */
        };
        ClusterItems = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterItem>(".\\Scripts\\Get-ClusterItems.ps1", scriptParams);
        LogOutput = $"Found {ClusterItems.Count} resources in '{SelectedCluster.Name}'.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OnStartMigration()
    {
        // ... (This logic will now need to be updated to use the selected cluster items)
    }

    public async Task OnNavigatedToAsync()
    {
        // When we navigate to this page, automatically load the clusters
        if (!Clusters.Any())
        {
            await LoadClusters();
        }
    }

    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
}