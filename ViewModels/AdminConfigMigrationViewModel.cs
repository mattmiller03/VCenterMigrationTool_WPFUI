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
    public partial class AdminConfigMigrationViewModel : ObservableObject, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
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
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            CredentialService credentialService,
            ILogger<AdminConfigMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
            _persistentConnectionService = persistentConnectionService;
            _credentialService = credentialService;
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
                    // Clear and populate source collections
                    SourceRoles.Clear();
                    foreach (var role in sourceInventory.Roles ?? new List<RoleInfo>())
                    {
                        SourceRoles.Add(role);
                    }

                    SourcePermissions.Clear();
                    foreach (var permission in sourceInventory.Permissions ?? new List<PermissionInfo>())
                    {
                        SourcePermissions.Add(permission);
                    }

                    SourceFolders.Clear();
                    foreach (var folder in sourceInventory.Folders ?? new List<FolderInfo>())
                    {
                        SourceFolders.Add(folder);
                    }

                    SourceTags.Clear();
                    foreach (var tag in sourceInventory.Tags ?? new List<TagInfo>())
                    {
                        SourceTags.Add(tag);
                    }

                    SourceCustomAttributes.Clear();
                    foreach (var attribute in sourceInventory.CustomAttributes ?? new List<CustomAttributeInfo>())
                    {
                        SourceCustomAttributes.Add(attribute);
                    }

                    // Update status with counts
                    var roleCount = SourceRoles.Count(r => !r.IsSystem);
                    SourceDataStatus = $"✅ {roleCount} custom roles, {SourcePermissions.Count} permissions, " +
                                     $"{SourceFolders.Count} folders, {SourceTags.Count} tags, " +
                                     $"{SourceCustomAttributes.Count} custom attributes";
                }
                else
                {
                    SourceDataStatus = "No admin config data loaded";
                }

                if (targetInventory != null)
                {
                    // Clear and populate target collections
                    TargetRoles.Clear();
                    foreach (var role in targetInventory.Roles ?? new List<RoleInfo>())
                    {
                        TargetRoles.Add(role);
                    }

                    TargetPermissions.Clear();
                    foreach (var permission in targetInventory.Permissions ?? new List<PermissionInfo>())
                    {
                        TargetPermissions.Add(permission);
                    }

                    TargetFolders.Clear();
                    foreach (var folder in targetInventory.Folders ?? new List<FolderInfo>())
                    {
                        TargetFolders.Add(folder);
                    }

                    TargetTags.Clear();
                    foreach (var tag in targetInventory.Tags ?? new List<TagInfo>())
                    {
                        TargetTags.Add(tag);
                    }

                    TargetCustomAttributes.Clear();
                    foreach (var attribute in targetInventory.CustomAttributes ?? new List<CustomAttributeInfo>())
                    {
                        TargetCustomAttributes.Add(attribute);
                    }

                    // Update status with counts
                    var roleCount = TargetRoles.Count(r => !r.IsSystem);
                    TargetDataStatus = $"✅ {roleCount} custom roles, {TargetPermissions.Count} permissions, " +
                                      $"{TargetFolders.Count} folders, {TargetTags.Count} tags, " +
                                      $"{TargetCustomAttributes.Count} custom attributes";
                }
                else
                {
                    TargetDataStatus = "No admin config data loaded";
                }

                // Notify UI of collection changes
                OnPropertyChanged(nameof(SourceRoles));
                OnPropertyChanged(nameof(SourcePermissions));
                OnPropertyChanged(nameof(SourceFolders));
                OnPropertyChanged(nameof(SourceTags));
                OnPropertyChanged(nameof(SourceCustomAttributes));
                OnPropertyChanged(nameof(TargetRoles));
                OnPropertyChanged(nameof(TargetPermissions));
                OnPropertyChanged(nameof(TargetFolders));
                OnPropertyChanged(nameof(TargetTags));
                OnPropertyChanged(nameof(TargetCustomAttributes));
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
                
                var originalValidateOnly = ValidateOnly;
                ValidateOnly = true; // Force validation mode
                
                try
                {
                    // Run validation by calling migration methods in validate-only mode
                    var totalSteps = GetEnabledMigrationStepsCount();
                    var currentStep = 0;
                    
                    if (MigrateRoles)
                    {
                        await MigrateRolesAsync();
                        currentStep++;
                    }
                    
                    if (MigrateFolders)
                    {
                        await MigrateFoldersAsync();
                        currentStep++;
                    }
                    
                    if (MigrateTags)
                    {
                        await MigrateTagsAsync();
                        currentStep++;
                    }
                    
                    if (MigrateCustomAttributes)
                    {
                        await MigrateCustomAttributesAsync();
                        currentStep++;
                    }
                    
                    if (MigratePermissions)
                    {
                        await MigratePermissionsAsync();
                        currentStep++;
                    }
                }
                finally
                {
                    ValidateOnly = originalValidateOnly; // Restore original setting
                }
                
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
                
                var totalSteps = GetEnabledMigrationStepsCount();
                var currentStep = 0;
                
                // Migrate selected components
                if (MigrateRoles)
                {
                    await MigrateRolesAsync();
                    currentStep++;
                    MigrationProgress = (double)currentStep / totalSteps * 100;
                }
                
                if (MigrateFolders)
                {
                    await MigrateFoldersAsync();
                    currentStep++;
                    MigrationProgress = (double)currentStep / totalSteps * 100;
                }
                
                if (MigrateTags)
                {
                    await MigrateTagsAsync();
                    currentStep++;
                    MigrationProgress = (double)currentStep / totalSteps * 100;
                }
                
                if (MigrateCustomAttributes)
                {
                    await MigrateCustomAttributesAsync();
                    currentStep++;
                    MigrationProgress = (double)currentStep / totalSteps * 100;
                }
                
                if (MigratePermissions)
                {
                    await MigratePermissionsAsync();
                    currentStep++;
                    MigrationProgress = (double)currentStep / totalSteps * 100;
                }
                
                MigrationProgress = 100;
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

        private int GetEnabledMigrationStepsCount()
        {
            int steps = 0;
            if (MigrateRoles) steps++;
            if (MigrateFolders) steps++;
            if (MigrateTags) steps++;
            if (MigrateCustomAttributes) steps++;
            if (MigratePermissions) steps++;
            return Math.Max(1, steps); // Ensure at least 1 to avoid division by zero
        }

        private async Task MigrateRolesAsync()
        {
            try
            {
                MigrationStatus = "Migrating roles...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting role migration\n";
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "ValidateOnly", ValidateOnly },
                    { "OverwriteExisting", true },
                    { "BypassModuleCheck", true }
                };

                var result = await _powerShellService.RunDualVCenterScriptAsync(
                    "Scripts\\Migrate-Roles.ps1",
                    _sharedConnectionService.SourceConnection,
                    sourcePassword,
                    _sharedConnectionService.TargetConnection,
                    targetPassword,
                    parameters);

                if (result.StartsWith("SUCCESS:"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Roles migration completed\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Roles migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Roles migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during roles migration");
                throw;
            }
        }

        private async Task MigrateFoldersAsync()
        {
            try
            {
                MigrationStatus = "Migrating folders...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting folder migration\n";
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "ValidateOnly", ValidateOnly },
                    { "SkipExisting", true },
                    { "FolderTypes", new[] { "VM", "Host", "Network", "Datastore" } },
                    { "BypassModuleCheck", true }
                };

                var result = await _powerShellService.RunDualVCenterScriptAsync(
                    "Scripts\\Migrate-Folders.ps1",
                    _sharedConnectionService.SourceConnection,
                    sourcePassword,
                    _sharedConnectionService.TargetConnection,
                    targetPassword,
                    parameters);

                if (result.StartsWith("SUCCESS:"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Folders migration completed\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Folders migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Folders migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during folders migration");
                throw;
            }
        }

        private async Task MigrateTagsAsync()
        {
            try
            {
                MigrationStatus = "Migrating tags and categories...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting tags migration\n";
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "ValidateOnly", ValidateOnly },
                    { "OverwriteExisting", false },
                    { "MigrateTagAssignments", false }, // Skip tag assignments for now
                    { "BypassModuleCheck", true }
                };

                var result = await _powerShellService.RunDualVCenterScriptAsync(
                    "Scripts\\Migrate-Tags.ps1",
                    _sharedConnectionService.SourceConnection,
                    sourcePassword,
                    _sharedConnectionService.TargetConnection,
                    targetPassword,
                    parameters);

                if (result.StartsWith("SUCCESS:"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Tags migration completed\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Tags migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Tags migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during tags migration");
                throw;
            }
        }

        private async Task MigrateCustomAttributesAsync()
        {
            try
            {
                MigrationStatus = "Migrating custom attributes...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting custom attributes migration\n";
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "ValidateOnly", ValidateOnly },
                    { "OverwriteExisting", false },
                    { "MigrateAttributeValues", false }, // Skip values migration for now
                    { "BypassModuleCheck", true }
                };

                var result = await _powerShellService.RunDualVCenterScriptAsync(
                    "Scripts\\Migrate-CustomAttributes.ps1",
                    _sharedConnectionService.SourceConnection,
                    sourcePassword,
                    _sharedConnectionService.TargetConnection,
                    targetPassword,
                    parameters);

                if (result.StartsWith("SUCCESS:"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Custom attributes migration completed\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Custom attributes migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Custom attributes migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during custom attributes migration");
                throw;
            }
        }

        private async Task MigratePermissionsAsync()
        {
            try
            {
                MigrationStatus = "Migrating permissions...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting permissions migration\n";
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "ValidateOnly", ValidateOnly },
                    { "OverwriteExisting", false },
                    { "SkipMissingEntities", true },
                    { "BypassModuleCheck", true }
                };

                var result = await _powerShellService.RunDualVCenterScriptAsync(
                    "Scripts\\Migrate-Permissions.ps1",
                    _sharedConnectionService.SourceConnection,
                    sourcePassword,
                    _sharedConnectionService.TargetConnection,
                    targetPassword,
                    parameters);

                if (result.StartsWith("SUCCESS:"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Permissions migration completed\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Permissions migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Permissions migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during permissions migration");
                throw;
            }
        }
    }
}