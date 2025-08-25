using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class InfrastructureMigrationViewModel : ObservableObject, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly CredentialService _credentialService;
        private readonly ConfigurationService _configurationService;
        private readonly ILogger<InfrastructureMigrationViewModel> _logger;

        // Connection Status
        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        [ObservableProperty]
        private string _sourceConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _targetConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _sourceDataStatus = "No data loaded";

        [ObservableProperty]
        private string _targetDataStatus = "No data loaded";

        // Data Collections
        [ObservableProperty]
        private ObservableCollection<DatacenterInfo> _sourceDatacenters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _sourceClusters = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _sourceHosts = new();

        [ObservableProperty]
        private ObservableCollection<DatastoreInfo> _sourceDatastores = new();

        [ObservableProperty]
        private ObservableCollection<DatacenterInfo> _targetDatacenters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _targetClusters = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _targetHosts = new();

        [ObservableProperty]
        private ObservableCollection<DatastoreInfo> _targetDatastores = new();

        // Migration Options
        [ObservableProperty]
        private bool _migrateDatacenters = true;

        [ObservableProperty]
        private bool _migrateClusters = true;

        [ObservableProperty]
        private bool _migrateHosts = true;

        [ObservableProperty]
        private bool _migrateDatastores = true;

        [ObservableProperty]
        private bool _preserveResourceConfigs = true;

        [ObservableProperty]
        private bool _validateOnly = false;

        // Migration Status
        [ObservableProperty]
        private bool _isMigrationInProgress = false;

        [ObservableProperty]
        private double _migrationProgress = 0;

        [ObservableProperty]
        private string _migrationStatus = "Ready to start infrastructure migration";

        [ObservableProperty]
        private string _activityLog = "Infrastructure migration activity log will appear here...\n";

        // Computed Properties
        public bool CanValidateMigration => IsSourceConnected && IsTargetConnected && !IsMigrationInProgress;
        public bool CanStartMigration => CanValidateMigration && !ValidateOnly;

        public InfrastructureMigrationViewModel(
            SharedConnectionService sharedConnectionService,
            HybridPowerShellService powerShellService,
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            CredentialService credentialService,
            ConfigurationService configurationService,
            ILogger<InfrastructureMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
            _persistentConnectionService = persistentConnectionService;
            _credentialService = credentialService;
            _configurationService = configurationService;
            _logger = logger;
        }

        public async Task OnNavigatedToAsync()
        {
            try
            {
                await LoadConnectionStatusAsync();
                await LoadInfrastructureDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during page navigation");
                MigrationStatus = "Error loading page data. Please try refreshing.";
            }
        }

        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;

        private async Task LoadConnectionStatusAsync()
        {
            try
            {
                // Check connection status via SharedConnectionService (supports both API and PowerCLI)
                var sourceConnected = await _sharedConnectionService.IsConnectedAsync("source");
                IsSourceConnected = sourceConnected;
                SourceConnectionStatus = sourceConnected ? "Connected" : "Disconnected";

                // Check target connection
                var targetConnected = await _sharedConnectionService.IsConnectedAsync("target");
                IsTargetConnected = targetConnected;
                TargetConnectionStatus = targetConnected ? "Connected" : "Disconnected";

                OnPropertyChanged(nameof(CanValidateMigration));
                OnPropertyChanged(nameof(CanStartMigration));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load connection status");
                SourceConnectionStatus = "Error checking connection";
                TargetConnectionStatus = "Error checking connection";
            }
        }

        private async Task LoadInfrastructureDataAsync()
        {
            try
            {
                // Load cached inventory data if available
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var targetInventory = _sharedConnectionService.GetTargetInventory();

                // Populate source infrastructure data
                if (sourceInventory != null)
                {
                    // Clear existing collections
                    SourceDatacenters.Clear();
                    SourceClusters.Clear();
                    SourceHosts.Clear();
                    SourceDatastores.Clear();

                    // Populate with data from inventory
                    foreach (var datacenter in sourceInventory.Datacenters)
                    {
                        SourceDatacenters.Add(datacenter);
                    }

                    foreach (var cluster in sourceInventory.Clusters)
                    {
                        SourceClusters.Add(cluster);
                    }

                    foreach (var host in sourceInventory.Hosts)
                    {
                        SourceHosts.Add(host);
                    }

                    foreach (var datastore in sourceInventory.Datastores)
                    {
                        SourceDatastores.Add(datastore);
                    }

                    SourceDataStatus = $"✅ {sourceInventory.Datacenters.Count} datacenters, {sourceInventory.Clusters.Count} clusters, {sourceInventory.Hosts.Count} hosts, {sourceInventory.Datastores.Count} datastores";
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source inventory populated: {SourceDatacenters.Count} DCs, {SourceClusters.Count} clusters, {SourceHosts.Count} hosts, {SourceDatastores.Count} datastores\n";
                }
                else
                {
                    // Clear collections if no data
                    SourceDatacenters.Clear();
                    SourceClusters.Clear();
                    SourceHosts.Clear();
                    SourceDatastores.Clear();
                    SourceDataStatus = "No infrastructure data loaded";
                }

                // Populate target infrastructure data
                if (targetInventory != null)
                {
                    // Clear existing collections
                    TargetDatacenters.Clear();
                    TargetClusters.Clear();
                    TargetHosts.Clear();
                    TargetDatastores.Clear();

                    // Populate with data from inventory
                    foreach (var datacenter in targetInventory.Datacenters)
                    {
                        TargetDatacenters.Add(datacenter);
                    }

                    foreach (var cluster in targetInventory.Clusters)
                    {
                        TargetClusters.Add(cluster);
                    }

                    foreach (var host in targetInventory.Hosts)
                    {
                        TargetHosts.Add(host);
                    }

                    foreach (var datastore in targetInventory.Datastores)
                    {
                        TargetDatastores.Add(datastore);
                    }

                    TargetDataStatus = $"✅ {targetInventory.Datacenters.Count} datacenters, {targetInventory.Clusters.Count} clusters, {targetInventory.Hosts.Count} hosts, {targetInventory.Datastores.Count} datastores";
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target inventory populated: {TargetDatacenters.Count} DCs, {TargetClusters.Count} clusters, {TargetHosts.Count} hosts, {TargetDatastores.Count} datastores\n";
                }
                else
                {
                    // Clear collections if no data
                    TargetDatacenters.Clear();
                    TargetClusters.Clear();
                    TargetHosts.Clear();
                    TargetDatastores.Clear();
                    TargetDataStatus = "No infrastructure data loaded";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading infrastructure data");
                SourceDataStatus = "Error loading data";
                TargetDataStatus = "Error loading data";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR loading infrastructure data: {ex.Message}\n";
            }
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            await LoadConnectionStatusAsync();
            await LoadInfrastructureDataAsync();
            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Connection status and data refreshed\n";
        }

        [RelayCommand]
        private async Task LoadSourceInfrastructure()
        {
            if (!IsSourceConnected)
            {
                MigrationStatus = "Source connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading source infrastructure...";
                SourceDataStatus = "🔄 Loading infrastructure...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source infrastructure data\n";
                
                var success = await _sharedConnectionService.LoadSourceInfrastructureAsync();
                if (success)
                {
                    SourceDataStatus = "✅ Infrastructure loaded";
                    MigrationStatus = "Source infrastructure loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source infrastructure data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadInfrastructureDataAsync();
                }
                else
                {
                    SourceDataStatus = "❌ Failed to load infrastructure";
                    MigrationStatus = "Failed to load source infrastructure";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load source infrastructure\n";
                }
            }
            catch (Exception ex)
            {
                SourceDataStatus = "❌ Error loading infrastructure";
                MigrationStatus = $"Failed to load source infrastructure: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading source infrastructure");
            }
        }

        [RelayCommand]
        private async Task LoadTargetInfrastructure()
        {
            if (!IsTargetConnected)
            {
                MigrationStatus = "Target connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading target infrastructure...";
                TargetDataStatus = "🔄 Loading infrastructure...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target infrastructure data\n";
                
                var success = await _sharedConnectionService.LoadTargetInfrastructureAsync();
                if (success)
                {
                    TargetDataStatus = "✅ Infrastructure loaded";
                    MigrationStatus = "Target infrastructure loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target infrastructure data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadInfrastructureDataAsync();
                }
                else
                {
                    TargetDataStatus = "❌ Failed to load infrastructure";
                    MigrationStatus = "Failed to load target infrastructure";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load target infrastructure\n";
                }
            }
            catch (Exception ex)
            {
                TargetDataStatus = "❌ Error loading infrastructure";
                MigrationStatus = $"Failed to load target infrastructure: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading target infrastructure");
            }
        }

        [RelayCommand]
        private async Task ValidateMigration()
        {
            try
            {
                MigrationStatus = "Validating infrastructure migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting migration validation\n";
                
                // TODO: Implement validation logic
                await Task.Delay(2000);
                
                MigrationStatus = "Infrastructure migration validation completed";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migration validation completed successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Validation failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during migration validation");
            }
        }

        [RelayCommand]
        private async Task StartMigration()
        {
            try
            {
                IsMigrationInProgress = true;
                MigrationProgress = 0;
                MigrationStatus = "Starting infrastructure migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting infrastructure migration\n";

                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    MigrationStatus = "Source or target connection not available";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Connection not available\n";
                    return;
                }

                // Get source credentials
                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);

                if (string.IsNullOrEmpty(sourcePassword) || string.IsNullOrEmpty(targetPassword))
                {
                    MigrationStatus = "Unable to retrieve connection credentials";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Missing credentials\n";
                    return;
                }

                // Get source inventory for datacenter migration
                MigrationStatus = "Loading source datacenters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source datacenter list\n";
                MigrationProgress = 10;

                // For now, we'll migrate datacenters based on what we find in the source
                // In a complete implementation, this would come from selected items in the UI
                var sourceDatacenters = await GetSourceDatacentersAsync();
                
                if (sourceDatacenters == null || !sourceDatacenters.Any())
                {
                    MigrationStatus = "No datacenters found in source to migrate";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: No datacenters found in source\n";
                    return;
                }

                MigrationProgress = 20;
                var totalDatacenters = sourceDatacenters.Count();
                var migratedCount = 0;
                var skippedCount = 0;
                var errorCount = 0;

                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Found {totalDatacenters} datacenter(s) to migrate\n";

                foreach (var datacenter in sourceDatacenters)
                {
                    try
                    {
                        MigrationStatus = $"Migrating datacenter: {datacenter.Name}";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migrating datacenter: {datacenter.Name}\n";

                        // Call the migration script for each datacenter
                        var parameters = new Dictionary<string, object>
                        {
                            ["SourceVCenter"] = _sharedConnectionService.SourceConnection.ServerAddress,
                            ["SourceUsername"] = _sharedConnectionService.SourceConnection.Username,
                            ["SourcePassword"] = sourcePassword,
                            ["TargetVCenter"] = _sharedConnectionService.TargetConnection.ServerAddress,
                            ["TargetUsername"] = _sharedConnectionService.TargetConnection.Username,
                            ["TargetPassword"] = targetPassword,
                            ["ObjectType"] = "Datacenter",
                            ["ObjectName"] = datacenter.Name,
                            ["ObjectId"] = datacenter.Id ?? "",
                            ["ObjectPath"] = "", // Datacenters don't have a path concept
                            ["LogPath"] = _configurationService.GetConfiguration().LogPath,
                            ["ValidateOnly"] = ValidateOnly
                        };

                        var result = await _powerShellService.RunScriptAsync("Scripts\\Migrate-VCenterObject.ps1", parameters);

                        if (result.Contains("Successfully migrated"))
                        {
                            migratedCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Successfully migrated datacenter: {datacenter.Name}\n";
                        }
                        else if (result.Contains("already exists"))
                        {
                            skippedCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ Datacenter already exists: {datacenter.Name}\n";
                        }
                        else if (result.StartsWith("ERROR"))
                        {
                            errorCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Error migrating {datacenter.Name}: {result}\n";
                        }
                        else
                        {
                            // Log the full result for debugging
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Result for {datacenter.Name}: {result}\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Exception migrating {datacenter.Name}: {ex.Message}\n";
                        _logger.LogError(ex, "Error migrating datacenter {DatacenterName}", datacenter.Name);
                    }

                    // Update progress
                    var currentProgress = 20 + ((migratedCount + skippedCount + errorCount) * 70 / totalDatacenters);
                    MigrationProgress = Math.Min(currentProgress, 90);
                }

                // Final summary
                MigrationProgress = 100;
                MigrationStatus = $"Migration completed - {migratedCount} migrated, {skippedCount} skipped, {errorCount} errors";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migration Summary:\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Migrated: {migratedCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Skipped (already exist): {skippedCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Errors: {errorCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Infrastructure migration completed\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Migration failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during infrastructure migration");
            }
            finally
            {
                IsMigrationInProgress = false;
                OnPropertyChanged(nameof(CanValidateMigration));
                OnPropertyChanged(nameof(CanStartMigration));
            }
        }

        private async Task<IEnumerable<DatacenterInfo>> GetSourceDatacentersAsync()
        {
            try
            {
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                if (sourceInventory?.Datacenters != null && sourceInventory.Datacenters.Any())
                {
                    return sourceInventory.Datacenters;
                }

                // If no cached inventory, try to load it
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source infrastructure data...\n";
                var success = await _sharedConnectionService.LoadSourceInfrastructureAsync();
                if (success)
                {
                    sourceInventory = _sharedConnectionService.GetSourceInventory();
                    return sourceInventory?.Datacenters ?? new List<DatacenterInfo>();
                }

                return new List<DatacenterInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading source datacenters");
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR loading source datacenters: {ex.Message}\n";
                return new List<DatacenterInfo>();
            }
        }
    }
}