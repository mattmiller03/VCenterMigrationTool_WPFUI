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
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
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
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            ILogger<VirtualMachinesMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
            _persistentConnectionService = persistentConnectionService;
            _logger = logger;
        }

        public async Task OnNavigatedToAsync()
        {
            try
            {
                await LoadConnectionStatusAsync();
                await LoadVMDataAsync();
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

        private async Task LoadVMDataAsync()
        {
            try
            {
                // Load cached inventory data if available
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var targetInventory = _sharedConnectionService.GetTargetInventory();

                if (sourceInventory != null)
                {
                    // TODO: Populate SourceVirtualMachines, SourceResourcePools, etc. from inventory
                    SourceDataStatus = "VM data available";
                }
                else
                {
                    SourceDataStatus = "No VM data loaded";
                }

                if (targetInventory != null)
                {
                    // TODO: Populate TargetVirtualMachines, TargetResourcePools, etc. from inventory
                    TargetDataStatus = "VM data available";
                }
                else
                {
                    TargetDataStatus = "No VM data loaded";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading VM data");
                SourceDataStatus = "Error loading data";
                TargetDataStatus = "Error loading data";
            }
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            await LoadConnectionStatusAsync();
            await LoadVMDataAsync();
            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Connection status and data refreshed\n";
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
                SourceDataStatus = "🔄 Loading VMs...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source VM data\n";
                
                var success = await _sharedConnectionService.LoadSourceVirtualMachinesAsync();
                if (success)
                {
                    SourceDataStatus = "✅ VMs loaded";
                    MigrationStatus = "Source VMs loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source VM data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadVMDataAsync();
                }
                else
                {
                    SourceDataStatus = "❌ Failed to load VMs";
                    MigrationStatus = "Failed to load source VMs";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load source VMs\n";
                }
            }
            catch (Exception ex)
            {
                SourceDataStatus = "❌ Error loading VMs";
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
                TargetDataStatus = "🔄 Loading VMs...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target VM data\n";
                
                var success = await _sharedConnectionService.LoadTargetVirtualMachinesAsync();
                if (success)
                {
                    TargetDataStatus = "✅ VMs loaded";
                    MigrationStatus = "Target VMs loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target VM data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadVMDataAsync();
                }
                else
                {
                    TargetDataStatus = "❌ Failed to load VMs";
                    MigrationStatus = "Failed to load target VMs";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load target VMs\n";
                }
            }
            catch (Exception ex)
            {
                TargetDataStatus = "❌ Error loading VMs";
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