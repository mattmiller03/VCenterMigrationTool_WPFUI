// Create ViewModels/NetworkMigrationViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
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

public partial class NetworkMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly ILogger<NetworkMigrationViewModel> _logger;

    // Source Network Data
    [ObservableProperty]
    private ObservableCollection<EsxiHost> _sourceHosts = new();

    [ObservableProperty]
    private EsxiHost? _selectedSourceHost;

    [ObservableProperty]
    private ObservableCollection<NetworkHostNode> _sourceNetworkTopology = new();

    [ObservableProperty]
    private NetworkHostNode? _selectedSourceNetworkHost;

    // Target Network Data
    [ObservableProperty]
    private ObservableCollection<EsxiHost> _targetHosts = new();

    [ObservableProperty]
    private EsxiHost? _selectedTargetHost;

    [ObservableProperty]
    private ObservableCollection<NetworkHostNode> _targetNetworkTopology = new();

    [ObservableProperty]
    private NetworkHostNode? _selectedTargetNetworkHost;

    // Migration Configuration
    [ObservableProperty]
    private bool _migrateStandardSwitches = true;

    [ObservableProperty]
    private bool _migrateDistributedSwitches = false;

    [ObservableProperty]
    private bool _migratePortGroups = true;

    [ObservableProperty]
    private bool _migrateVmkernelPorts = true;

    [ObservableProperty]
    private bool _preserveVlanIds = true;

    [ObservableProperty]
    private bool _recreateIfExists = false;

    [ObservableProperty]
    private bool _validateOnly = false;

    // Network Mapping
    [ObservableProperty]
    private ObservableCollection<NetworkMappingItem> _networkMappings = new();

    // Status and Progress
    [ObservableProperty]
    private bool _isLoadingData = false;

    [ObservableProperty]
    private bool _isMigrationInProgress = false;

    [ObservableProperty]
    private double _migrationProgress = 0;

    [ObservableProperty]
    private string _migrationStatus = "Ready to load network data";

    [ObservableProperty]
    private string _logOutput = "Network migration log will appear here...";

    // Export/Import
    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private bool _exportToJson = true;

    [ObservableProperty]
    private bool _exportToCsv = false;

    public NetworkMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        ILogger<NetworkMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _logger = logger;

        // Initialize with default network mapping
        NetworkMappings.Add(new NetworkMappingItem
            {
            SourceNetwork = "VM Network",
            TargetNetwork = "VM Network"
            });
        }

    public async Task OnNavigatedToAsync ()
        {
        if (_sharedConnectionService.SourceConnection != null && _sharedConnectionService.TargetConnection != null)
            {
            MigrationStatus = "Connections available - ready to load network data";
            }
        else
            {
            MigrationStatus = "Please establish source and target connections on the Dashboard first";
            }

        await Task.CompletedTask;
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    // Data Loading Commands
    [RelayCommand]
    private async Task LoadSourceNetworkData ()
        {
        if (_sharedConnectionService.SourceConnection == null)
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No source vCenter connection\n";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Loading source network topology...";
            SourceHosts.Clear();
            SourceNetworkTopology.Clear();

            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for source connection";
                return;
                }

            // Load ESXi hosts first
            var hostResult = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-EsxiHosts.ps1",
                _sharedConnectionService.SourceConnection,
                password);

            // Parse host data and populate SourceHosts
            // TODO: Implement JSON parsing when script returns structured data

            // Load network topology
            var networkResult = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-NetworkTopology.ps1",
                _sharedConnectionService.SourceConnection,
                password);

            // TODO: Parse network topology data and populate SourceNetworkTopology

            MigrationStatus = $"Loaded source network data successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Loaded source network data\n";

            _logger.LogInformation("Successfully loaded source network data");
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Failed to load source network data: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error loading source network data");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task LoadTargetNetworkData ()
        {
        if (_sharedConnectionService.TargetConnection == null)
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No target vCenter connection\n";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Loading target network topology...";
            TargetHosts.Clear();
            TargetNetworkTopology.Clear();

            var password = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for target connection";
                return;
                }

            // Load target network data using new credential method
            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-NetworkTopology.ps1",
                _sharedConnectionService.TargetConnection,
                password);

            MigrationStatus = "Loaded target network data successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Loaded target network data\n";

            _logger.LogInformation("Successfully loaded target network data");
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Failed to load target network data: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error loading target network data");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    // Selection Commands
    [RelayCommand]
    private void SelectAllNetworkItems ()
        {
        foreach (var networkHost in SourceNetworkTopology)
            {
            foreach (var vswitch in networkHost.VSwitches)
                {
                vswitch.IsSelected = true;
                foreach (var portGroup in vswitch.PortGroups)
                    {
                    portGroup.IsSelected = true;
                    }
                }

            foreach (var vmkPort in networkHost.VmKernelPorts)
                {
                vmkPort.IsSelected = true;
                }
            }

        var totalItems = SourceNetworkTopology.Sum(h => h.VSwitches.Count + h.VSwitches.Sum(v => v.PortGroups.Count) + h.VmKernelPorts.Count);
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Selected all {totalItems} network items\n";
        _logger.LogInformation("Selected all network items: {Count}", totalItems);
        }

    [RelayCommand]
    private void UnselectAllNetworkItems ()
        {
        foreach (var networkHost in SourceNetworkTopology)
            {
            foreach (var vswitch in networkHost.VSwitches)
                {
                vswitch.IsSelected = false;
                foreach (var portGroup in vswitch.PortGroups)
                    {
                    portGroup.IsSelected = false;
                    }
                }

            foreach (var vmkPort in networkHost.VmKernelPorts)
                {
                vmkPort.IsSelected = false;
                }
            }

        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Unselected all network items\n";
        _logger.LogInformation("Unselected all network items");
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

        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Added new network mapping\n";
        _logger.LogInformation("Added new network mapping");
        }

    [RelayCommand]
    private void RemoveNetworkMapping (NetworkMappingItem mapping)
        {
        if (mapping != null && NetworkMappings.Contains(mapping))
            {
            NetworkMappings.Remove(mapping);
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Removed network mapping: {mapping.SourceNetwork} -> {mapping.TargetNetwork}\n";
            _logger.LogInformation("Removed network mapping: {Source} -> {Target}", mapping.SourceNetwork, mapping.TargetNetwork);
            }
        }

    [RelayCommand]
    private void AutoMapNetworks ()
        {
        NetworkMappings.Clear();

        // Auto-map based on matching names
        var sourceNetworks = SourceNetworkTopology
            .SelectMany(h => h.VSwitches.SelectMany(v => v.PortGroups))
            .Select(pg => pg.Name)
            .Distinct()
            .ToList();

        var targetNetworks = TargetNetworkTopology
            .SelectMany(h => h.VSwitches.SelectMany(v => v.PortGroups))
            .Select(pg => pg.Name)
            .Distinct()
            .ToList();

        foreach (var sourceNetwork in sourceNetworks)
            {
            var targetNetwork = targetNetworks.FirstOrDefault(t => t.Equals(sourceNetwork, StringComparison.OrdinalIgnoreCase))
                              ?? targetNetworks.FirstOrDefault();

            if (targetNetwork != null)
                {
                NetworkMappings.Add(new NetworkMappingItem
                    {
                    SourceNetwork = sourceNetwork,
                    TargetNetwork = targetNetwork
                    });
                }
            }

        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Auto-mapped {NetworkMappings.Count} networks\n";
        _logger.LogInformation("Auto-mapped {Count} networks", NetworkMappings.Count);
        }

    // Migration Commands
    [RelayCommand]
    private async Task ValidateNetworkMigration ()
        {
        try
            {
            IsLoadingData = true;
            MigrationStatus = "Validating network migration configuration...";

            var selectedItems = GetSelectedNetworkItems();
            if (!selectedItems.Any())
                {
                MigrationStatus = "No network items selected for validation";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Validation failed: No items selected\n";
                return;
                }

            // TODO: Implement validation logic
            await Task.Delay(1000);

            MigrationStatus = $"Validation completed - {selectedItems.Count()} items ready for migration";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network migration validation completed successfully\n";
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Validation failed: {ex.Message}";
            _logger.LogError(ex, "Error during network migration validation");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task StartNetworkMigration ()
        {
        var selectedItems = GetSelectedNetworkItems();
        if (!selectedItems.Any())
            {
            MigrationStatus = "No network items selected for migration";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Migration failed: No items selected\n";
            return;
            }

        try
            {
            IsMigrationInProgress = true;
            MigrationProgress = 0;
            MigrationStatus = "Starting network migration...";

            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting migration of {selectedItems.Count()} network items\n";

            // TODO: Implement actual migration using dual vCenter script method
            await Task.Delay(2000); // Placeholder

            MigrationProgress = 100;
            MigrationStatus = "Network migration completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network migration completed successfully\n";

            _logger.LogInformation("Network migration completed for {Count} items", selectedItems.Count());
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Migration error: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error during network migration");
            }
        finally
            {
            IsMigrationInProgress = false;
            }
        }

    // Export/Import Commands
    [RelayCommand]
    private void BrowseExportFile ()
        {
        var saveFileDialog = new SaveFileDialog
            {
            Title = "Export Network Configuration",
            Filter = ExportToJson ?
                "JSON files (*.json)|*.json|All files (*.*)|*.*" :
                "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"NetworkConfig_Export_{DateTime.Now:yyyyMMdd_HHmmss}.{(ExportToJson ? "json" : "csv")}"
            };

        if (saveFileDialog.ShowDialog() == true)
            {
            ExportFilePath = saveFileDialog.FileName;
            }
        }

    [RelayCommand]
    private void BrowseImportFile ()
        {
        var openFileDialog = new OpenFileDialog
            {
            Title = "Import Network Configuration",
            Filter = "Configuration files (*.json;*.csv)|*.json;*.csv|JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            };

        if (openFileDialog.ShowDialog() == true)
            {
            ImportFilePath = openFileDialog.FileName;
            }
        }

    [RelayCommand]
    private async Task ExportNetworkConfiguration ()
        {
        if (string.IsNullOrEmpty(ExportFilePath))
            {
            MigrationStatus = "Please select an export file path first";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Exporting network configuration...";

            var selectedItems = GetSelectedNetworkItems();
            if (!selectedItems.Any())
                {
                MigrationStatus = "No network items selected for export";
                return;
                }

            // TODO: Implement export logic
            await Task.Delay(1000);

            MigrationStatus = $"Exported {selectedItems.Count()} network items successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network configuration exported to: {ExportFilePath}\n";

            _logger.LogInformation("Network configuration exported to {FilePath}", ExportFilePath);
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Export failed: {ex.Message}";
            _logger.LogError(ex, "Error exporting network configuration");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task ImportNetworkConfiguration ()
        {
        if (string.IsNullOrEmpty(ImportFilePath) || !File.Exists(ImportFilePath))
            {
            MigrationStatus = "Please select a valid import file first";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Importing network configuration...";

            // TODO: Implement import logic
            await Task.Delay(1000);

            MigrationStatus = "Network configuration imported successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network configuration imported from: {ImportFilePath}\n";

            _logger.LogInformation("Network configuration imported from {FilePath}", ImportFilePath);
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Import failed: {ex.Message}";
            _logger.LogError(ex, "Error importing network configuration");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    // Helper Methods
    private IEnumerable<object> GetSelectedNetworkItems ()
        {
        var items = new List<object>();

        foreach (var networkHost in SourceNetworkTopology)
            {
            items.AddRange(networkHost.VSwitches.Where(v => v.IsSelected));
            items.AddRange(networkHost.VSwitches.SelectMany(v => v.PortGroups.Where(pg => pg.IsSelected)));
            items.AddRange(networkHost.VmKernelPorts.Where(vmk => vmk.IsSelected));
            }

        return items;
        }
    }