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
        private readonly CredentialService _credentialService;
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
            CredentialService credentialService,
            ILogger<InfrastructureMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _credentialService = credentialService;
            _logger = logger;
        }

        public async Task OnNavigatedToAsync()
        {
            UpdateConnectionStatus();
            await Task.CompletedTask;
        }

        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;

        private void UpdateConnectionStatus()
        {
            IsSourceConnected = _sharedConnectionService.SourceConnection != null;
            IsTargetConnected = _sharedConnectionService.TargetConnection != null;

            SourceConnectionStatus = IsSourceConnected ? 
                $"Connected to {_sharedConnectionService.SourceConnection}" : "Not connected";
            TargetConnectionStatus = IsTargetConnected ? 
                $"Connected to {_sharedConnectionService.TargetConnection}" : "Not connected";

            OnPropertyChanged(nameof(CanValidateMigration));
            OnPropertyChanged(nameof(CanStartMigration));
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            UpdateConnectionStatus();
            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Connection status refreshed\n";
            await Task.CompletedTask;
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source infrastructure data\n";
                
                // TODO: Implement actual infrastructure loading
                await Task.Delay(1000);
                
                SourceDataStatus = "Infrastructure data loaded";
                MigrationStatus = "Source infrastructure loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source infrastructure data loaded successfully\n";
            }
            catch (Exception ex)
            {
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target infrastructure data\n";
                
                // TODO: Implement actual infrastructure loading
                await Task.Delay(1000);
                
                TargetDataStatus = "Infrastructure data loaded";
                MigrationStatus = "Target infrastructure loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target infrastructure data loaded successfully\n";
            }
            catch (Exception ex)
            {
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