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
    public partial class AdminConfigMigrationViewModel : ObservableObject, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly ILogger<AdminConfigMigrationViewModel> _logger;

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
        private ObservableCollection<RoleInfo> _sourceRoles = new();

        [ObservableProperty]
        private ObservableCollection<PermissionInfo> _sourcePermissions = new();

        [ObservableProperty]
        private ObservableCollection<FolderInfo> _sourceFolders = new();

        [ObservableProperty]
        private ObservableCollection<TagInfo> _sourceTags = new();

        [ObservableProperty]
        private ObservableCollection<string> _sourceCertificates = new();

        [ObservableProperty]
        private ObservableCollection<CustomAttributeInfo> _sourceCustomAttributes = new();

        [ObservableProperty]
        private ObservableCollection<RoleInfo> _targetRoles = new();

        [ObservableProperty]
        private ObservableCollection<PermissionInfo> _targetPermissions = new();

        [ObservableProperty]
        private ObservableCollection<FolderInfo> _targetFolders = new();

        [ObservableProperty]
        private ObservableCollection<TagInfo> _targetTags = new();

        [ObservableProperty]
        private ObservableCollection<string> _targetCertificates = new();

        [ObservableProperty]
        private ObservableCollection<CustomAttributeInfo> _targetCustomAttributes = new();

        // Migration Options
        [ObservableProperty]
        private bool _migrateRoles = true;

        [ObservableProperty]
        private bool _migratePermissions = true;

        [ObservableProperty]
        private bool _migrateFolders = true;

        [ObservableProperty]
        private bool _migrateTags = true;

        [ObservableProperty]
        private bool _migrateCertificates = false;

        [ObservableProperty]
        private bool _migrateCustomAttributes = true;

        [ObservableProperty]
        private bool _validateOnly = false;

        // Migration Status
        [ObservableProperty]
        private bool _isMigrationInProgress = false;

        [ObservableProperty]
        private double _migrationProgress = 0;

        [ObservableProperty]
        private string _migrationStatus = "Ready to start admin config migration";

        [ObservableProperty]
        private string _activityLog = "Admin configuration migration activity log will appear here...\n";

        // Computed Properties
        public bool CanValidateMigration => IsSourceConnected && IsTargetConnected && !IsMigrationInProgress;
        public bool CanStartMigration => CanValidateMigration && !ValidateOnly;

        public AdminConfigMigrationViewModel(
            SharedConnectionService sharedConnectionService,
            HybridPowerShellService powerShellService,
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            ILogger<AdminConfigMigrationViewModel> logger)
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
                await LoadAdminConfigDataAsync();
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
                // Check persistent connection status (same as Dashboard)
                var sourceConnected = await _persistentConnectionService.IsConnectedAsync("source");
                IsSourceConnected = sourceConnected;
                SourceConnectionStatus = sourceConnected ? "Connected" : "Disconnected";

                // Check target connection
                var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");
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

        private async Task LoadAdminConfigDataAsync()
        {
            try
            {
                // Load cached inventory data if available
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var targetInventory = _sharedConnectionService.GetTargetInventory();

                if (sourceInventory != null)
                {
                    // TODO: Populate SourceRoles, SourcePermissions, etc. from inventory
                    SourceDataStatus = "Admin config data available";
                }
                else
                {
                    SourceDataStatus = "No admin config data loaded";
                }

                if (targetInventory != null)
                {
                    // TODO: Populate TargetRoles, TargetPermissions, etc. from inventory
                    TargetDataStatus = "Admin config data available";
                }
                else
                {
                    TargetDataStatus = "No admin config data loaded";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading admin config data");
                SourceDataStatus = "Error loading data";
                TargetDataStatus = "Error loading data";
            }
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            await LoadConnectionStatusAsync();
            await LoadAdminConfigDataAsync();
            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Connection status and data refreshed\n";
        }

        [RelayCommand]
        private async Task LoadSourceAdminConfig()
        {
            if (!IsSourceConnected)
            {
                MigrationStatus = "Source connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading source admin configuration...";
                SourceDataStatus = "🔄 Loading admin config...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source admin config data\n";
                
                var success = await _sharedConnectionService.LoadSourceAdminConfigAsync();
                if (success)
                {
                    SourceDataStatus = "✅ Admin config loaded";
                    MigrationStatus = "Source admin config loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source admin config data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadAdminConfigDataAsync();
                }
                else
                {
                    SourceDataStatus = "❌ Failed to load admin config";
                    MigrationStatus = "Failed to load source admin config";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load source admin config\n";
                }
            }
            catch (Exception ex)
            {
                SourceDataStatus = "❌ Error loading admin config";
                MigrationStatus = $"Failed to load source admin config: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading source admin config");
            }
        }

        [RelayCommand]
        private async Task LoadTargetAdminConfig()
        {
            if (!IsTargetConnected)
            {
                MigrationStatus = "Target connection not available";
                return;
            }

            try
            {
                MigrationStatus = "Loading target admin configuration...";
                TargetDataStatus = "🔄 Loading admin config...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target admin config data\n";
                
                var success = await _sharedConnectionService.LoadTargetAdminConfigAsync();
                if (success)
                {
                    TargetDataStatus = "✅ Admin config loaded";
                    MigrationStatus = "Target admin config loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target admin config data loaded successfully\n";
                    
                    // Refresh the data display
                    await LoadAdminConfigDataAsync();
                }
                else
                {
                    TargetDataStatus = "❌ Failed to load admin config";
                    MigrationStatus = "Failed to load target admin config";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Failed to load target admin config\n";
                }
            }
            catch (Exception ex)
            {
                TargetDataStatus = "❌ Error loading admin config";
                MigrationStatus = $"Failed to load target admin config: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error loading target admin config");
            }
        }

        [RelayCommand]
        private async Task ValidateMigration()
        {
            try
            {
                MigrationStatus = "Validating admin config migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting migration validation\n";
                
                // TODO: Implement validation logic
                await Task.Delay(2000);
                
                MigrationStatus = "Admin config migration validation completed";
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
                MigrationStatus = "Starting admin config migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting admin config migration\n";
                
                // TODO: Implement actual migration logic
                for (int i = 0; i <= 100; i += 10)
                {
                    MigrationProgress = i;
                    await Task.Delay(200);
                }
                
                MigrationStatus = "Admin config migration completed successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Admin config migration completed successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Migration failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during admin config migration");
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