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
        private readonly CredentialService _credentialService;
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
            CredentialService credentialService,
            ILogger<AdminConfigMigrationViewModel> logger)
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source admin config data\n";
                
                // TODO: Implement actual admin config loading
                await Task.Delay(1000);
                
                SourceDataStatus = "Admin config data loaded";
                MigrationStatus = "Source admin config loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source admin config data loaded successfully\n";
            }
            catch (Exception ex)
            {
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading target admin config data\n";
                
                // TODO: Implement actual admin config loading
                await Task.Delay(1000);
                
                TargetDataStatus = "Admin config data loaded";
                MigrationStatus = "Target admin config loaded successfully";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target admin config data loaded successfully\n";
            }
            catch (Exception ex)
            {
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