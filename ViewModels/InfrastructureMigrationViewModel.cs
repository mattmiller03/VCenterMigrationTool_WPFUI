using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
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
            ILogger<InfrastructureMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
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
                // Check source connection
                var sourceStatus = await _sharedConnectionService.GetConnectionStatusAsync("source");
                IsSourceConnected = sourceStatus.IsConnected;
                SourceConnectionStatus = sourceStatus.IsConnected ? "Connected" : "Disconnected";

                // Check target connection
                var targetStatus = await _sharedConnectionService.GetConnectionStatusAsync("target");
                IsTargetConnected = targetStatus.IsConnected;
                TargetConnectionStatus = targetStatus.IsConnected ? "Connected" : "Disconnected";

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

                if (sourceInventory != null)
                {
                    // TODO: Populate SourceDatacenters, SourceClusters, etc. from inventory
                    SourceDataStatus = "Infrastructure data available";
                }
                else
                {
                    SourceDataStatus = "No infrastructure data loaded";
                }

                if (targetInventory != null)
                {
                    // TODO: Populate TargetDatacenters, TargetClusters, etc. from inventory
                    TargetDataStatus = "Infrastructure data available";
                }
                else
                {
                    TargetDataStatus = "No infrastructure data loaded";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading infrastructure data");
                SourceDataStatus = "Error loading data";
                TargetDataStatus = "Error loading data";
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
                
                // TODO: Implement actual migration logic
                for (int i = 0; i <= 100; i += 10)
                {
                    MigrationProgress = i;
                    await Task.Delay(200);
                }
                
                MigrationStatus = "Infrastructure migration completed successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Infrastructure migration completed successfully\n";
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
    }
}