using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VCenterMigrationViewModel : ObservableObject, INavigationAware
{
    private readonly ILogger<VCenterMigrationViewModel> _logger;
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly IErrorHandlingService _errorHandlingService;

    // Connection Status
    [ObservableProperty] private bool _isSourceConnected;
    [ObservableProperty] private bool _isTargetConnected;
    [ObservableProperty] private string _sourceConnectionStatus = "Not connected";
    [ObservableProperty] private string _targetConnectionStatus = "Not connected";
    [ObservableProperty] private string _sourceVCenterInfo = "";
    [ObservableProperty] private string _targetVCenterInfo = "";

    // Migration Options
    [ObservableProperty] private bool _migrateRoles = true;
    [ObservableProperty] private bool _migrateFolders = true;
    [ObservableProperty] private bool _migrateTags = true;
    [ObservableProperty] private bool _migratePermissions = true;
    [ObservableProperty] private bool _migrateResourcePools = false;
    [ObservableProperty] private bool _migrateCustomAttributes = true;

    // Data Collections
    [ObservableProperty] private ObservableCollection<ClusterInfo> _sourceClusters = new();
    [ObservableProperty] private ObservableCollection<ClusterInfo> _targetClusters = new();
    [ObservableProperty] private ClusterInfo? _selectedSourceCluster;
    [ObservableProperty] private ClusterInfo? _selectedTargetCluster;
    [ObservableProperty] private bool _isLoadingClusters;
    [ObservableProperty] private bool _isLoadingClusterItems;
    [ObservableProperty] private ObservableCollection<ClusterItem> _clusterItems = new();
    
    // Migration Progress
    [ObservableProperty] private bool _isMigrating;
    [ObservableProperty] private ObservableCollection<MigrationTask> _migrationTasks = new();
    [ObservableProperty] private string _overallStatus = "Ready to start migration";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _currentTaskDetails = "Select source and target clusters to begin";
    [ObservableProperty] private DateTime? _migrationStartTime;
    [ObservableProperty] private DateTime? _migrationEndTime;

    // Statistics
    [ObservableProperty] private int _totalItemsToMigrate;
    [ObservableProperty] private int _successfulMigrations;
    [ObservableProperty] private int _failedMigrations;
    [ObservableProperty] private int _skippedMigrations;

    public VCenterMigrationViewModel(
        ILogger<VCenterMigrationViewModel> logger,
        HybridPowerShellService powerShellService, 
        SharedConnectionService sharedConnectionService,
        IErrorHandlingService errorHandlingService)
    {
        _logger = logger;
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _errorHandlingService = errorHandlingService;
    }

    public async Task OnNavigatedToAsync()
    {
        try
        {
            await LoadConnectionStatusAsync();
            await LoadClustersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during page navigation");
            OverallStatus = "Error loading page data. Please try refreshing.";
        }
    }

    public async Task OnNavigatedFromAsync() => await Task.CompletedTask;

    [RelayCommand]
    private async Task LoadClusters()
    {
        try
        {
            IsLoadingClusters = true;
            OverallStatus = "Loading clusters...";

            // Load source clusters
            var sourceClusters = await _powerShellService.GetClustersAsync();
            SourceClusters.Clear();
            foreach (var cluster in sourceClusters)
            {
                SourceClusters.Add(cluster);
            }

            // Load target clusters if target is connected
            if (IsTargetConnected)
            {
                var targetClusters = await _powerShellService.GetClustersAsync("target");
                TargetClusters.Clear();
                foreach (var cluster in targetClusters)
                {
                    TargetClusters.Add(cluster);
                }
            }

            OverallStatus = $"Loaded {SourceClusters.Count} source clusters";
            _logger.LogInformation("Loaded {SourceCount} source clusters and {TargetCount} target clusters", 
                SourceClusters.Count, TargetClusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load clusters");
            OverallStatus = $"Failed to load clusters: {ex.Message}";
            await _errorHandlingService.ShowErrorDialogAsync(
                _errorHandlingService.TranslateError(ex.Message, "Load Clusters"));
        }
        finally
        {
            IsLoadingClusters = false;
        }
    }

    [RelayCommand]
    private async Task LoadClusterItems()
    {
        if (SelectedSourceCluster == null)
        {
            CurrentTaskDetails = "Please select a source cluster first";
            return;
        }

        try
        {
            IsLoadingClusterItems = true;
            CurrentTaskDetails = $"Loading items from cluster {SelectedSourceCluster.Name}...";
            ClusterItems.Clear();

            var parameters = new Dictionary<string, object>
            {
                { "ConnectionType", "source" },  // Specify source connection for cluster items
                { "ClusterName", SelectedSourceCluster.Name },
                { "IncludeRoles", MigrateRoles },
                { "IncludeFolders", MigrateFolders },
                { "IncludeTags", MigrateTags },
                { "IncludePermissions", MigratePermissions },
                { "IncludeResourcePools", MigrateResourcePools },
                { "IncludeCustomAttributes", MigrateCustomAttributes }
            };

            var clusterItems = await _powerShellService.GetClusterItemsAsync(parameters);
            
            foreach (var item in clusterItems)
            {
                ClusterItems.Add(item);
            }

            TotalItemsToMigrate = ClusterItems.Count(i => i.IsSelected);
            CurrentTaskDetails = $"Found {ClusterItems.Count} items in cluster {SelectedSourceCluster.Name}";
            
            _logger.LogInformation("Loaded {Count} cluster items from {Cluster}", 
                ClusterItems.Count, SelectedSourceCluster.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cluster items for cluster {Cluster}", SelectedSourceCluster?.Name);
            CurrentTaskDetails = $"Failed to load cluster items: {ex.Message}";
            await _errorHandlingService.ShowErrorDialogAsync(
                _errorHandlingService.TranslateError(ex.Message, "Load Cluster Items"));
        }
        finally
        {
            IsLoadingClusterItems = false;
        }
    }

    [RelayCommand]
    private async Task RefreshConnections()
    {
        await LoadConnectionStatusAsync();
        await LoadClustersAsync();
    }

    [RelayCommand]
    private async Task ExportObjects()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Export vCenter Objects",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"vCenter-Objects-{DateTime.Now:yyyy-MM-dd-HHmm}.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                OverallStatus = "Exporting objects...";
                
                var exportData = new VCenterObjectsExport
                {
                    ExportDate = DateTime.Now,
                    SourceCluster = SelectedSourceCluster?.Name ?? "",
                    TargetCluster = SelectedTargetCluster?.Name ?? "",
                    MigrationOptions = new MigrationOptionsData
                    {
                        MigrateRoles = MigrateRoles,
                        MigrateFolders = MigrateFolders,
                        MigrateTags = MigrateTags,
                        MigratePermissions = MigratePermissions,
                        MigrateResourcePools = MigrateResourcePools,
                        MigrateCustomAttributes = MigrateCustomAttributes
                    },
                    ClusterItems = ClusterItems.ToList()
                };

                var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(saveDialog.FileName, json);
                
                OverallStatus = $"Objects exported to {Path.GetFileName(saveDialog.FileName)}";
                _logger.LogInformation("Exported {Count} objects to {File}", ClusterItems.Count, saveDialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export objects");
            OverallStatus = $"Export failed: {ex.Message}";
            await _errorHandlingService.ShowErrorDialogAsync(
                _errorHandlingService.TranslateError(ex.Message, "Export Objects"));
        }
    }

    [RelayCommand]
    private async Task ImportObjects()
    {
        try
        {
            var openDialog = new OpenFileDialog
            {
                Title = "Import vCenter Objects",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (openDialog.ShowDialog() == true)
            {
                OverallStatus = "Importing objects...";
                
                var json = await File.ReadAllTextAsync(openDialog.FileName);
                var importData = JsonSerializer.Deserialize<VCenterObjectsExport>(json);

                if (importData != null)
                {
                    // Apply migration options
                    MigrateRoles = importData.MigrationOptions.MigrateRoles;
                    MigrateFolders = importData.MigrationOptions.MigrateFolders;
                    MigrateTags = importData.MigrationOptions.MigrateTags;
                    MigratePermissions = importData.MigrationOptions.MigratePermissions;
                    MigrateResourcePools = importData.MigrationOptions.MigrateResourcePools;
                    MigrateCustomAttributes = importData.MigrationOptions.MigrateCustomAttributes;

                    // Import cluster items
                    ClusterItems.Clear();
                    foreach (var item in importData.ClusterItems)
                    {
                        ClusterItems.Add(item);
                    }

                    TotalItemsToMigrate = ClusterItems.Count(i => i.IsSelected);
                    
                    OverallStatus = $"Imported {ClusterItems.Count} objects from {Path.GetFileName(openDialog.FileName)}";
                    CurrentTaskDetails = $"Imported from export created on {importData.ExportDate:yyyy-MM-dd HH:mm}";
                    
                    _logger.LogInformation("Imported {Count} objects from {File}", ClusterItems.Count, openDialog.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import objects");
            OverallStatus = $"Import failed: {ex.Message}";
            await _errorHandlingService.ShowErrorDialogAsync(
                _errorHandlingService.TranslateError(ex.Message, "Import Objects"));
        }
    }

    [RelayCommand]
    private async Task StartMigration()
    {
        if (SelectedSourceCluster == null || SelectedTargetCluster == null)
        {
            OverallStatus = "Please select both source and target clusters";
            return;
        }

        var selectedItems = ClusterItems.Where(i => i.IsSelected).ToList();
        if (!selectedItems.Any())
        {
            OverallStatus = "Please select items to migrate";
            return;
        }

        try
        {
            IsMigrating = true;
            MigrationStartTime = DateTime.Now;
            MigrationTasks.Clear();
            SuccessfulMigrations = 0;
            FailedMigrations = 0;
            SkippedMigrations = 0;
            
            OverallStatus = "Starting vCenter objects migration...";
            CurrentTaskDetails = $"Migrating {selectedItems.Count} items from {SelectedSourceCluster.Name} to {SelectedTargetCluster.Name}";

            var migrationParams = new Dictionary<string, object>
            {
                { "SourceCluster", SelectedSourceCluster.Name },
                { "TargetCluster", SelectedTargetCluster.Name },
                { "Items", selectedItems.Select(i => new { i.Name, i.Type, i.Id }).ToList() },
                { "MigrateRoles", MigrateRoles },
                { "MigrateFolders", MigrateFolders },
                { "MigrateTags", MigrateTags },
                { "MigratePermissions", MigratePermissions },
                { "MigrateResourcePools", MigrateResourcePools },
                { "MigrateCustomAttributes", MigrateCustomAttributes }
            };

            await PerformMigrationAsync(migrationParams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vCenter objects migration failed");
            OverallStatus = $"Migration failed: {ex.Message}";
            await _errorHandlingService.ShowErrorDialogAsync(
                _errorHandlingService.TranslateError(ex.Message, "vCenter Objects Migration"));
        }
        finally
        {
            IsMigrating = false;
            MigrationEndTime = DateTime.Now;
        }
    }

    private async Task LoadConnectionStatusAsync()
    {
        try
        {
            // Check source connection
            var sourceStatus = await _sharedConnectionService.GetConnectionStatusAsync("source");
            IsSourceConnected = sourceStatus.IsConnected;
            SourceConnectionStatus = sourceStatus.IsConnected ? "Connected" : "Disconnected";
            SourceVCenterInfo = sourceStatus.IsConnected ? 
                $"{sourceStatus.ServerName} (v{sourceStatus.Version})" : "Not connected";

            // Check target connection
            var targetStatus = await _sharedConnectionService.GetConnectionStatusAsync("target");
            IsTargetConnected = targetStatus.IsConnected;
            TargetConnectionStatus = targetStatus.IsConnected ? "Connected" : "Disconnected";
            TargetVCenterInfo = targetStatus.IsConnected ? 
                $"{targetStatus.ServerName} (v{targetStatus.Version})" : "Not connected";

            _logger.LogInformation("Connection status - Source: {Source}, Target: {Target}", 
                SourceConnectionStatus, TargetConnectionStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load connection status");
            SourceConnectionStatus = "Error checking connection";
            TargetConnectionStatus = "Error checking connection";
        }
    }

    private async Task LoadClustersAsync()
    {
        if (IsSourceConnected)
        {
            await LoadClusters();
        }
        else
        {
            OverallStatus = "Please establish vCenter connections first";
            CurrentTaskDetails = "Go to Dashboard to connect to source and target vCenter servers";
        }
    }

    private async Task PerformMigrationAsync(Dictionary<string, object> parameters)
    {
        var totalSteps = ClusterItems.Count(i => i.IsSelected);
        var currentStep = 0;

        foreach (var item in ClusterItems.Where(i => i.IsSelected))
        {
            currentStep++;
            OverallProgress = (double)currentStep / totalSteps * 100;
            CurrentTaskDetails = $"Migrating {item.Type}: {item.Name} ({currentStep}/{totalSteps})";

            var taskStartTime = DateTime.Now;
            var migrationTask = new MigrationTask
            {
                Description = $"Migrate {item.Type}: {item.Name}",
                StartTime = taskStartTime,
                Status = "In Progress"
            };
            MigrationTasks.Add(migrationTask);

            try
            {
                // Get source and target connection information
                var sourceConnection = await _sharedConnectionService.GetConnectionAsync("source");
                var targetConnection = await _sharedConnectionService.GetConnectionAsync("target");
                
                if (sourceConnection == null || targetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                // Perform the actual migration for this item
                var itemParams = new Dictionary<string, object>
                {
                    { "SourceVCenter", sourceConnection.ServerAddress },
                    { "SourceUsername", sourceConnection.Username },
                    { "SourcePassword", await _sharedConnectionService.GetPasswordAsync("source") ?? "" },
                    { "TargetVCenter", targetConnection.ServerAddress },
                    { "TargetUsername", targetConnection.Username },
                    { "TargetPassword", await _sharedConnectionService.GetPasswordAsync("target") ?? "" },
                    { "ObjectType", item.Type },
                    { "ObjectName", item.Name },
                    { "ObjectId", item.Id },
                    { "ObjectPath", item.Path }
                };

                var result = await _powerShellService.MigrateVCenterObjectAsync(itemParams);
                
                migrationTask.EndTime = DateTime.Now;
                migrationTask.ElapsedTime = (migrationTask.EndTime.Value - migrationTask.StartTime).ToString(@"mm\:ss");
                
                // Parse the JSON result to determine success/failure
                bool migrationSuccessful = false;
                string errorMessage = "";
                
                try
                {
                    var resultJson = JsonSerializer.Deserialize<Dictionary<string, object>>(result);
                    migrationSuccessful = bool.Parse(resultJson.GetValueOrDefault("Success", false).ToString());
                    if (!migrationSuccessful)
                    {
                        errorMessage = resultJson.GetValueOrDefault("Error", "Unknown error").ToString();
                    }
                }
                catch (JsonException)
                {
                    // Fallback to string checking if JSON parsing fails
                    migrationSuccessful = result.Contains("\"Success\":true") || result.Contains("SUCCESS");
                }
                
                if (migrationSuccessful)
                {
                    migrationTask.Status = "Completed";
                    SuccessfulMigrations++;
                    item.Status = "Migrated";
                }
                else
                {
                    migrationTask.Status = $"Failed{(string.IsNullOrEmpty(errorMessage) ? "" : ": " + errorMessage)}";
                    FailedMigrations++;
                    item.Status = "Failed";
                }

                _logger.LogInformation("Migration completed for {Type}: {Name} - {Status}", 
                    item.Type, item.Name, migrationTask.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed for {Type}: {Name}", item.Type, item.Name);
                migrationTask.Status = "Failed";
                migrationTask.EndTime = DateTime.Now;
                migrationTask.ElapsedTime = (migrationTask.EndTime.Value - migrationTask.StartTime).ToString(@"mm\:ss");
                FailedMigrations++;
                item.Status = "Failed";
            }

            // Brief delay between items
            await Task.Delay(500);
        }

        var totalTime = MigrationEndTime.HasValue && MigrationStartTime.HasValue ?
            (MigrationEndTime.Value - MigrationStartTime.Value).ToString(@"mm\:ss") : "N/A";

        OverallStatus = $"Migration completed - {SuccessfulMigrations} successful, {FailedMigrations} failed (Total time: {totalTime})";
        CurrentTaskDetails = "Migration process finished. Check the task list for details.";
    }

    partial void OnSelectedSourceClusterChanged(ClusterInfo? value)
    {
        if (value != null && !IsLoadingClusterItems)
        {
            _ = Task.Run(LoadClusterItems);
        }
    }

    partial void OnMigrateRolesChanged(bool value) => RefreshClusterItemsIfNeeded();
    partial void OnMigrateFoldersChanged(bool value) => RefreshClusterItemsIfNeeded();
    partial void OnMigrateTagsChanged(bool value) => RefreshClusterItemsIfNeeded();
    partial void OnMigratePermissionsChanged(bool value) => RefreshClusterItemsIfNeeded();
    partial void OnMigrateResourcePoolsChanged(bool value) => RefreshClusterItemsIfNeeded();
    partial void OnMigrateCustomAttributesChanged(bool value) => RefreshClusterItemsIfNeeded();

    private void RefreshClusterItemsIfNeeded()
    {
        if (SelectedSourceCluster != null && !IsLoadingClusterItems && !IsMigrating)
        {
            _ = Task.Run(LoadClusterItems);
        }
    }

    public string MigrationSummary
    {
        get
        {
            if (MigrationStartTime.HasValue && MigrationEndTime.HasValue)
            {
                var duration = MigrationEndTime.Value - MigrationStartTime.Value;
                return $"Migration completed in {duration:mm\\:ss} - {SuccessfulMigrations} successful, {FailedMigrations} failed, {SkippedMigrations} skipped";
            }
            return "No migration performed yet";
        }
    }

    public bool CanStartMigration => IsSourceConnected && IsTargetConnected && 
                                   SelectedSourceCluster != null && SelectedTargetCluster != null && 
                                   ClusterItems.Any(i => i.IsSelected) && !IsMigrating;
}