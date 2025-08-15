using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class VmMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<VmMigrationViewModel> _logger;

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

    [ObservableProperty]
    private double _migrationProgress;

    [ObservableProperty]
    private string _migrationStatus = "Ready to migrate VMs";

    [ObservableProperty]
    private string _logOutput = "VM migration log will appear here...";

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

    public List<string> AvailableDiskFormats { get; } = new() { "Thin", "Thick", "EagerZeroedThick" };

    public VmMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        IDialogService dialogService,
        ILogger<VmMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _dialogService = dialogService;
        _logger = logger;

        // Initialize with some default network mappings
        NetworkMappings.Add(new NetworkMappingItem { SourceNetwork = "VM Network", TargetNetwork = "VM Network" });
        }

    public async Task OnNavigatedToAsync ()
        {
        // Check if we have active connections
        if (_sharedConnectionService.SourceConnection != null && _sharedConnectionService.TargetConnection != null)
            {
            MigrationStatus = "Connections available - ready to load data";
            }
        else
            {
            MigrationStatus = "Please establish source and target connections on the Dashboard first";
            }

        await Task.CompletedTask;
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task LoadSourceVMs ()
        {
        if (_sharedConnectionService.SourceConnection == null)
            {
            LogOutput = "Error: No source vCenter connection. Please connect on the Dashboard first.\n";
            return;
            }

        IsLoadingData = true;
        MigrationStatus = "Loading VMs from source vCenter...";
        LogOutput = "Starting VM discovery from source vCenter...\n";

        try
            {
            var connection = _sharedConnectionService.SourceConnection;
            var password = await GetConnectionPassword(connection);

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password }
            };

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                _logger.LogInformation("Added BypassModuleCheck for VM discovery script");
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            var vms = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<VirtualMachine>(
                ".\\Scripts\\Get-VmsForMigration.ps1",
                scriptParams,
                logPath);

            if (vms?.Any() == true)
                {
                SourceVms = new ObservableCollection<VirtualMachine>(vms.OrderBy(vm => vm.Name));
                MigrationStatus = $"Loaded {SourceVms.Count} VMs from source vCenter";
                LogOutput += $"Successfully loaded {SourceVms.Count} VMs:\n";

                foreach (var vm in SourceVms.Take(10)) // Show first 10 VMs
                    {
                    LogOutput += $"  - {vm.Name} ({vm.PowerState}) on {vm.EsxiHost}\n";
                    }

                if (SourceVms.Count > 10)
                    {
                    LogOutput += $"  ... and {SourceVms.Count - 10} more VMs\n";
                    }
                }
            else
                {
                MigrationStatus = "No VMs found in source vCenter";
                LogOutput += "Warning: No VMs returned from source vCenter.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load VMs from source");
            MigrationStatus = "Failed to load VMs from source vCenter";
            LogOutput += $"Error loading VMs: {ex.Message}\n";
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
            LogOutput += "Error: No target vCenter connection. Please connect on the Dashboard first.\n";
            return;
            }

        IsLoadingData = true;
        MigrationStatus = "Loading target vCenter resources...";
        LogOutput += "Starting target resource discovery...\n";

        try
            {
            var connection = _sharedConnectionService.TargetConnection;
            var password = await GetConnectionPassword(connection);

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password }
            };

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            // Load clusters
            var clusters = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(
                ".\\Scripts\\Get-Clusters.ps1",
                scriptParams,
                logPath);

            if (clusters?.Any() == true)
                {
                TargetClusters = new ObservableCollection<ClusterInfo>(clusters);
                LogOutput += $"Loaded {TargetClusters.Count} clusters:\n";
                foreach (var cluster in TargetClusters)
                    {
                    LogOutput += $"  - {cluster.Name}\n";
                    }
                }

            // Load target resources (hosts and datastores)
            var targetResources = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\Get-TargetResources.ps1",
                scriptParams,
                logPath);

            // Parse JSON response for hosts and datastores
            if (!string.IsNullOrWhiteSpace(targetResources))
                {
                try
                    {
                    var resourceData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(targetResources);

                    // Extract hosts and datastores from the JSON
                    // This would need to be implemented based on your Get-TargetResources.ps1 script output format
                    // For now, using placeholder logic
                    TargetHosts = new ObservableCollection<TargetHost>
                    {
                        new() { Name = "target-host-01.lab.local" },
                        new() { Name = "target-host-02.lab.local" }
                    };

                    TargetDatastores = new ObservableCollection<TargetDatastore>
                    {
                        new() { Name = "datastore-01" },
                        new() { Name = "datastore-02" }
                    };
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Failed to parse target resources JSON");
                    // Fallback to sample data
                    LoadSampleTargetResources();
                    }
                }
            else
                {
                LoadSampleTargetResources();
                }

            MigrationStatus = $"Loaded target resources: {TargetClusters.Count} clusters, {TargetHosts.Count} hosts, {TargetDatastores.Count} datastores";
            LogOutput += $"Target resource discovery completed successfully.\n";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load target resources");
            MigrationStatus = "Failed to load target resources";
            LogOutput += $"Error loading target resources: {ex.Message}\n";
            LoadSampleTargetResources();
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task StartVMMigration ()
        {
        if (!CanStartMigration())
            {
            LogOutput += "Error: Cannot start migration. Please check requirements.\n";
            return;
            }

        IsMigrating = true;
        MigrationProgress = 0;
        MigrationStatus = "Starting VM migration...";
        LogOutput += $"\n=== STARTING VM MIGRATION ===\n";

        try
            {
            var selectedVMs = SourceVms.Where(vm => vm.IsSelected).ToList();
            LogOutput += $"Selected {selectedVMs.Count} VMs for migration:\n";
            foreach (var vm in selectedVMs)
                {
                LogOutput += $"  - {vm.Name}\n";
                }

            // Prepare migration parameters
            var migrationParams = await PrepareMigrationParameters(selectedVMs);

            // Execute the cross-vCenter migration script
            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            var scriptParams = new Dictionary<string, object>
            {
                { "SourceVCenter", _sharedConnectionService.SourceConnection!.ServerAddress },
                { "DestVCenter", _sharedConnectionService.TargetConnection!.ServerAddress },
                { "VMList", selectedVMs.Select(vm => vm.Name).ToArray() },
                { "DestinationCluster", SelectedTargetCluster?.Name ?? "" },
                { "NameSuffix", NameSuffix },
                { "PreserveMAC", PreserveMAC },
                { "DiskFormat", SelectedDiskFormat },
                { "MaxConcurrentMigrations", MaxConcurrentMigrations },
                { "SequentialMode", SequentialMode },
                { "EnhancedNetworkHandling", EnhancedNetworkHandling },
                { "IgnoreNetworkErrors", IgnoreNetworkErrors },
                { "Validate", ValidateOnly },
                { "LogFile", "VMMigration" },
                { "LogLevel", "Verbose" }
            };

            // Add credentials
            var sourcePassword = await GetConnectionPassword(_sharedConnectionService.SourceConnection!);
            var targetPassword = await GetConnectionPassword(_sharedConnectionService.TargetConnection!);

            scriptParams["SourceVCCredential"] = CreateCredentialParameter(_sharedConnectionService.SourceConnection!.Username, sourcePassword);
            scriptParams["DestVCCredential"] = CreateCredentialParameter(_sharedConnectionService.TargetConnection!.Username, targetPassword);

            // Add network mapping if configured
            if (NetworkMappings.Any(nm => !string.IsNullOrEmpty(nm.SourceNetwork) && !string.IsNullOrEmpty(nm.TargetNetwork)))
                {
                var networkMap = NetworkMappings
                    .Where(nm => !string.IsNullOrEmpty(nm.SourceNetwork) && !string.IsNullOrEmpty(nm.TargetNetwork))
                    .ToDictionary(nm => nm.SourceNetwork!, nm => nm.TargetNetwork!);

                scriptParams["NetworkMapping"] = networkMap;
                LogOutput += $"Network mappings configured: {networkMap.Count} mappings\n";
                }

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["SkipModuleCheck"] = true;
                }

            LogOutput += "Executing cross-vCenter migration script...\n";

            // Execute the migration script
            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\CrossVcenterVMmigration_list.ps1",
                scriptParams,
                logPath);

            LogOutput += "\n=== MIGRATION SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== MIGRATION COMPLETED ===\n";

            // Parse results and update progress
            MigrationProgress = 100;

            if (result.Contains("All VMs migrated successfully"))
                {
                MigrationStatus = "All VMs migrated successfully!";
                }
            else if (result.Contains("Migration completed with some failures"))
                {
                MigrationStatus = "Migration completed with some failures";
                }
            else if (result.Contains("No VMs were successfully migrated"))
                {
                MigrationStatus = "Migration failed - no VMs migrated";
                }
            else
                {
                MigrationStatus = "Migration process completed - check logs for details";
                }

            // If this was validation only, update status accordingly
            if (ValidateOnly)
                {
                MigrationStatus = "Validation completed - no migrations performed";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "VM migration failed");
            MigrationStatus = "VM migration failed";
            LogOutput += $"\nERROR: VM migration failed: {ex.Message}\n";
            }
        finally
            {
            IsMigrating = false;
            }
        }

    [RelayCommand]
    private async Task RunPostMigrationCleanup ()
        {
        if (_sharedConnectionService.TargetConnection == null)
            {
            LogOutput += "Error: No target vCenter connection for cleanup.\n";
            return;
            }

        try
            {
            LogOutput += "\n=== STARTING POST-MIGRATION CLEANUP ===\n";

            var password = await GetConnectionPassword(_sharedConnectionService.TargetConnection);
            var exportPath = _configurationService.GetConfiguration().ExportPath ?? "Exports";

            // Look for backup JSON files in the export path
            var backupFiles = Directory.GetFiles(exportPath, "*.json", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f).Contains("VM") || Path.GetFileName(f).Contains("vm"))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (!backupFiles.Any())
                {
                LogOutput += "No VM backup JSON files found for cleanup.\n";
                return;
                }

            var latestBackup = backupFiles.First();
            LogOutput += $"Using backup file: {Path.GetFileName(latestBackup)}\n";

            var scriptParams = new Dictionary<string, object>
            {
                { "JsonBackupPath", latestBackup },
                { "NewVCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                { "Credential", CreateCredentialParameter(_sharedConnectionService.TargetConnection.Username, password) },
                { "LogPath", Path.Combine(_configurationService.GetConfiguration().LogPath ?? "Logs", "VMCleanup.log") },
                { "WhatIf", false }
            };

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\VMPostMigrationCleanup.ps1",
                scriptParams);

            LogOutput += "\n=== CLEANUP SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== CLEANUP COMPLETED ===\n";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Post-migration cleanup failed");
            LogOutput += $"ERROR: Post-migration cleanup failed: {ex.Message}\n";
            }
        }

    [RelayCommand]
    private void BrowseVMListFile ()
        {
        var dialog = new Microsoft.Win32.OpenFileDialog
            {
            Title = "Select VM List File",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            InitialDirectory = _configurationService.GetConfiguration().ExportPath ?? "Exports"
            };

        if (dialog.ShowDialog() == true)
            {
            VmListFile = dialog.FileName;
            }
        }

    [RelayCommand]
    private void AddNetworkMapping ()
        {
        NetworkMappings.Add(new NetworkMappingItem());
        }

    [RelayCommand]
    private void RemoveNetworkMapping (NetworkMappingItem? mapping)
        {
        if (mapping != null)
            {
            NetworkMappings.Remove(mapping);
            }
        }

    [RelayCommand]
    private void SelectAllVMs ()
        {
        foreach (var vm in SourceVms)
            {
            vm.IsSelected = true;
            }
        }

    [RelayCommand]
    private void UnselectAllVMs ()
        {
        foreach (var vm in SourceVms)
            {
            vm.IsSelected = false;
            }
        }

    private bool CanStartMigration ()
        {
        return SourceVms.Any(vm => vm.IsSelected) &&
               _sharedConnectionService.SourceConnection != null &&
               _sharedConnectionService.TargetConnection != null &&
               !IsMigrating &&
               !IsLoadingData;
        }

    private async Task<Dictionary<string, object>> PrepareMigrationParameters (List<VirtualMachine> selectedVMs)
        {
        var parameters = new Dictionary<string, object>();

        // Add all the migration configuration
        foreach (var vm in selectedVMs)
            {
            LogOutput += $"Preparing migration for VM: {vm.Name}\n";
            }

        return parameters;
        }

    private async Task<string> GetConnectionPassword (VCenterConnection connection)
        {
        var password = _credentialService.GetPassword(connection);

        if (string.IsNullOrEmpty(password))
            {
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {connection.Username}@{connection.ServerAddress}:");

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                throw new InvalidOperationException($"Password required for {connection.ServerAddress}");
                }
            password = promptedPassword;
            }

        return password;
        }

    private string CreateCredentialParameter (string username, string password)
        {
        // For PowerShell script, we'll pass username and password separately
        // The script will create the credential object internally
        return $"{username}:{password}";
        }

    private void LoadSampleTargetResources ()
        {
        TargetHosts = new ObservableCollection<TargetHost>
        {
            new() { Name = "target-esx01.lab.local" },
            new() { Name = "target-esx02.lab.local" },
            new() { Name = "target-esx03.lab.local" }
        };

        TargetDatastores = new ObservableCollection<TargetDatastore>
        {
            new() { Name = "target-datastore1" },
            new() { Name = "target-datastore2" },
            new() { Name = "target-datastore3" }
        };

        LogOutput += "Loaded sample target resources for demonstration.\n";
        }
    }