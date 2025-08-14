using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class HostMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly PowerShellService _powerShellService;
    private readonly ConnectionProfileService _profileService;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _logOutput = "Migration log will be displayed here...";

    [ObservableProperty] private ObservableCollection<ClusterNode> _sourceTopology = new();
    [ObservableProperty] private ObservableCollection<ClusterInfo> _targetClusters = new();
    [ObservableProperty] private ClusterInfo? _selectedTargetCluster;

    public HostMigrationViewModel(PowerShellService powerShellService, ConnectionProfileService profileService)
    {
        _powerShellService = powerShellService;
        _profileService = profileService;
    }

    [RelayCommand]
    private async Task LoadData()
    {
        IsBusy = true;
        LogOutput = "Loading source topology and target clusters...";

        // In a real app, you would get the selected source/target profiles from the Dashboard
        var sourceProfile = _profileService.Profiles.FirstOrDefault();
        var targetProfile = _profileService.Profiles.LastOrDefault();

        if (sourceProfile != null)
        {
            // This script needs to return a hierarchical JSON object
            SourceTopology = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterNode>(".\\Scripts\\Get-EsxiHosts.ps1", new Dictionary<string, object>());
        }

        if (targetProfile != null)
        {
            TargetClusters = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterInfo>(".\\Scripts\\Get-Clusters.ps1", new Dictionary<string, object>());
        }

        LogOutput = $"Loaded {SourceTopology.Count} source clusters and {TargetClusters.Count} target clusters.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OnMigrateHosts()
    {
        // Find all hosts that have IsSelected = true across all clusters
        var selectedHosts = SourceTopology
            .SelectMany(cluster => cluster.Hosts)
            .Where(host => host.IsSelected)
            .ToList();

        if (!selectedHosts.Any() || SelectedTargetCluster is null)
        {
            LogOutput += "\nPlease select at least one host and a target cluster.";
            return;
        }

        IsBusy = true;
        LogOutput = "Starting host migration...\n";

        foreach (var host in selectedHosts)
        {
            LogOutput += $"Simulating migration of host '{host.Name}' to cluster '{SelectedTargetCluster.Name}'...\n";
            await Task.Delay(2000); // Simulate PowerShell script work
            LogOutput += $"Migration of '{host.Name}' complete.\n";
        }

        IsBusy = false;
        LogOutput += "\nAll selected hosts have been migrated.";
    }

    public async Task OnNavigatedToAsync()
    {
        if (!SourceTopology.Any())
        {
            await LoadData();
        }
    }
    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
}