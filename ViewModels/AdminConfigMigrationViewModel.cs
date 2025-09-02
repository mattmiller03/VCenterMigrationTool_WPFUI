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
using VCenterMigrationTool.ViewModels.Base;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class AdminConfigMigrationViewModel : ActivityLogViewModelBase, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly CredentialService _credentialService;
        private readonly ConfigurationService _configurationService;
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

        [ObservableProperty]
        private bool _isAutoScrollEnabled = true;

        // Computed Properties
        public bool CanValidateMigration => IsSourceConnected && IsTargetConnected && !IsMigrationInProgress;
        public bool CanStartMigration => CanValidateMigration && !ValidateOnly;

        public AdminConfigMigrationViewModel(
            SharedConnectionService sharedConnectionService,
            HybridPowerShellService powerShellService,
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            CredentialService credentialService,
            ConfigurationService configurationService,
            ILogger<AdminConfigMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
            _persistentConnectionService = persistentConnectionService;
            _credentialService = credentialService;
            _configurationService = configurationService;
            _logger = logger;

            // Initialize activity log
            InitializeActivityLog("Admin Configuration Migration");
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
                // Check connection status (supports both API and PowerCLI)
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

        private Task LoadAdminConfigDataAsync()
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

            return Task.CompletedTask;
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
            // Check connection status and attempt reconnection if needed
            var connectionCheck = await EnsureSourceConnectionAsync();
            if (!connectionCheck)
            {
                MigrationStatus = "❌ Source connection failed - unable to load admin config";
                SourceDataStatus = "❌ Connection failed";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Source connection failed, admin config loading aborted\n";
                return;
            }

            try
            {
                MigrationStatus = "Loading source admin configuration...";
                SourceDataStatus = "🔄 Loading admin config...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting source admin config discovery\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering roles and permissions...\n";
                
                // Track progress through multiple phases
                var startTime = DateTime.Now;
                
                var success = await _sharedConnectionService.LoadSourceAdminConfigAsync();
                if (success)
                {
                    // Get the loaded data to show detailed results
                    var inventory = _sharedConnectionService.GetSourceInventory();
                    if (inventory != null)
                    {
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Roles discovery complete: Found {inventory.Roles.Count(r => !r.IsSystem)} custom roles, {inventory.Roles.Count(r => r.IsSystem)} system roles\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Permissions discovery complete: Found {inventory.Permissions.Count} permission assignments\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering VM folders structure...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Folders discovery complete: Found {inventory.Folders.Count} folders\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering tags and categories...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Tags discovery complete: Found {inventory.Categories.Count} categories, {inventory.Tags.Count} tags\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering custom attributes...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Custom attributes discovery complete: Found {inventory.CustomAttributes.Count} custom attributes\n";
                        
                        var duration = DateTime.Now - startTime;
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🎉 Source admin config discovery completed in {duration.TotalSeconds:F1}s\n";
                    }
                    
                    SourceDataStatus = "✅ Admin config loaded";
                    MigrationStatus = "Source admin config loaded successfully";
                    
                    // Refresh the data display
                    await LoadAdminConfigDataAsync();
                }
                else
                {
                    SourceDataStatus = "⚠️ Limited admin config loaded";
                    MigrationStatus = "Admin config loaded with limitations (VMware SDK unavailable)";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ WARNING: Admin config loaded with limitations\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: VMware.SDK.vSphere module not available - using PowerCLI basic discovery\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: Standard vCenter roles and permissions will be available\n";
                }
            }
            catch (Exception ex)
            {
                SourceDataStatus = "❌ Error loading admin config";
                MigrationStatus = $"Failed to load source admin config: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: {ex.Message}\n";
                
                // Check if the error might be related to missing SDK module
                if (ex.Message.Contains("SSO") || ex.Message.Contains("SsoAdmin") || ex.Message.Contains("SDK"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: This error may be due to missing VMware.SDK.vSphere module (PowerCLI 13.x+)\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: This module is deprecated in PowerCLI 13.x - basic discovery should still work\n";
                }
                
                _logger.LogError(ex, "Error loading source admin config");
            }
        }

        [RelayCommand]
        private async Task LoadTargetAdminConfig()
        {
            // Check connection status and attempt reconnection if needed
            var connectionCheck = await EnsureTargetConnectionAsync();
            if (!connectionCheck)
            {
                MigrationStatus = "❌ Target connection failed - unable to load admin config";
                TargetDataStatus = "❌ Connection failed";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Target connection failed, admin config loading aborted\n";
                return;
            }

            try
            {
                MigrationStatus = "Loading target admin configuration...";
                TargetDataStatus = "🔄 Loading admin config...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting target admin config discovery\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering roles and permissions...\n";
                
                // Track progress through multiple phases
                var startTime = DateTime.Now;
                
                var success = await _sharedConnectionService.LoadTargetAdminConfigAsync();
                if (success)
                {
                    // Get the loaded data to show detailed results
                    var inventory = _sharedConnectionService.GetTargetInventory();
                    if (inventory != null)
                    {
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Roles discovery complete: Found {inventory.Roles.Count(r => !r.IsSystem)} custom roles, {inventory.Roles.Count(r => r.IsSystem)} system roles\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Permissions discovery complete: Found {inventory.Permissions.Count} permission assignments\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering VM folders structure...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Folders discovery complete: Found {inventory.Folders.Count} folders\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering tags and categories...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Tags discovery complete: Found {inventory.Categories.Count} categories, {inventory.Tags.Count} tags\n";
                        
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Discovering custom attributes...\n";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Custom attributes discovery complete: Found {inventory.CustomAttributes.Count} custom attributes\n";
                        
                        var duration = DateTime.Now - startTime;
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🎉 Target admin config discovery completed in {duration.TotalSeconds:F1}s\n";
                    }
                    
                    TargetDataStatus = "✅ Admin config loaded";
                    MigrationStatus = "Target admin config loaded successfully";
                    
                    // Refresh the data display
                    await LoadAdminConfigDataAsync();
                }
                else
                {
                    TargetDataStatus = "⚠️ Limited admin config loaded";
                    MigrationStatus = "Target admin config loaded with limitations (VMware SDK unavailable)";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ WARNING: Target admin config loaded with limitations\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: VMware.SDK.vSphere module not available - using PowerCLI basic discovery\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: Standard vCenter roles and permissions will be available\n";
                }
            }
            catch (Exception ex)
            {
                TargetDataStatus = "❌ Error loading admin config";
                MigrationStatus = $"Failed to load target admin config: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: {ex.Message}\n";
                
                // Check if the error might be related to missing SDK module
                if (ex.Message.Contains("SSO") || ex.Message.Contains("SsoAdmin") || ex.Message.Contains("SDK"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: This error may be due to missing VMware.SDK.vSphere module (PowerCLI 13.x+)\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ INFO: This module is deprecated in PowerCLI 13.x - basic discovery should still work\n";
                }
                
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting role migration\n";
                
                // Get source data for logging
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var customRoleCount = sourceInventory?.Roles?.Count(r => !r.IsSystem) ?? 0;
                
                if (customRoleCount > 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📋 Migrating {customRoleCount} custom roles to target vCenter...\n";
                }
                
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
                    var action = ValidateOnly ? "validation" : "migration";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Roles {action} completed successfully\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Roles migration result: {result}\n";
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting folder migration\n";
                
                // Get source and target inventory for datacenter information
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var targetInventory = _sharedConnectionService.GetTargetInventory();
                
                if (sourceInventory?.Datacenters == null || sourceInventory.Datacenters.Count == 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No datacenters found in source vCenter\n";
                    return;
                }
                
                if (targetInventory?.Datacenters == null || targetInventory.Datacenters.Count == 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No datacenters found in target vCenter\n";
                    return;
                }
                
                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Source or target connection not available");
                }

                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                // Get the first datacenter from each vCenter (or use mapping if available)
                var sourceDatacenter = sourceInventory.Datacenters.FirstOrDefault();
                var targetDatacenter = targetInventory.Datacenters.FirstOrDefault();
                
                if (sourceDatacenter == null || targetDatacenter == null)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Unable to determine source or target datacenter\n";
                    return;
                }
                
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📁 Replicating folder structure from '{sourceDatacenter.Name}' to '{targetDatacenter.Name}'...\n";
                
                // Convert passwords to SecureString for PowerShell
                var sourceSecurePassword = new System.Security.SecureString();
                foreach (char c in sourcePassword)
                {
                    sourceSecurePassword.AppendChar(c);
                }
                sourceSecurePassword.MakeReadOnly();
                
                var targetSecurePassword = new System.Security.SecureString();
                foreach (char c in targetPassword)
                {
                    targetSecurePassword.AppendChar(c);
                }
                targetSecurePassword.MakeReadOnly();
                
                // Use Copy-VMFolderStructure.ps1 for folder replication
                var parameters = new Dictionary<string, object>
                {
                    { "SourceVCenter", _sharedConnectionService.SourceConnection.ServerAddress },
                    { "TargetVCenter", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "SourceDatacenterName", sourceDatacenter.Name },
                    { "TargetDatacenterName", targetDatacenter.Name },
                    { "SourceUser", _sharedConnectionService.SourceConnection.Username },
                    { "SourcePassword", sourceSecurePassword },
                    { "TargetUser", _sharedConnectionService.TargetConnection.Username },
                    { "TargetPassword", targetSecurePassword },
                    { "LogPath", _configurationService.GetConfiguration().LogPath },
                    { "SuppressConsoleOutput", false }
                };

                // Check if we're in validate-only mode
                if (ValidateOnly)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Running in validation mode - no folders will be created\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Validation: Would replicate folders from '{sourceDatacenter.Name}' to '{targetDatacenter.Name}'\n";
                    return;
                }

                var result = await _powerShellService.RunScriptAsync(
                    "Scripts\\Copy-VMFolderStructure.ps1",
                    parameters);

                // Parse the result for statistics
                if (result.Contains("Successfully copied folder structure"))
                {
                    // Extract statistics from the result
                    var lines = result.Split('\n');
                    string stats = "";
                    foreach (var line in lines)
                    {
                        if (line.Contains("Created:") || line.Contains("Skipped:") || line.Contains("Failed:"))
                        {
                            stats = line;
                            break;
                        }
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Folder structure replicated successfully\n";
                    if (!string.IsNullOrEmpty(stats))
                    {
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📊 {stats}\n";
                    }
                }
                else if (result.Contains("ERROR:") || result.Contains("failed"))
                {
                    var errorMessage = result.Contains("ERROR:") 
                        ? result.Substring(result.IndexOf("ERROR:") + 6)
                        : "Folder replication failed - check logs for details";
                    throw new Exception(errorMessage);
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Folder replication completed\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Folder replication failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during folder replication");
                throw;
            }
        }

        private async Task MigrateTagsAsync()
        {
            try
            {
                MigrationStatus = "Migrating tags and categories...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting tags and categories migration\n";
                
                // Get source data for logging
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var categoryCount = sourceInventory?.Categories?.Count ?? 0;
                var tagCount = sourceInventory?.Tags?.Count ?? 0;
                
                if (categoryCount > 0 || tagCount > 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🏷️ Migrating {categoryCount} categories and {tagCount} tags...\n";
                }
                
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
                    var action = ValidateOnly ? "validation" : "migration";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Tags and categories {action} completed successfully\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Tags migration result: {result}\n";
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting custom attributes migration\n";
                
                // Get source data for logging
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var attributeCount = sourceInventory?.CustomAttributes?.Count ?? 0;
                
                if (attributeCount > 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚙️ Migrating {attributeCount} custom attributes...\n";
                }
                
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
                    var action = ValidateOnly ? "validation" : "migration";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Custom attributes {action} completed successfully\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Custom attributes migration result: {result}\n";
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
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting permissions migration\n";
                
                // Get source data for logging
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var permissionCount = sourceInventory?.Permissions?.Count ?? 0;
                
                if (permissionCount > 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔐 Migrating {permissionCount} permission assignments...\n";
                }
                
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
                    var action = ValidateOnly ? "validation" : "migration";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Permissions {action} completed successfully\n";
                }
                else if (result.StartsWith("ERROR:"))
                {
                    throw new Exception(result.Substring(6));
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Permissions migration result: {result}\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Permissions migration failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during permissions migration");
                throw;
            }
        }

        #region Individual Export/Import Commands

        // Source Export Commands
        [RelayCommand]
        private async Task ExportRoles()
        {
            await ExportCategory("Roles", SourceRoles.ToList());
        }

        [RelayCommand]
        private async Task ExportPermissions()
        {
            await ExportCategory("Permissions", SourcePermissions.ToList());
        }

        [RelayCommand]
        private async Task ExportFolders()
        {
            try
            {
                // Add diagnostic logging before export
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔍 Diagnosing folder export...\n";
                
                // Check source connection
                var sourceConnected = _sharedConnectionService.SourceConnection != null;
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source connection available: {sourceConnected}\n";
                
                if (sourceConnected)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source server: {_sharedConnectionService.SourceConnection.ServerAddress}\n";
                }
                
                // Check source inventory
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var hasInventory = sourceInventory != null;
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source inventory available: {hasInventory}\n";
                
                if (hasInventory)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Inventory last updated: {sourceInventory.LastUpdated}\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Total folders in inventory: {sourceInventory.Folders?.Count ?? 0}\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] SourceFolders collection count: {SourceFolders.Count}\n";
                    
                    // List sample folder data if available
                    if (sourceInventory.Folders != null && sourceInventory.Folders.Count > 0)
                    {
                        var sampleFolders = sourceInventory.Folders.Take(3);
                        foreach (var folder in sampleFolders)
                        {
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Sample folder: '{folder.Name}' (Path: {folder.Path}, DC: {folder.DatacenterName})\n";
                        }
                    }
                }
                
                // Check if we need to refresh inventory
                if (!hasInventory || (sourceInventory?.Folders?.Count ?? 0) == 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No folder data available. Try refreshing the admin configuration first.\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 💡 Click 'Refresh Source Admin Config' or 'Refresh Target Admin Config' to load folder data.\n";
                    return;
                }

                await ExportCategory("Folders", SourceFolders.ToList());
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Folder export diagnostic failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during folder export diagnostics");
            }
        }

        [RelayCommand]
        private async Task ExportTags()
        {
            await ExportCategory("Tags", SourceTags.ToList());
        }

        [RelayCommand]
        private async Task ExportCertificates()
        {
            await ExportCategory("Certificates", SourceCertificates.ToList());
        }

        [RelayCommand]
        private async Task ExportCustomAttributes()
        {
            await ExportCategory("CustomAttributes", SourceCustomAttributes.ToList());
        }

        // Source Import Commands
        [RelayCommand]
        private async Task ImportRoles()
        {
            await ImportCategory("Roles");
        }

        [RelayCommand]
        private async Task ImportPermissions()
        {
            await ImportCategory("Permissions");
        }

        [RelayCommand]
        private async Task ImportFolders()
        {
            await ImportCategory("Folders");
        }

        [RelayCommand]
        private async Task ImportTags()
        {
            await ImportCategory("Tags");
        }

        [RelayCommand]
        private async Task ImportCertificates()
        {
            await ImportCategory("Certificates");
        }

        [RelayCommand]
        private async Task ImportCustomAttributes()
        {
            await ImportCategory("CustomAttributes");
        }

        // Target Export Commands
        [RelayCommand]
        private async Task ExportTargetRoles()
        {
            await ExportCategory("Roles", TargetRoles.ToList(), isTarget: true);
        }

        [RelayCommand]
        private async Task ExportTargetPermissions()
        {
            await ExportCategory("Permissions", TargetPermissions.ToList(), isTarget: true);
        }

        [RelayCommand]
        private async Task ExportTargetFolders()
        {
            await ExportCategory("Folders", TargetFolders.ToList(), isTarget: true);
        }

        [RelayCommand]
        private async Task ExportTargetTags()
        {
            await ExportCategory("Tags", TargetTags.ToList(), isTarget: true);
        }

        [RelayCommand]
        private async Task ExportTargetCertificates()
        {
            await ExportCategory("Certificates", TargetCertificates.ToList(), isTarget: true);
        }

        [RelayCommand]
        private async Task ExportTargetCustomAttributes()
        {
            await ExportCategory("CustomAttributes", TargetCustomAttributes.ToList(), isTarget: true);
        }

        // Helper methods for export/import functionality
        private async Task ExportCategory<T>(string categoryName, List<T> data, bool isTarget = false)
        {
            try
            {
                var sourceType = isTarget ? "Target" : "Source";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📤 Exporting {categoryName} from {sourceType} vCenter...\n";

                if (!data.Any())
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No {categoryName} data to export\n";
                    return;
                }

                // Use file dialog to get save location
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = $"Export {sourceType} {categoryName}",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"{sourceType}_{categoryName}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });

                    await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ {categoryName} exported successfully to: {dialog.FileName}\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📊 Exported {data.Count} {categoryName} items\n";
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Export cancelled by user\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Export failed: {ex.Message}\n";
                _logger.LogError(ex, "Failed to export {CategoryName}", categoryName);
            }
        }

        private async Task ImportCategory(string categoryName)
        {
            try
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📥 Importing {categoryName}...\n";

                // Use file dialog to get import file
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = $"Import {categoryName}",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📄 Loading data from: {dialog.FileName}\n";

                    // Parse and validate the JSON based on category type
                    switch (categoryName)
                    {
                        case "Roles":
                            var roles = System.Text.Json.JsonSerializer.Deserialize<List<RoleInfo>>(json);
                            if (roles != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Loaded {roles.Count} roles - Import functionality will be implemented in future update\n";
                            }
                            break;
                        case "Permissions":
                            var permissions = System.Text.Json.JsonSerializer.Deserialize<List<PermissionInfo>>(json);
                            if (permissions != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Loaded {permissions.Count} permissions - Import functionality will be implemented in future update\n";
                            }
                            break;
                        case "Folders":
                            var folders = System.Text.Json.JsonSerializer.Deserialize<List<FolderInfo>>(json);
                            if (folders != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📁 Loaded {folders.Count} folders from export file\n";
                                await ImportFoldersFromJson(folders);
                            }
                            break;
                        case "Tags":
                            var tags = System.Text.Json.JsonSerializer.Deserialize<List<TagInfo>>(json);
                            if (tags != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Loaded {tags.Count} tags - Import functionality will be implemented in future update\n";
                            }
                            break;
                        case "Certificates":
                            var certificates = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                            if (certificates != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Loaded {certificates.Count} certificates - Import functionality will be implemented in future update\n";
                            }
                            break;
                        case "CustomAttributes":
                            var customAttributes = System.Text.Json.JsonSerializer.Deserialize<List<CustomAttributeInfo>>(json);
                            if (customAttributes != null)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Loaded {customAttributes.Count} custom attributes - Import functionality will be implemented in future update\n";
                            }
                            break;
                        default:
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Unknown category: {categoryName}\n";
                            return;
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ {categoryName} data validated successfully\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 💡 Note: Full import functionality will be available in a future update\n";
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Import cancelled by user\n";
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Import failed: {ex.Message}\n";
                _logger.LogError(ex, "Failed to import {CategoryName}", categoryName);
            }
        }

        /// <summary>
        /// Import folders from JSON and create them in the target vCenter
        /// </summary>
        private async Task ImportFoldersFromJson(List<FolderInfo> folders)
        {
            try
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Starting folder import to target vCenter...\n";
                
                // Check target connection
                var connectionCheck = await EnsureTargetConnectionAsync();
                if (!connectionCheck)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Target connection failed - unable to import folders\n";
                    return;
                }
                
                // Get target inventory for datacenter information
                var targetInventory = _sharedConnectionService.GetTargetInventory();
                if (targetInventory?.Datacenters == null || targetInventory.Datacenters.Count == 0)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No datacenters found in target vCenter\n";
                    return;
                }
                
                // Get the first datacenter as default target
                var targetDatacenter = targetInventory.Datacenters.FirstOrDefault();
                if (targetDatacenter == null)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Unable to determine target datacenter\n";
                    return;
                }
                
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🎯 Target datacenter: '{targetDatacenter.Name}'\n";
                
                // Filter to only VM folders for import
                var vmFolders = folders.Where(f => f.Type.Equals("VM", StringComparison.OrdinalIgnoreCase)).ToList();
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📂 Found {vmFolders.Count} VM folders to import\n";
                
                if (!vmFolders.Any())
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ No VM folders found in export file\n";
                    return;
                }
                
                // Group folders by datacenter and create them
                var foldersByDatacenter = vmFolders.GroupBy(f => f.DatacenterName).ToList();
                
                foreach (var dcGroup in foldersByDatacenter)
                {
                    var sourceDcName = dcGroup.Key;
                    var dcFolders = dcGroup.OrderBy(f => f.Path.Count(c => c == '/')).ToList(); // Create parent folders first
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📁 Processing {dcFolders.Count} folders from source datacenter '{sourceDcName}'\n";
                    
                    var createdCount = 0;
                    var skippedCount = 0;
                    var failedCount = 0;
                    
                    foreach (var folder in dcFolders)
                    {
                        try
                        {
                            // Skip root folders (vm, Discovered virtual machine, etc.)
                            if (folder.Path.Count(c => c == '/') <= 2)
                            {
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⏭️ Skipping root folder: {folder.Name}\n";
                                skippedCount++;
                                continue;
                            }
                            
                            var result = await CreateFolderInTarget(folder, targetDatacenter.Name);
                            
                            if (result == "created")
                            {
                                createdCount++;
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Created folder: {folder.Name} in path: {folder.Path}\n";
                            }
                            else if (result == "exists")
                            {
                                skippedCount++;
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ℹ️ Folder already exists: {folder.Name}\n";
                            }
                            else
                            {
                                failedCount++;
                                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Failed to create folder: {folder.Name}\n";
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Error creating folder '{folder.Name}': {ex.Message}\n";
                            _logger.LogError(ex, "Error creating folder {FolderName}", folder.Name);
                        }
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 📊 Datacenter '{sourceDcName}' import results: Created: {createdCount}, Existed: {skippedCount}, Failed: {failedCount}\n";
                }
                
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Folder import completed\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 💡 Tip: Refresh the target admin config to see the new folders\n";
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Folder import failed: {ex.Message}\n";
                _logger.LogError(ex, "Error during folder import");
            }
        }
        
        /// <summary>
        /// Create a single folder in the target vCenter using PowerShell
        /// </summary>
        private async Task<string> CreateFolderInTarget(FolderInfo folderInfo, string targetDatacenterName)
        {
            try
            {
                if (_sharedConnectionService.TargetConnection == null)
                {
                    throw new InvalidOperationException("Target connection not available");
                }

                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
                
                // Convert password to SecureString for PowerShell
                var targetSecurePassword = new System.Security.SecureString();
                foreach (char c in targetPassword)
                {
                    targetSecurePassword.AppendChar(c);
                }
                targetSecurePassword.MakeReadOnly();
                
                // Create PowerShell script to create the folder
                var createFolderScript = @"
                param(
                    [string]$VCenterServer,
                    [string]$Username,
                    [securestring]$Password,
                    [string]$TargetDatacenterName,
                    [string]$FolderName,
                    [string]$FolderPath
                )
                
                try {
                    # Connect to vCenter (reuse existing connection if available)
                    $existingConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
                    if (-not $existingConnection -or -not $existingConnection.IsConnected) {
                        $credential = New-Object System.Management.Automation.PSCredential($Username, $Password)
                        Connect-VIServer -Server $VCenterServer -Credential $credential -Force | Out-Null
                    }
                    
                    # Get target datacenter
                    $targetDc = Get-Datacenter -Name $TargetDatacenterName -ErrorAction SilentlyContinue
                    if (-not $targetDc) {
                        throw ""Target datacenter '$TargetDatacenterName' not found""
                    }
                    
                    # Parse the folder path to get parent folder structure
                    $pathParts = $FolderPath.Split('/') | Where-Object { $_ -ne '' }
                    
                    # Start from the datacenter's VM folder
                    $parentFolder = Get-Folder -Type VM -Location $targetDc | Where-Object { $_.Name -eq 'vm' }
                    
                    # Navigate/create path excluding root parts
                    for ($i = 1; $i -lt ($pathParts.Length - 1); $i++) {
                        $pathPart = $pathParts[$i]
                        if ($pathPart -eq 'vm' -or $pathPart -eq $targetDc.Name) { continue }
                        
                        $childFolder = Get-Folder -Type VM -Location $parentFolder -Name $pathPart -ErrorAction SilentlyContinue
                        if (-not $childFolder) {
                            $childFolder = New-Folder -Location $parentFolder -Name $pathPart -Type VM
                        }
                        $parentFolder = $childFolder
                    }
                    
                    # Check if target folder already exists
                    $existingFolder = Get-Folder -Type VM -Location $parentFolder -Name $FolderName -ErrorAction SilentlyContinue
                    if ($existingFolder) {
                        Write-Output ""EXISTS""
                        return
                    }
                    
                    # Create the target folder
                    $newFolder = New-Folder -Location $parentFolder -Name $FolderName -Type VM
                    Write-Output ""CREATED:$($newFolder.Name)""
                    
                } catch {
                    Write-Output ""ERROR:$($_.Exception.Message)""
                }
                ";
                
                var parameters = new Dictionary<string, object>
                {
                    { "VCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                    { "Username", _sharedConnectionService.TargetConnection.Username },
                    { "Password", targetSecurePassword },
                    { "TargetDatacenterName", targetDatacenterName },
                    { "FolderName", folderInfo.Name },
                    { "FolderPath", folderInfo.Path }
                };

                var result = await _powerShellService.RunScriptAsync(createFolderScript, parameters);

                if (result.Contains("CREATED:"))
                {
                    return "created";
                }
                else if (result.Contains("EXISTS"))
                {
                    return "exists";
                }
                else if (result.Contains("ERROR:"))
                {
                    var errorMessage = result.Substring(result.IndexOf("ERROR:") + 6);
                    throw new Exception(errorMessage);
                }
                
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating folder {FolderName} in target", folderInfo.Name);
                throw;
            }
        }


        /// <summary>
        /// Add a message to the activity log with timestamp
        /// </summary>
        public new void LogMessage(string message, string level = "INFO")
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}\n";
            ActivityLog += logEntry;
            
            // If we have many lines, trim to keep performance good
            var lines = ActivityLog.Split('\n');
            if (lines.Length > 1000)
            {
                var keepLines = lines.Skip(lines.Length - 800).ToArray();
                ActivityLog = string.Join("\n", keepLines);
            }
        }

        /// <summary>
        /// Ensure source connection is active, attempt PowerCLI reconnection if needed
        /// </summary>
        private async Task<bool> EnsureSourceConnectionAsync()
        {
            try
            {
                // First check if connection is already active
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Checking source vCenter connection status...\n";
                
                var isConnected = await _sharedConnectionService.IsConnectedAsync("source");
                if (isConnected)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Source connection is active\n";
                    IsSourceConnected = true;
                    SourceConnectionStatus = "Connected";
                    return true;
                }
                
                // Connection failed, attempt PowerCLI reconnection
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️  Source connection not active, attempting PowerCLI reconnection...\n";
                
                var sourceProfile = _sharedConnectionService.SourceConnection;
                if (sourceProfile == null)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: No source connection profile configured\n";
                    return false;
                }
                
                var password = _credentialService.GetPassword(sourceProfile);
                if (string.IsNullOrEmpty(password))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: No credentials available for source connection\n";
                    return false;
                }
                
                // Attempt PowerCLI reconnection
                var reconnectSuccess = await AttemptPowerCLIReconnection(sourceProfile, password, "source");
                
                if (reconnectSuccess)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Source PowerCLI reconnection successful\n";
                    IsSourceConnected = true;
                    SourceConnectionStatus = "Connected (PowerCLI)";
                    return true;
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Source PowerCLI reconnection failed\n";
                    IsSourceConnected = false;
                    SourceConnectionStatus = "Disconnected";
                    return false;
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Exception during source connection check: {ex.Message}\n";
                _logger.LogError(ex, "Error ensuring source connection");
                IsSourceConnected = false;
                SourceConnectionStatus = "Connection Error";
                return false;
            }
        }

        /// <summary>
        /// Ensure target connection is active, attempt PowerCLI reconnection if needed
        /// </summary>
        private async Task<bool> EnsureTargetConnectionAsync()
        {
            try
            {
                // First check if connection is already active
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Checking target vCenter connection status...\n";
                
                var isConnected = await _sharedConnectionService.IsConnectedAsync("target");
                if (isConnected)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Target connection is active\n";
                    IsTargetConnected = true;
                    TargetConnectionStatus = "Connected";
                    return true;
                }
                
                // Connection failed, attempt PowerCLI reconnection
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️  Target connection not active, attempting PowerCLI reconnection...\n";
                
                var targetProfile = _sharedConnectionService.TargetConnection;
                if (targetProfile == null)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: No target connection profile configured\n";
                    return false;
                }
                
                var password = _credentialService.GetPassword(targetProfile);
                if (string.IsNullOrEmpty(password))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: No credentials available for target connection\n";
                    return false;
                }
                
                // Attempt PowerCLI reconnection
                var reconnectSuccess = await AttemptPowerCLIReconnection(targetProfile, password, "target");
                
                if (reconnectSuccess)
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Target PowerCLI reconnection successful\n";
                    IsTargetConnected = true;
                    TargetConnectionStatus = "Connected (PowerCLI)";
                    return true;
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Target PowerCLI reconnection failed\n";
                    IsTargetConnected = false;
                    TargetConnectionStatus = "Disconnected";
                    return false;
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR: Exception during target connection check: {ex.Message}\n";
                _logger.LogError(ex, "Error ensuring target connection");
                IsTargetConnected = false;
                TargetConnectionStatus = "Connection Error";
                return false;
            }
        }

        /// <summary>
        /// Attempt to reconnect using PowerCLI directly 
        /// </summary>
        private async Task<bool> AttemptPowerCLIReconnection(VCenterConnection connectionProfile, string password, string connectionType)
        {
            try
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] 🔄 Attempting PowerCLI reconnection to {connectionProfile.ServerAddress}...\n";
                
                // Use HybridPowerShellService to attempt connection
                var connectionScript = $@"
                    # DO NOT DISCONNECT - Using persistent connections managed by application
                    # Existing connections should be preserved for other operations
                    
                    # Attempt new connection
                    try {{
                        $credential = New-Object System.Management.Automation.PSCredential('{connectionProfile.Username}', (ConvertTo-SecureString '{password}' -AsPlainText -Force))
                        Connect-VIServer -Server '{connectionProfile.ServerAddress}' -Credential $credential -Force
                        
                        # Test connection with a simple command
                        $server = Get-VIServer -Server '{connectionProfile.ServerAddress}' -ErrorAction Stop
                        if ($server.IsConnected) {{
                            Write-Output 'SUCCESS: PowerCLI connection established'
                            Write-Output ""Version: $($server.Version)""
                        }} else {{
                            Write-Output 'ERROR: Connection not active'
                        }}
                    }} catch {{
                        Write-Output ""ERROR: $($_.Exception.Message)""
                    }}
                ";

                var result = await _powerShellService.RunScriptAsync(connectionScript, new Dictionary<string, object>());
                
                if (!string.IsNullOrEmpty(result) && result.Contains("SUCCESS: PowerCLI connection established"))
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ PowerCLI reconnection successful to {connectionProfile.ServerAddress}\n";
                    return true;
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ PowerCLI reconnection failed to {connectionProfile.ServerAddress}\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] PowerCLI Response: {result?.Substring(0, Math.Min(200, result?.Length ?? 0))}...\n";
                    return false;
                }
            }
            catch (Exception ex)
            {
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ ERROR during PowerCLI reconnection: {ex.Message}\n";
                _logger.LogError(ex, "Error during PowerCLI reconnection for {ConnectionType}", connectionType);
                return false;
            }
        }

        #endregion
    }
}