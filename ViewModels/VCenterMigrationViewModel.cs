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

    // --- Standard Migration Options ---
    [ObservableProperty] private bool _migrateRolesAndPermissions = true;
    [ObservableProperty] private bool _migrateVmFolders = true;
    [ObservableProperty] private bool _migrateTagsAndCategories = true;

    // --- Cluster-Specific Options ---
    [ObservableProperty] private ObservableCollection<ClusterInfo> _clusters = new();
    [ObservableProperty] private ClusterInfo? _selectedCluster;
    [ObservableProperty] private ObservableCollection<ClusterItem> _clusterItems = new();

    // --- Progress Tracking Properties ---
    [ObservableProperty] private ObservableCollection<MigrationTask> _migrationTasks = new();
    [ObservableProperty] private int _overallProgress;
    [ObservableProperty] private string _currentTaskText = "Ready to start migration.";

    public VCenterMigrationViewModel(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    async partial void OnSelectedClusterChanged(ClusterInfo? value)
    {
        await LoadClusterItemsAsync();
    }

    [RelayCommand]
    private async Task LoadClusters()
    {
        IsBusy = true;
        CurrentTaskText = "Loading clusters from source vCenter...";
        var scriptParams = new Dictionary<string, object>(); // Placeholder for connection params
        Clusters = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterInfo>(".\\Scripts\\Get-Clusters.ps1", scriptParams);
        CurrentTaskText = $"Found {Clusters.Count} clusters.";
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
        CurrentTaskText = $"Loading resources for cluster '{SelectedCluster.Name}'...";
        var scriptParams = new Dictionary<string, object>
        {
            { "ClusterName", SelectedCluster.Name } // Placeholder
        };
        ClusterItems = await _powerShellService.RunScriptAndGetObjectsAsync<ClusterItem>(".\\Scripts\\Get-ClusterItems.ps1", scriptParams);
        CurrentTaskText = $"Found {ClusterItems.Count} resources in '{SelectedCluster.Name}'.";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OnStartMigration()
    {
        IsBusy = true;
        MigrationTasks.Clear();
        OverallProgress = 0;

        // Create a list of tasks based on user selection
        if (MigrateRolesAndPermissions) MigrationTasks.Add(new MigrationTask { ObjectName = "Roles & Permissions" });
        if (MigrateVmFolders) MigrationTasks.Add(new MigrationTask { ObjectName = "VM Folders" });
        if (MigrateTagsAndCategories) MigrationTasks.Add(new MigrationTask { ObjectName = "Tags & Categories" });

        var selectedClusterItems = ClusterItems.Where(i => i.IsSelected).ToList();
        foreach (var item in selectedClusterItems)
        {
            MigrationTasks.Add(new MigrationTask { ObjectName = $"{item.Name} ({item.Type})" });
        }

        if (!MigrationTasks.Any())
        {
            CurrentTaskText = "No items selected for migration.";
            IsBusy = false;
            return;
        }

        // --- Simulate the migration process ---
        for (int i = 0; i < MigrationTasks.Count; i++)
        {
            var task = MigrationTasks[i];
            CurrentTaskText = $"Migrating {task.ObjectName}...";
            task.Status = "In Progress";

            for (int p = 0; p <= 100; p += 10)
            {
                task.Progress = p;
                await Task.Delay(50); // Simulate work on a sub-task
            }

            task.Status = "Completed";
            task.Details = "Success";
            OverallProgress = (int)(((i + 1.0) / MigrationTasks.Count) * 100);
        }

        CurrentTaskText = "All migration tasks completed.";
        IsBusy = false;
    }

    public async Task OnNavigatedToAsync()
    {
        if (!Clusters.Any())
        {
            await LoadClusters();
        }
    }
    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}