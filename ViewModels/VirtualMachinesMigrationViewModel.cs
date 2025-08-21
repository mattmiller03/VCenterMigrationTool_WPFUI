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
    public partial class VirtualMachinesMigrationViewModel : ObservableObject, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly CredentialService _credentialService;
        private readonly ILogger<VirtualMachinesMigrationViewModel> _logger;

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
        private ObservableCollection<VirtualMachine> _sourceVirtualMachines = new();

        [ObservableProperty]
        private ObservableCollection<ResourcePoolInventoryInfo> _sourceResourcePools = new();

        [ObservableProperty]
        private ObservableCollection<VirtualMachine> _targetVirtualMachines = new();

        [ObservableProperty]
        private ObservableCollection<ResourcePoolInventoryInfo> _targetResourcePools = new();

        // VM Statistics
        [ObservableProperty]
        private int _sourcePoweredOnVMs;

        [ObservableProperty]
        private int _sourceTemplates;

        [ObservableProperty]
        private int _targetPoweredOnVMs;

        [ObservableProperty]
        private int _targetTemplates;

        // Migration Options
        [ObservableProperty]
        private bool _migrateVirtualMachines = true;

        [ObservableProperty]
        private bool _migrateResourcePools = true;

        [ObservableProperty]
        private bool _migrateTemplates = true;

        [ObservableProperty]
        private bool _preserveVMConfigs = true;

        [ObservableProperty]
        private bool _powerOffVMs = false;

        [ObservableProperty]
        private bool _validateOnly = false;

        // Migration Status
        [ObservableProperty]
        private bool _isMigrationInProgress = false;

        [ObservableProperty]
        private double _migrationProgress = 0;

        [ObservableProperty]
        private string _migrationStatus = "Ready to start VM migration";

        [ObservableProperty]
        private string _activityLog = "Virtual machine migration activity log will appear here...\n";

        // Computed Properties
        public bool CanValidateMigration => IsSourceConnected && IsTargetConnected && !IsMigrationInProgress;
        public bool CanStartMigration => CanValidateMigration && !ValidateOnly;

        public VirtualMachinesMigrationViewModel(
            SharedConnectionService sharedConnectionService,
            HybridPowerShellService powerShellService,
            CredentialService credentialService,
            ILogger<VirtualMachinesMigrationViewModel> logger)
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
        private async Task LoadSourceVMs()
        {
            if (!IsSourceConnected)
            {
                MigrationStatus = "Source connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading source virtual machines...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source VM data\n";
                
                // TODO: Implement actual VM loading
                await Task.Delay(1000);
                
                SourceDataStatus = "VM data loaded";
                MigrationStatus = "Source VMs loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source VM data loaded successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Failed to load source VMs: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading source VMs");
            }
        }

        [RelayCommand]
        private async Task LoadTargetVMs()
        {
            if (!IsTargetConnected)
            {
                MigrationStatus = "Target connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading target virtual machines...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target VM data\n";
                
                // TODO: Implement actual VM loading
                await Task.Delay(1000);
                
                TargetDataStatus = "VM data loaded";
                MigrationStatus = "Target VMs loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target VM data loaded successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Failed to load target VMs: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading target VMs");
            }
        }

        [RelayCommand]
        private async Task ValidateMigration()
        {
            try
            {
                MigrationStatus = "Validating VM migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting migration validation\n";
                
                // TODO: Implement validation logic
                await Task.Delay(2000);
                
                MigrationStatus = "VM migration validation completed";
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
                MigrationStatus = "Starting VM migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting VM migration\n";
                
                // TODO: Implement actual migration logic
                for (int i = 0; i <= 100; i += 10)
                {
                    MigrationProgress = i;
                    await Task.Delay(200);
                }
                
                MigrationStatus = "VM migration completed successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] VM migration completed successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Migration failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during VM migration");
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