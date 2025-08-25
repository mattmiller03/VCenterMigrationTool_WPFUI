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
using VCenterMigrationTool.ViewModels.Base;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VmMigrationViewModel : ActivityLogViewModelBase, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly PersistentExternalConnectionService _persistentConnectionService;
    private readonly ILogger<VmMigrationViewModel> _logger;

    // Source and Target VM Collections
    [ObservableProperty]
    private ObservableCollection<VirtualMachine> _sourceVms = new();

    [ObservableProperty]
    private ObservableCollection<TargetHost> _targetHosts = new();

    [ObservableProperty]
    private TargetHost? _selectedTargetHost;

    [ObservableProperty]
    private ObservableCollection<TargetDatastore> _targetDatastores = new();

    [ObservableProperty]
    private TargetDatastore? _selectedTargetDatastore;

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private bool _isLoadingData;

    [ObservableProperty]
    private bool _isMigrating;

    // Connection Status Properties
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

    [ObservableProperty]
    private double _migrationProgress;

    [ObservableProperty]
    private string _migrationStatus = "Ready to migrate VMs";

    // Activity log inherited from ActivityLogViewModelBase

    // Migration Options
    [ObservableProperty]
    private string _vmListFile = string.Empty;

    [ObservableProperty]
    private string _nameSuffix = "-Imported";

    [ObservableProperty]
    private bool _preserveMAC = false;

    [ObservableProperty]
    private string _selectedDiskFormat = "Thin";

    [ObservableProperty]
    private int _maxConcurrentMigrations = 2;

    [ObservableProperty]
    private bool _sequentialMode = false;

    [ObservableProperty]
    private bool _enhancedNetworkHandling = true;

    [ObservableProperty]
    private bool _ignoreNetworkErrors = false;

    [ObservableProperty]
    private bool _validateOnly = false;

    // Network Mapping
    [ObservableProperty]
    private ObservableCollection<NetworkMappingItem> _networkMappings = new();

    // VM Backup Properties
    [ObservableProperty]
    private bool _backupSelectedVMs = true;

    [ObservableProperty]
    private bool _backupAllVMsInCluster = false;

    [ObservableProperty]
    private bool _backupAllVMsInVCenter = false;

    [ObservableProperty]
    private string _backupLocation = string.Empty;

    [ObservableProperty]
    private bool _includeVMSettings = true;

    [ObservableProperty]
    private bool _includeVMSnapshots = false;

    [ObservableProperty]
    private bool _includeVMAnnotations = true;

    [ObservableProperty]
    private bool _includeVMCustomAttributes = true;

    [ObservableProperty]
    private bool _includeVMPermissions = false;

    [ObservableProperty]
    private bool _compressBackup = true;

    [ObservableProperty]
    private string _backupStatus = "Ready";

    [ObservableProperty]
    private bool _isBackupInProgress = false;

    [ObservableProperty]
    private double _backupProgress = 0;

    [ObservableProperty]
    private ObservableCollection<string> _backupResults = new();

    public List<string> AvailableDiskFormats { get; } = new() { "Thin", "Thick", "EagerZeroedThick" };

    public VmMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        PersistentExternalConnectionService persistentConnectionService,
        ILogger<VmMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _persistentConnectionService = persistentConnectionService;
        _logger = logger;

        // Initialize dashboard-style activity log
        InitializeActivityLog("VM Migration");

        // Initialize with some default network mappings
        NetworkMappings.Add(new NetworkMappingItem { SourceNetwork = "VM Network", TargetNetwork = "VM Network" });

        // Initialize backup location
        BackupLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VCenterMigrationTool", "VMBackups");
        }

    public async Task OnNavigatedToAsync ()
        {
        try
        {
            await LoadConnectionStatusAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during VM migration page navigation");
            MigrationStatus = "Error loading page data. Please try refreshing.";
        }
        }

    private async Task LoadConnectionStatusAsync()
    {
        try
        {
            // Check connection status via SharedConnectionService (supports both API and PowerCLI)
            var sourceConnected = await _sharedConnectionService.IsConnectedAsync("source");
            IsSourceConnected = sourceConnected;
            SourceConnectionStatus = sourceConnected ? "Connected" : "Disconnected";

            var targetConnected = await _sharedConnectionService.IsConnectedAsync("target");
            IsTargetConnected = targetConnected;
            TargetConnectionStatus = targetConnected ? "Connected" : "Disconnected";

            // Update migration status
            if (sourceConnected && targetConnected)
            {
                var sourceServer = _sharedConnectionService.SourceConnection?.ServerAddress ?? "Unknown";
                var targetServer = _sharedConnectionService.TargetConnection?.ServerAddress ?? "Unknown";
                MigrationStatus = $"Connected to {sourceServer} and {targetServer} - ready to load data";
                _logger.LogInformation("VM migration page loaded with active connections");
            }
            else if (sourceConnected)
            {
                var sourceServer = _sharedConnectionService.SourceConnection?.ServerAddress ?? "Unknown";
                MigrationStatus = $"Connected to source ({sourceServer}) - target connection needed";
            }
            else if (targetConnected)
            {
                var targetServer = _sharedConnectionService.TargetConnection?.ServerAddress ?? "Unknown";
                MigrationStatus = $"Connected to target ({targetServer}) - source connection needed";
            }
            else
            {
                MigrationStatus = "Please establish source and target connections on the Dashboard first";
            }

            // Load existing inventory data if available
            var sourceInventory = _sharedConnectionService.GetSourceInventory();
            var targetInventory = _sharedConnectionService.GetTargetInventory();

            if (sourceInventory?.VirtualMachines?.Any() == true)
            {
                SourceDataStatus = $"✅ {sourceInventory.VirtualMachines.Count} VMs loaded";
            }
            else
            {
                SourceDataStatus = "No VM data loaded";
            }

            if (targetInventory?.VirtualMachines?.Any() == true)
            {
                TargetDataStatus = $"✅ {targetInventory.VirtualMachines.Count} VMs loaded";
            }
            else
            {
                TargetDataStatus = "No VM data loaded";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading connection status");
            IsSourceConnected = false;
            IsTargetConnected = false;
            SourceConnectionStatus = "Connection Error";
            TargetConnectionStatus = "Connection Error";
            MigrationStatus = "Error checking connection status";
        }
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    // Data Refresh and VM Loading Commands
    [RelayCommand]
    private async Task RefreshData()
    {
        await LoadConnectionStatusAsync();
        LogMessage("Data refreshed - connection status updated");
    }

    [RelayCommand]
    private async Task LoadSourceVMs ()
        {
        if (_sharedConnectionService.SourceConnection == null)
            {
            LogMessage("Error: No source vCenter connection. Please connect on the Dashboard first.", "ERROR");
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Loading source VMs...";
            SourceVms.Clear();

            // Get the password from credential service
            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);

            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found. Please configure credentials in Settings.";
                LogMessage("Error: No stored password found for source connection", "ERROR");
                return;
                }

            _logger.LogInformation("Loading VMs from source vCenter: {Server}", _sharedConnectionService.SourceConnection.ServerAddress);

            // Use the new credential method
            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-VMs.ps1",
                _sharedConnectionService.SourceConnection,
                password);

            if (!string.IsNullOrEmpty(result) && !result.Contains("ERROR"))
                {
                // Parse JSON result
                try
                    {
                    var vms = System.Text.Json.JsonSerializer.Deserialize<VirtualMachine[]>(result,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (vms != null)
                        {
                        foreach (var vm in vms)
                            {
                            SourceVms.Add(vm);
                            }
                        }

                    MigrationStatus = $"Loaded {SourceVms.Count} VMs from source vCenter";
                    LogMessage($"Loaded {SourceVms.Count} VMs from {_sharedConnectionService.SourceConnection.ServerAddress}");

                    _logger.LogInformation("Successfully loaded {Count} VMs from source vCenter", SourceVms.Count);
                    }
                catch (JsonException ex)
                    {
                    MigrationStatus = "Error parsing VM data from vCenter";
                    LogMessage($"Error parsing VM data: {ex.Message}", "ERROR");
                    _logger.LogError(ex, "Error parsing VM JSON data");
                    }
                }
            else
                {
                MigrationStatus = "Failed to load VMs from source vCenter";
                LogMessage($"Failed to load VMs: {result}", "ERROR");
                _logger.LogError("Failed to load VMs: {Result}", result);
                }
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Failed to load source VMs: {ex.Message}";
            LogMessage($"ERROR: {ex.Message}", "ERROR");
            _logger.LogError(ex, "Error loading source VMs");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task LoadTargetResources ()
        {
        if (_sharedConnectionService.TargetConnection == null)
            {
            LogMessage("Error: No target vCenter connection. Please connect on the Dashboard first.", "ERROR");
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Loading target resources...";

            var parameters = new Dictionary<string, object>
                {
                ["VCenterServer"] = _sharedConnectionService.TargetConnection.ServerAddress,
                ["Username"] = _sharedConnectionService.TargetConnection.Username,
                ["Password"] = _credentialService.GetPassword(_sharedConnectionService.TargetConnection) ?? ""
                };

            // Load target clusters
            var scriptPath = Path.Combine("Scripts", "Get-Clusters.ps1");
            var clusters = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(scriptPath, parameters);

            TargetClusters.Clear();
            foreach (var cluster in clusters)
                {
                TargetClusters.Add(cluster);
                }

            // Load target hosts and datastores would go here with additional script calls

            MigrationStatus = $"Loaded {TargetClusters.Count} target clusters";
            LogMessage($"Loaded target resources from {_sharedConnectionService.TargetConnection.ServerAddress}");

            _logger.LogInformation("Successfully loaded target resources");
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Failed to load target resources: {ex.Message}";
            LogMessage($"ERROR: {ex.Message}", "ERROR");
            _logger.LogError(ex, "Error loading target resources");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    // VM Selection Commands
    [RelayCommand]
    private void SelectAllVMs ()
        {
        if (SourceVms?.Any() == true)
            {
            foreach (var vm in SourceVms)
                {
                vm.IsSelected = true;
                }

            var selectedCount = SourceVms.Count;
            LogMessage($"Selected all {selectedCount} VMs");
            _logger.LogInformation("Selected all {Count} VMs", selectedCount);
            }
        }

    [RelayCommand]
    private void UnselectAllVMs ()
        {
        if (SourceVms?.Any() == true)
            {
            foreach (var vm in SourceVms)
                {
                vm.IsSelected = false;
                }

            var totalCount = SourceVms.Count;
            LogMessage($"Unselected all {totalCount} VMs");
            _logger.LogInformation("Unselected all {Count} VMs", totalCount);
            }
        }

    // Network Mapping Commands
    [RelayCommand]
    private void AddNetworkMapping ()
        {
        NetworkMappings.Add(new NetworkMappingItem
            {
            SourceNetwork = "Source Network",
            TargetNetwork = "Target Network"
            });

        LogMessage("Added new network mapping");
        _logger.LogInformation("Added new network mapping");
        }

    [RelayCommand]
    private void RemoveNetworkMapping (NetworkMappingItem mapping)
        {
        if (mapping != null && NetworkMappings.Contains(mapping))
            {
            NetworkMappings.Remove(mapping);
            LogMessage($"Removed network mapping: {mapping.SourceNetwork} -> {mapping.TargetNetwork}");
            _logger.LogInformation("Removed network mapping: {Source} -> {Target}", mapping.SourceNetwork, mapping.TargetNetwork);
            }
        }

    // Migration Commands (aliases for XAML compatibility)
    [RelayCommand]
    private async Task StartVMMigration ()
        {
        await StartMigration();
        }

    [RelayCommand]
    private async Task RunPostMigrationCleanup ()
        {
        if (!SourceVms.Any(vm => vm.IsSelected))
            {
            MigrationStatus = "No VMs selected for cleanup";
            LogMessage("No VMs selected for post-migration cleanup", "WARN");
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Running post-migration cleanup...";

            var selectedVMs = SourceVms.Where(vm => vm.IsSelected).ToList();
            LogMessage($"Starting post-migration cleanup for {selectedVMs.Count} VMs");

            // Prepare cleanup parameters
            var parameters = new Dictionary<string, object>
                {
                ["DestVCenter"] = _sharedConnectionService.TargetConnection?.ServerAddress ?? "",
                ["VMNames"] = selectedVMs.Select(vm => vm.Name).ToArray(),
                ["NameSuffix"] = NameSuffix
                };

            // Execute cleanup script
            var scriptPath = Path.Combine("Scripts", "VMPostMigrationCleanup.ps1");
            var result = await _powerShellService.RunScriptOptimizedAsync(scriptPath, parameters);

            if (result.Contains("SUCCESS") || result.Contains("completed"))
                {
                MigrationStatus = "Post-migration cleanup completed successfully";
                LogMessage("Post-migration cleanup completed successfully");
                }
            else
                {
                MigrationStatus = "Post-migration cleanup failed - check logs";
                LogMessage($"Post-migration cleanup failed: {result}", "ERROR");
                }

            _logger.LogInformation("Post-migration cleanup completed for {Count} VMs", selectedVMs.Count);
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Cleanup error: {ex.Message}";
            LogMessage($"ERROR: {ex.Message}", "ERROR");
            _logger.LogError(ex, "Error during post-migration cleanup");
            }
        finally
            {
            IsLoadingData = false;
            }
        }
    [RelayCommand]
    private async Task StartMigration ()
        {
        if (!SourceVms.Any(vm => vm.IsSelected))
            {
            MigrationStatus = "No VMs selected for migration";
            return;
            }

        try
            {
            IsMigrating = true;
            MigrationProgress = 0;
            MigrationStatus = "Starting VM migration...";

            var selectedVMs = SourceVms.Where(vm => vm.IsSelected).ToList();
            LogMessage($"Starting migration of {selectedVMs.Count} VMs");

            // Prepare migration parameters
            var parameters = new Dictionary<string, object>
                {
                ["SourceVCenter"] = _sharedConnectionService.SourceConnection?.ServerAddress ?? "",
                ["DestVCenter"] = _sharedConnectionService.TargetConnection?.ServerAddress ?? "",
                ["VMList"] = selectedVMs.Select(vm => vm.Name).ToArray(),
                ["NameSuffix"] = NameSuffix,
                ["DiskFormat"] = SelectedDiskFormat,
                ["PreserveMAC"] = PreserveMAC,
                ["MaxConcurrentMigrations"] = MaxConcurrentMigrations,
                ["SequentialMode"] = SequentialMode,
                ["EnhancedNetworkHandling"] = EnhancedNetworkHandling,
                ["IgnoreNetworkErrors"] = IgnoreNetworkErrors,
                ["Validate"] = ValidateOnly
                };

            // Execute migration script
            var scriptPath = Path.Combine("Scripts", "CrossVcenterVMmigration_list.ps1");
            var result = await _powerShellService.RunScriptOptimizedAsync(scriptPath, parameters);

            if (result.Contains("SUCCESS") || result.Contains("completed"))
                {
                MigrationProgress = 100;
                MigrationStatus = "Migration completed successfully";
                LogMessage("Migration completed successfully");
                }
            else
                {
                MigrationStatus = "Migration failed - check logs";
                LogMessage($"Migration failed: {result}", "ERROR");
                }

            _logger.LogInformation("VM migration completed for {Count} VMs", selectedVMs.Count);
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Migration error: {ex.Message}";
            LogMessage($"ERROR: {ex.Message}", "ERROR");
            _logger.LogError(ex, "Error during VM migration");
            }
        finally
            {
            IsMigrating = false;
            }
        }

    [RelayCommand]
    private async Task ValidateMigration ()
        {
        try
            {
            IsLoadingData = true;
            MigrationStatus = "Validating migration configuration...";

            // TODO: Implement validation logic
            await Task.Delay(1000);

            MigrationStatus = "Validation completed";
            LogMessage("Migration validation completed");
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Validation failed: {ex.Message}";
            _logger.LogError(ex, "Error during migration validation");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    // VM Backup Commands
    [RelayCommand]
    private void BrowseBackupLocation ()
        {
        var folderDialog = new OpenFileDialog
            {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder",
            Title = "Select VM Backup Location"
            };

        if (folderDialog.ShowDialog() == true)
            {
            BackupLocation = System.IO.Path.GetDirectoryName(folderDialog.FileName) ?? BackupLocation;
            }
        }

    [RelayCommand]
    private async Task BackupVMConfigurations ()
        {
        if (string.IsNullOrEmpty(BackupLocation))
            {
            BackupStatus = "Error: Please select a backup location";
            _logger.LogWarning("Cannot backup: No backup location specified");
            return;
            }

        // Validate that we have VMs to backup
        if (BackupSelectedVMs && (SourceVms == null || !SourceVms.Any()))
            {
            BackupStatus = "Error: No VMs loaded. Please load source VMs first.";
            _logger.LogWarning("Cannot backup: No VMs loaded in SourceVms collection");
            return;
            }

        if (BackupAllVMsInCluster && SelectedTargetCluster == null)
            {
            BackupStatus = "Error: No cluster selected for cluster backup.";
            _logger.LogWarning("Cannot backup: No cluster selected for cluster backup");
            return;
            }

        try
            {
            IsBackupInProgress = true;
            BackupStatus = "Preparing VM backup...";
            BackupProgress = 0;
            BackupResults.Clear();

            // Create backup directory if it doesn't exist
            if (!Directory.Exists(BackupLocation))
                {
                Directory.CreateDirectory(BackupLocation);
                }

            var backupScope = GetBackupScope();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"VM_Backup_{backupScope}_{timestamp}.json";
            var backupFilePath = Path.Combine(BackupLocation, backupFileName);

            // Get VM count for status message
            var vmCount = 0;
            if (BackupSelectedVMs && SourceVms.Any())
                {
                vmCount = SourceVms.Count(vm => vm.IsSelected);
                if (vmCount == 0)
                    {
                    vmCount = SourceVms.Count;
                    BackupStatus = $"No VMs selected - backing up all {vmCount} loaded VMs...";
                    }
                else
                    {
                    BackupStatus = $"Backing up {vmCount} selected VMs...";
                    }
                }
            else if (BackupAllVMsInCluster && SelectedTargetCluster != null)
                {
                BackupStatus = $"Backing up all VMs from cluster {SelectedTargetCluster.Name}...";
                }
            else if (BackupAllVMsInVCenter)
                {
                BackupStatus = "Backing up all VMs from vCenter...";
                }
            else
                {
                BackupStatus = $"Backing up VM configurations...";
                }

            BackupProgress = 10;

            // Prepare parameters for the backup script
            var parameters = new Dictionary<string, object>
                {
                ["BackupFilePath"] = backupFilePath,
                ["IncludeSettings"] = IncludeVMSettings,
                ["IncludeSnapshots"] = IncludeVMSnapshots,
                ["IncludeAnnotations"] = IncludeVMAnnotations,
                ["IncludeCustomAttributes"] = IncludeVMCustomAttributes,
                ["IncludePermissions"] = IncludeVMPermissions,
                ["CompressOutput"] = CompressBackup
                };

            // Add scope-specific parameters
            if (BackupSelectedVMs && SourceVms.Any())
                {
                // Get only the selected VMs using the IsSelected property
                var selectedVMs = SourceVms.Where(vm => vm.IsSelected).ToArray();
                if (selectedVMs.Any())
                    {
                    parameters["VMNames"] = selectedVMs.Select(vm => vm.Name).ToArray();
                    }
                else
                    {
                    // If no VMs are selected, backup all loaded VMs
                    parameters["VMNames"] = SourceVms.Select(vm => vm.Name).ToArray();
                    }
                }
            else if (BackupAllVMsInCluster && SelectedTargetCluster != null)
                {
                parameters["ClusterName"] = SelectedTargetCluster.Name;
                }
            else if (BackupAllVMsInVCenter)
                {
                parameters["BackupAllVMs"] = true;
                }

            BackupProgress = 25;
            BackupStatus = "Executing VM backup script...";

            // Execute the external PowerShell script
            var scriptPath = Path.Combine("Scripts", "BackupVMConfigurations.ps1");
            var result = await _powerShellService.RunScriptAsync(scriptPath, parameters);

            BackupProgress = 80;

            if (!string.IsNullOrEmpty(result) && result.Contains("SUCCESS"))
                {
                BackupProgress = 100;
                BackupStatus = "VM backup completed successfully";
                BackupResults.Add($"Backup saved to: {backupFilePath}");
                BackupResults.Add($"Backup scope: {backupScope}");
                BackupResults.Add($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // Add more detailed info about what was backed up
                if (BackupSelectedVMs && SourceVms.Any())
                    {
                    var selectedCount = SourceVms.Count(vm => vm.IsSelected);
                    if (selectedCount > 0)
                        {
                        BackupResults.Add($"Selected VMs: {selectedCount} of {SourceVms.Count}");
                        BackupResults.Add($"VM Names: {string.Join(", ", SourceVms.Where(vm => vm.IsSelected).Take(5).Select(vm => vm.Name))}{(selectedCount > 5 ? "..." : "")}");
                        }
                    else
                        {
                        BackupResults.Add($"All loaded VMs: {SourceVms.Count}");
                        }
                    }

                // Parse additional info from script output
                var lines = result.Split('\n');
                foreach (var line in lines)
                    {
                    if (line.StartsWith("VMs backed up:") || line.StartsWith("File size:"))
                        {
                        BackupResults.Add(line.Trim());
                        }
                    }

                _logger.LogInformation("VM backup completed successfully: {FilePath}", backupFilePath);
                }
            else
                {
                BackupStatus = "VM backup failed";
                BackupResults.Add($"Error: {result}");
                _logger.LogError("VM backup failed: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            BackupStatus = $"Backup failed: {ex.Message}";
            BackupResults.Add($"Exception: {ex.Message}");
            _logger.LogError(ex, "Error during VM backup");
            }
        finally
            {
            IsBackupInProgress = false;
            if (BackupProgress < 100)
                {
                BackupProgress = 0;
                }
            }
        }

    [RelayCommand]
    private async Task ValidateBackup ()
        {
        if (string.IsNullOrEmpty(BackupLocation) || !Directory.Exists(BackupLocation))
            {
            BackupStatus = "Error: Invalid backup location";
            return;
            }

        try
            {
            IsBackupInProgress = true;
            BackupStatus = "Validating backup files...";
            BackupProgress = 0;
            BackupResults.Clear();

            var parameters = new Dictionary<string, object>
                {
                ["BackupLocation"] = BackupLocation
                };

            BackupProgress = 50;

            // Execute the external validation script
            var scriptPath = Path.Combine("Scripts", "ValidateVMBackups.ps1");
            var result = await _powerShellService.RunScriptAsync(scriptPath, parameters);

            if (!string.IsNullOrEmpty(result) && result.Contains("SUCCESS"))
                {
                BackupProgress = 100;
                BackupStatus = "Backup validation completed";

                // Parse and display results
                var lines = result.Split('\n');
                foreach (var line in lines.Where(l => l.StartsWith("File:") || l.StartsWith("  ")))
                    {
                    BackupResults.Add(line.Trim());
                    }

                _logger.LogInformation("Backup validation completed successfully");
                }
            else
                {
                BackupStatus = "Validation failed";
                BackupResults.Add($"Error: {result}");
                _logger.LogError("Backup validation failed: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            BackupStatus = $"Validation failed: {ex.Message}";
            BackupResults.Add($"Exception: {ex.Message}");
            _logger.LogError(ex, "Error validating backups");
            }
        finally
            {
            IsBackupInProgress = false;
            }
        }

    [RelayCommand]
    private async Task RestoreVMConfiguration ()
        {
        var openFileDialog = new OpenFileDialog
            {
            Title = "Select VM Backup File to Restore",
            Filter = "Backup files (*.json;*.zip)|*.json;*.zip|JSON files (*.json)|*.json|ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
            InitialDirectory = BackupLocation
            };

        if (openFileDialog.ShowDialog() != true)
            {
            return;
            }

        try
            {
            IsBackupInProgress = true;
            BackupStatus = "Validating VM configuration backup...";
            BackupProgress = 0;
            BackupResults.Clear();

            var parameters = new Dictionary<string, object>
                {
                ["BackupFilePath"] = openFileDialog.FileName,
                ["ValidateOnly"] = true, // Always validate first for safety
                ["RestoreSettings"] = IncludeVMSettings,
                ["RestoreAnnotations"] = IncludeVMAnnotations,
                ["RestoreCustomAttributes"] = IncludeVMCustomAttributes
                };

            BackupProgress = 25;

            // Execute the external restore script
            var scriptPath = Path.Combine("Scripts", "RestoreVMConfigurations.ps1");
            var result = await _powerShellService.RunScriptAsync(scriptPath, parameters);

            BackupProgress = 90;

            if (!string.IsNullOrEmpty(result) && result.Contains("SUCCESS"))
                {
                BackupProgress = 100;
                BackupStatus = "VM configuration backup validated successfully";

                // Parse and display results
                var lines = result.Split('\n');
                foreach (var line in lines.Where(l => l.StartsWith("  ") || l.Contains("VM:")))
                    {
                    BackupResults.Add(line.Trim());
                    }

                BackupResults.Add($"Validated backup from: {openFileDialog.FileName}");
                BackupResults.Add("Note: This was a validation run. No changes were made.");

                _logger.LogInformation("VM backup validated from: {FilePath}", openFileDialog.FileName);
                }
            else
                {
                BackupStatus = "Backup validation failed";
                BackupResults.Add($"Error: {result}");
                _logger.LogError("VM backup validation failed: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            BackupStatus = $"Validation failed: {ex.Message}";
            BackupResults.Add($"Exception: {ex.Message}");
            _logger.LogError(ex, "Error during VM backup validation");
            }
        finally
            {
            IsBackupInProgress = false;
            }
        }

    // File Operations
    [RelayCommand]
    private void BrowseVMListFile ()
        {
        var openFileDialog = new OpenFileDialog
            {
            Title = "Select VM List File",
            Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

        if (openFileDialog.ShowDialog() == true)
            {
            VmListFile = openFileDialog.FileName;
            }
        }

    // Helper Methods
    private string GetBackupScope ()
        {
        if (BackupSelectedVMs)
            {
            var selectedCount = SourceVms?.Count(vm => vm.IsSelected) ?? 0;
            return selectedCount > 0 ? $"Selected_{selectedCount}VMs" : "AllLoadedVMs";
            }
        if (BackupAllVMsInCluster) return $"Cluster_{SelectedTargetCluster?.Name ?? "Unknown"}";
        if (BackupAllVMsInVCenter) return "AllVMs";
        return "SelectedVMs";
        }

    // Property change handlers for backup options
    partial void OnBackupSelectedVMsChanged (bool value)
        {
        if (value)
            {
            BackupAllVMsInCluster = false;
            BackupAllVMsInVCenter = false;
            }
        }

    partial void OnBackupAllVMsInClusterChanged (bool value)
        {
        if (value)
            {
            BackupSelectedVMs = false;
            BackupAllVMsInVCenter = false;
            }
        }

    partial void OnBackupAllVMsInVCenterChanged (bool value)
        {
        if (value)
            {
            BackupSelectedVMs = false;
            BackupAllVMsInCluster = false;
            }
        }

    // Activity log commands inherited from ActivityLogViewModelBase
    }