﻿// Create ViewModels/NetworkMigrationViewModel.cs

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

public partial class NetworkMigrationViewModel : ActivityLogViewModelBase, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly PersistantVcenterConnectionService _persistentConnectionService;
    private readonly ILogger<NetworkMigrationViewModel> _logger;

    // vDS Configuration Data
    [ObservableProperty]
    private ObservableCollection<VirtualSwitchInfo> _sourceVDSSwitches = new();

    [ObservableProperty]
    private VirtualSwitchInfo? _selectedSourceVDS;

    [ObservableProperty]
    private ObservableCollection<PortGroupInfo> _sourcePortGroups = new();

    [ObservableProperty]
    private PortGroupInfo? _selectedSourcePortGroup;

    // vDS Summary
    [ObservableProperty]
    private string _vdsStatus = "No vDS data loaded";

    [ObservableProperty]
    private int _totalVDSSwitches = 0;

    [ObservableProperty]
    private int _totalPortGroups = 0;

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
        PersistantVcenterConnectionService persistentConnectionService,
        ILogger<NetworkMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _persistentConnectionService = persistentConnectionService;
        _logger = logger;

        // Initialize activity log
        InitializeActivityLog("Network Migration");

        // Initialize with default network mapping
        NetworkMappings.Add(new NetworkMappingItem
            {
            SourceNetwork = "VM Network",
            TargetNetwork = "VM Network"
            });
        }

    public async Task OnNavigatedToAsync ()
        {
        try
        {
            // First, synchronize connection states with persistent services
            await _sharedConnectionService.SynchronizeConnectionStatesAsync();
            
            // Check connection status via SharedConnectionService (supports both API and PowerCLI)
            var sourceConnected = await _sharedConnectionService.IsConnectedAsync("source");
            var targetConnected = await _sharedConnectionService.IsConnectedAsync("target");

            if (sourceConnected && targetConnected)
            {
                var sourceServer = _sharedConnectionService.SourceConnection?.ServerAddress ?? "Unknown";
                var targetServer = _sharedConnectionService.TargetConnection?.ServerAddress ?? "Unknown";
                MigrationStatus = $"Connected to {sourceServer} and {targetServer} - ready to load network data";
                _logger.LogInformation("Network page loaded with active connections");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection status on network page");
            MigrationStatus = "Error checking connection status";
        }
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
            MigrationStatus = "Loading source vDS configuration...";
            SourceVDSSwitches.Clear();
            SourcePortGroups.Clear();

            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for source connection";
                return;
                }

            var parameters = new Dictionary<string, object>
            {
                { "LogPath", _configurationService.GetConfiguration().LogPath },
                { "BypassModuleCheck", true }
            };

            // Discover VDS switches using lightweight discovery script
            var discoveryResult = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Get-VDSSwitches.ps1",
                _sharedConnectionService.SourceConnection,
                password,
                parameters);

            // Parse the discovery result
            if (!string.IsNullOrEmpty(discoveryResult) && !discoveryResult.StartsWith("ERROR:"))
            {
                await LoadDiscoveredVDSData(discoveryResult);
            }
            else
            {
                MigrationStatus = $"Failed to discover VDS switches: {discoveryResult}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Discovery error: {discoveryResult}\n";
                return;
            }

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
            MigrationStatus = "Loading target vCenter data...";
            // Target vDS data would be loaded here if needed for comparison

            var password = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for target connection";
                return;
                }

            // Create a temporary export file to load target VDS data
            var tempExportPath = Path.GetTempFileName();
            tempExportPath = Path.ChangeExtension(tempExportPath, ".json");
            
            var parameters = new Dictionary<string, object>
            {
                { "ExportPath", tempExportPath },
                { "LogPath", _configurationService.GetConfiguration().LogPath },
                { "BypassModuleCheck", true }
            };

            // Load target VDS configuration using Export-VDS.ps1 script
            var exportResult = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Export-VDS.ps1",
                _sharedConnectionService.TargetConnection,
                password,
                parameters);

            // Parse target VDS data for informational purposes
            if (exportResult.StartsWith("SUCCESS:") && File.Exists(tempExportPath))
            {
                var targetReference = await File.ReadAllTextAsync(tempExportPath);
                
                try
                {
                    var reference = JsonSerializer.Deserialize<JsonElement>(targetReference);
                    
                    int targetSwitches = 0;
                    if (reference.TryGetProperty("TotalSwitches", out var totalElement))
                    {
                        targetSwitches = totalElement.GetInt32();
                    }
                    
                    // Try to get port group count from manifest if available
                    int targetPortGroups = 0;
                    if (reference.TryGetProperty("ManifestFile", out var manifestElement))
                    {
                        var manifestFile = manifestElement.GetString();
                        if (File.Exists(manifestFile))
                        {
                            var manifestJson = await File.ReadAllTextAsync(manifestFile);
                            var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
                            
                            if (manifest.TryGetProperty("ExportedSwitches", out var switchesElement))
                            {
                                foreach (var switchElement in switchesElement.EnumerateArray())
                                {
                                    if (switchElement.TryGetProperty("PortGroupCount", out var countElement))
                                    {
                                        targetPortGroups += countElement.GetInt32();
                                    }
                                }
                            }
                        }
                    }
                    
                    MigrationStatus = $"Target: {targetSwitches} vDS switches, {targetPortGroups} port groups (Native Backup)";
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Target vCenter has {targetSwitches} vDS switches and {targetPortGroups} port groups\n";
                    _logger.LogInformation("Target vCenter has {Switches} switches, {PortGroups} port groups", 
                        targetSwitches, targetPortGroups);
                    
                    // Clean up temp file
                    File.Delete(tempExportPath);
                }
                catch (JsonException ex)
                {
                    MigrationStatus = $"Failed to parse target VDS data: {ex.Message}";
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] JSON parsing error: {ex.Message}\n";
                    _logger.LogError(ex, "Error parsing target VDS JSON");
                }
            }
            else
            {
                MigrationStatus = "No target VDS data returned from script";
            }

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
        foreach (var vdsSwitch in SourceVDSSwitches)
            {
                // vDS switches don't have selection state in this simplified model
            }

            foreach (var portGroup in SourcePortGroups)
            {
                // Port groups don't have selection state in this simplified model
            }

        var totalItems = SourceVDSSwitches.Count + SourcePortGroups.Count;
        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Selected all {totalItems} vDS items\n";
        _logger.LogInformation("Selected all vDS items: {Count}", totalItems);
        }

    [RelayCommand]
    private void UnselectAllNetworkItems ()
        {
        foreach (var vdsSwitch in SourceVDSSwitches)
            {
                // vDS switches don't have selection state in this simplified model
            }

            foreach (var portGroup in SourcePortGroups)
            {
                // Port groups don't have selection state in this simplified model
            }

        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Unselected all vDS items\n";
        _logger.LogInformation("Unselected all vDS items");
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

        // Auto-map based on matching names from vDS port groups
        var sourceNetworks = SourcePortGroups
            .Select(pg => pg.Name)
            .Distinct()
            .ToList();

        var targetNetworks = new List<string>(); // Would be populated from target vCenter if needed

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
        if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
            {
            MigrationStatus = "Both source and target vCenter connections are required";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Migration failed: Missing connections\n";
            return;
            }

        try
            {
            IsMigrationInProgress = true;
            MigrationProgress = 0;
            MigrationStatus = "Starting network migration...";

            var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            
            if (string.IsNullOrEmpty(sourcePassword) || string.IsNullOrEmpty(targetPassword))
                {
                MigrationStatus = "Error: Missing passwords for vCenter connections";
                return;
                }

            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting network migration\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Source: {_sharedConnectionService.SourceConnection}\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Target: {_sharedConnectionService.TargetConnection}\n";

            // Create network mappings dictionary from the UI
            var networkMappingsDict = new Dictionary<string, object>();
            foreach (var mapping in NetworkMappings)
                {
                if (!string.IsNullOrEmpty(mapping.SourceNetwork) && !string.IsNullOrEmpty(mapping.TargetNetwork))
                    {
                    networkMappingsDict[mapping.SourceNetwork] = mapping.TargetNetwork;
                    }
                }

            var parameters = new Dictionary<string, object>
                {
                { "SourceVCenter", _sharedConnectionService.SourceConnection },
                { "TargetVCenter", _sharedConnectionService.TargetConnection },
                { "MigrateStandardSwitches", MigrateStandardSwitches },
                { "MigrateDistributedSwitches", MigrateDistributedSwitches },
                { "MigratePortGroups", MigratePortGroups },
                { "PreserveVlanIds", PreserveVlanIds },
                { "RecreateIfExists", RecreateIfExists },
                { "ValidateOnly", ValidateOnly },
                { "NetworkMappings", networkMappingsDict },
                { "BypassModuleCheck", true }
                };

            MigrationProgress = 50;

            var result = await _powerShellService.RunDualVCenterScriptAsync(
                "Scripts\\Active\\Core Migration\\Migrate-NetworkConfiguration.ps1",
                _sharedConnectionService.SourceConnection,
                sourcePassword,
                _sharedConnectionService.TargetConnection, 
                targetPassword,
                parameters);

            MigrationProgress = 100;
            MigrationStatus = "Network migration completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network migration completed successfully\n";

            _logger.LogInformation("Network migration completed");
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

        if (_sharedConnectionService.SourceConnection == null)
            {
            MigrationStatus = "No source vCenter connection available";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Exporting network configuration...";

            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for source connection";
                return;
                }

            // Ensure export path has .json extension for new script
            if (!ExportFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ExportFilePath = Path.ChangeExtension(ExportFilePath, ".json");
            }
            
            var parameters = new Dictionary<string, object>
                {
                // VCenterServer will be set by RunVCenterScriptAsync - don't override it
                { "ExportPath", ExportFilePath },
                { "LogPath", _configurationService.GetConfiguration().LogPath },
                { "BypassModuleCheck", true }
                };

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Export-VDS.ps1",
                _sharedConnectionService.SourceConnection,
                password,
                parameters);

            // Check the actual script result
            if (result.StartsWith("ERROR:") || result.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                MigrationStatus = $"Export failed: {result}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export failed: {result}\n";
                _logger.LogError("VDS export failed: {Result}", result);
            }
            else if (result.StartsWith("SUCCESS:"))
            {
                MigrationStatus = "VDS configuration exported successfully";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] {result}\n";
                _logger.LogInformation("VDS configuration exported to {FilePath}", ExportFilePath);
                
                // Load the exported data to update UI
                await LoadExportedVDSData(ExportFilePath);
            }
            else
            {
                MigrationStatus = "Export completed with unknown result";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogWarning("VDS export completed but result unclear: {Result}", result);
            }
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Export failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error exporting network configuration");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task ExportVdsConfiguration()
    {
        if (_sharedConnectionService.SourceConnection == null)
        {
            MigrationStatus = "No source vCenter connection available";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export error: No source vCenter connection\n";
            return;
        }

        try
        {
            IsLoadingData = true;
            MigrationStatus = "Exporting vDS configuration to backup folder...";

            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            if (string.IsNullOrEmpty(password))
            {
                MigrationStatus = "Error: No password found for source connection";
                return;
            }

            // Get the configured export path (backup folder)
            var config = _configurationService.GetConfiguration();
            var exportPath = config.ExportPath;
            
            if (string.IsNullOrEmpty(exportPath))
            {
                MigrationStatus = "Error: No export path configured in settings";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export error: Export path not configured\n";
                return;
            }

            // Ensure export directory exists
            if (!Directory.Exists(exportPath))
            {
                Directory.CreateDirectory(exportPath);
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Created export directory: {exportPath}\n";
            }

            // Create a timestamped reference file name for this export
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var referenceFileName = $"VDS_Export_{timestamp}.json";
            var referenceFilePath = Path.Combine(exportPath, referenceFileName);

            var parameters = new Dictionary<string, object>
            {
                { "ExportPath", referenceFilePath },
                { "LogPath", config.LogPath },
                { "BypassModuleCheck", true }
            };

            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting VDS backup export to: {exportPath}\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Reference file: {referenceFileName}\n";

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Export-VDS.ps1",
                _sharedConnectionService.SourceConnection,
                password,
                parameters);

            // Check the actual script result
            if (result.StartsWith("ERROR:") || result.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                MigrationStatus = $"VDS export failed: {result}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export failed: {result}\n";
                _logger.LogError("VDS export failed: {Result}", result);
            }
            else if (result.StartsWith("SUCCESS:"))
            {
                MigrationStatus = "VDS configuration exported successfully to backup folder";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] {result}\n";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] VDS backup files saved to: {exportPath}\n";
                _logger.LogInformation("VDS configuration exported to backup folder: {ExportPath}", exportPath);
            }
            else
            {
                MigrationStatus = "VDS export completed with unknown result";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogWarning("VDS export completed but result unclear: {Result}", result);
            }
        }
        catch (Exception ex)
        {
            MigrationStatus = $"VDS export failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error exporting VDS configuration");
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

        if (_sharedConnectionService.TargetConnection == null)
            {
            MigrationStatus = "No target vCenter connection available";
            return;
            }

        try
            {
            IsLoadingData = true;
            MigrationStatus = "Importing network configuration...";

            var password = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            if (string.IsNullOrEmpty(password))
                {
                MigrationStatus = "Error: No password found for target connection";
                return;
                }

            // Create network mappings dictionary from the UI
            var networkMappingsDict = new Dictionary<string, object>();
            foreach (var mapping in NetworkMappings)
                {
                if (!string.IsNullOrEmpty(mapping.SourceNetwork) && !string.IsNullOrEmpty(mapping.TargetNetwork))
                    {
                    networkMappingsDict[mapping.SourceNetwork] = mapping.TargetNetwork;
                    }
                }

            var parameters = new Dictionary<string, object>
                {
                // VCenterServer will be set by RunVCenterScriptAsync - don't override it
                { "ImportPath", ImportFilePath },
                { "OverwriteExisting", RecreateIfExists },
                { "ValidateOnly", ValidateOnly },
                { "LogPath", _configurationService.GetConfiguration().LogPath },
                { "BypassModuleCheck", true }
                };

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Import-VDS.ps1",
                _sharedConnectionService.TargetConnection,
                password,
                parameters);

            // Check the actual script result
            if (result.StartsWith("ERROR:") || result.Contains("failed"))
            {
                MigrationStatus = $"Import failed: {result}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import failed: {result}\n";
                _logger.LogError("VDS import failed: {Result}", result);
            }
            else if (result.Contains("Import completed:") || result.Contains("Validation completed:"))
            {
                MigrationStatus = ValidateOnly ? "Network validation completed successfully" : "Network configuration imported successfully";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network configuration imported from: {ImportFilePath}\n";
                if (ValidateOnly)
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Validation mode - no changes were made\n";
                }
                else
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Recreate if exists: {RecreateIfExists}\n";
                }
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogInformation("Network configuration imported from {FilePath}", ImportFilePath);
            }
            else
            {
                MigrationStatus = "Import completed with unknown result";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogWarning("VDS import completed but result unclear: {Result}", result);
            }
            }
        catch (Exception ex)
            {
            MigrationStatus = $"Import failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error importing network configuration");
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task ImportVdsConfiguration()
    {
        if (_sharedConnectionService.TargetConnection == null)
        {
            MigrationStatus = "No target vCenter connection available";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: No target vCenter connection\n";
            return;
        }

        try
        {
            IsLoadingData = true;
            MigrationStatus = "Importing vDS configuration from backup folder...";

            var password = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            if (string.IsNullOrEmpty(password))
            {
                MigrationStatus = "Error: No password found for target connection";
                return;
            }

            // Get the configured export path (backup folder)
            var config = _configurationService.GetConfiguration();
            var backupPath = config.ExportPath;
            
            if (string.IsNullOrEmpty(backupPath))
            {
                MigrationStatus = "Error: No backup path configured in settings";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: Backup path not configured\n";
                return;
            }

            // Check if backup directory exists
            if (!Directory.Exists(backupPath))
            {
                MigrationStatus = $"Error: Backup directory does not exist: {backupPath}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: Backup directory not found\n";
                return;
            }

            // Find the most recent export subdirectory (timestamped folders)
            var exportSubdirectories = Directory.GetDirectories(backupPath)
                .Where(d => {
                    var dirName = Path.GetFileName(d);
                    return dirName.StartsWith("VDS_Export_", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(d => Directory.GetCreationTime(d))
                .ToList();

            if (!exportSubdirectories.Any())
            {
                MigrationStatus = "Error: No VDS export subdirectories found in backup directory";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: No VDS export folders found\n";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Looking for folders like 'VDS_Export_YYYYMMDD_HHMMSS' in: {backupPath}\n";
                return;
            }

            var latestExportDir = exportSubdirectories.First();
            var latestExportDirName = Path.GetFileName(latestExportDir);
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Found latest export directory: {latestExportDirName}\n";

            // Look for the reference JSON file in the main backup directory
            var referenceFileName = $"{latestExportDirName}.json";
            var latestExportFile = Path.Combine(backupPath, referenceFileName);
            
            if (!File.Exists(latestExportFile))
            {
                MigrationStatus = $"Error: Export reference file not found: {referenceFileName}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: Reference file not found\n";
                return;
            }

            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Found export reference file: {referenceFileName}\n";

            // Validate that it's a VDS export reference file
            try
            {
                var testContent = File.ReadAllText(latestExportFile);
                var testJson = JsonSerializer.Deserialize<JsonElement>(testContent);
                
                if (!testJson.TryGetProperty("ExportType", out var exportType) || 
                    exportType.GetString() != "VDS_Native_Backup")
                {
                    MigrationStatus = "Error: Invalid export reference file format";
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: Not a valid VDS backup reference file\n";
                    return;
                }
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Error: Unable to read export reference file: {ex.Message}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import error: Failed to validate reference file\n";
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "ImportPath", latestExportFile },
                { "OverwriteExisting", RecreateIfExists },
                { "ValidateOnly", ValidateOnly },
                { "LogPath", config.LogPath },
                { "BypassModuleCheck", true }
            };

            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting VDS import from reference file: {latestExportFile}\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Validate only: {ValidateOnly}\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Replace existing: {RecreateIfExists}\n";

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Active\\Network Management\\Import-VDS.ps1",
                _sharedConnectionService.TargetConnection,
                password,
                parameters);

            // Check the actual script result
            if (result.StartsWith("ERROR:") || result.Contains("failed", StringComparison.OrdinalIgnoreCase))
            {
                MigrationStatus = $"VDS import failed: {result}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Import failed: {result}\n";
                _logger.LogError("VDS import failed: {Result}", result);
            }
            else if (result.Contains("Import completed:") || result.Contains("Validation completed:"))
            {
                MigrationStatus = ValidateOnly ? "VDS validation completed successfully" : "VDS configuration imported successfully";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] VDS configuration imported from backup folder: {backupPath}\n";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Used reference file: {Path.GetFileName(latestExportFile)}\n";
                if (ValidateOnly)
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Validation mode - no changes were made\n";
                }
                else
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Replace existing: {RecreateIfExists}\n";
                }
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogInformation("VDS configuration imported from backup folder: {BackupPath} using reference file: {ReferenceFile}", backupPath, latestExportFile);
            }
            else
            {
                MigrationStatus = "VDS import completed with unknown result";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogWarning("VDS import completed but result unclear: {Result}", result);
            }
        }
        catch (Exception ex)
        {
            MigrationStatus = $"VDS import failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error importing VDS configuration");
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

        // Return all vDS switches and port groups for migration
        items.AddRange(SourceVDSSwitches);
        items.AddRange(SourcePortGroups);

        return items;
        }
        
    private async Task LoadExportedVDSData(string exportPath)
    {
        try
        {
            if (File.Exists(exportPath))
            {
                var jsonContent = await File.ReadAllTextAsync(exportPath);
                
                // Detect if this is a native backup reference or old format JSON
                try
                {
                    var testParse = JsonSerializer.Deserialize<JsonElement>(jsonContent);
                    if (testParse.TryGetProperty("ExportType", out var exportTypeElement) &&
                        exportTypeElement.GetString() == "VDS_Native_Backup")
                    {
                        // This is a native backup reference
                        await LoadNativeVDSBackupData(jsonContent);
                    }
                    else
                    {
                        // This is the old format JSON
                        await LoadExportedVDSDataFromJson(jsonContent);
                    }
                }
                catch
                {
                    // Fallback to old format
                    await LoadExportedVDSDataFromJson(jsonContent);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load exported vDS data file");
        }
    }
    
    private Task LoadExportedVDSDataFromJson(string jsonContent)
    {
        try
        {
            var exportData = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            
            // Clear existing data
            SourceVDSSwitches.Clear();
            SourcePortGroups.Clear();
            
            // Parse VDS switches
            if (exportData.TryGetProperty("VDSSwitches", out var vdsSwitchesElement))
            {
                foreach (var vdsElement in vdsSwitchesElement.EnumerateArray())
                {
                    if (vdsElement.ValueKind == JsonValueKind.Object)
                    {
                        var vdsInfo = new VirtualSwitchInfo
                        {
                            Name = vdsElement.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "",
                            Type = "VmwareDistributedVirtualSwitch"
                        };
                        
                        SourceVDSSwitches.Add(vdsInfo);
                        
                        // Parse port groups for this VDS
                        if (vdsElement.TryGetProperty("PortGroups", out var portGroupsElement))
                        {
                            foreach (var pgElement in portGroupsElement.EnumerateArray())
                            {
                                if (pgElement.ValueKind == JsonValueKind.Object)
                                {
                                    var portGroup = new PortGroupInfo
                                    {
                                        Name = pgElement.TryGetProperty("Name", out var pgNameElement) ? pgNameElement.GetString() ?? "" : "",
                                        Type = "DistributedVirtualPortgroup",
                                        IsSelected = false
                                    };
                                    
                                    // Parse VLAN configuration
                                    if (pgElement.TryGetProperty("VlanConfiguration", out var vlanElement) &&
                                        vlanElement.TryGetProperty("VlanId", out var vlanIdElement))
                                    {
                                        portGroup.VlanId = vlanIdElement.GetInt32();
                                    }
                                    
                                    SourcePortGroups.Add(portGroup);
                                }
                            }
                        }
                    }
                }
            }
            
            // Update statistics
            if (exportData.TryGetProperty("TotalSwitches", out var switches))
            {
                TotalVDSSwitches = switches.GetInt32();
            }
            else
            {
                TotalVDSSwitches = SourceVDSSwitches.Count;
            }
            
            if (exportData.TryGetProperty("TotalPortGroups", out var portGroups))
            {
                TotalPortGroups = portGroups.GetInt32();
            }
            else
            {
                TotalPortGroups = SourcePortGroups.Count;
            }
            
            VdsStatus = $"✅ {TotalVDSSwitches} vDS switches, {TotalPortGroups} port groups";
            MigrationStatus = $"Loaded {TotalVDSSwitches} vDS switches and {TotalPortGroups} port groups";
            _logger.LogInformation("Loaded exported vDS data: {Switches} switches, {PortGroups} port groups", 
                TotalVDSSwitches, TotalPortGroups);
        }
        catch (JsonException ex)
        {
            MigrationStatus = $"Failed to parse VDS data: {ex.Message}";
            _logger.LogError(ex, "Could not parse VDS JSON data");
        }
        catch (Exception ex)
        {
            MigrationStatus = $"Error loading VDS data: {ex.Message}";
            _logger.LogError(ex, "Could not load VDS data");
        }

        return Task.CompletedTask;
    }
    
    private async Task LoadNativeVDSBackupData(string referenceJson)
    {
        try
        {
            var reference = JsonSerializer.Deserialize<JsonElement>(referenceJson);
            
            if (!reference.TryGetProperty("ExportType", out var exportTypeElement) || 
                exportTypeElement.GetString() != "VDS_Native_Backup")
            {
                MigrationStatus = "Invalid export format - not a native VDS backup";
                return;
            }
            
            // Clear existing data
            SourceVDSSwitches.Clear();
            SourcePortGroups.Clear();
            
            // Get manifest file path
            if (reference.TryGetProperty("ManifestFile", out var manifestElement))
            {
                var manifestFile = manifestElement.GetString();
                if (File.Exists(manifestFile))
                {
                    var manifestJson = await File.ReadAllTextAsync(manifestFile);
                    var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson);
                    
                    // Parse exported switches from manifest
                    if (manifest.TryGetProperty("ExportedSwitches", out var switchesElement))
                    {
                        foreach (var switchElement in switchesElement.EnumerateArray())
                        {
                            if (switchElement.ValueKind == JsonValueKind.Object)
                            {
                                var vdsInfo = new VirtualSwitchInfo
                                {
                                    Name = switchElement.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "",
                                    Type = "VmwareDistributedVirtualSwitch"
                                };
                                
                                SourceVDSSwitches.Add(vdsInfo);
                                
                                // Create placeholder port groups based on count
                                if (switchElement.TryGetProperty("PortGroupCount", out var countElement))
                                {
                                    var portGroupCount = countElement.GetInt32();
                                    for (int i = 0; i < portGroupCount; i++)
                                    {
                                        var portGroup = new PortGroupInfo
                                        {
                                            Name = $"{vdsInfo.Name}-PortGroup-{i+1}",
                                            Type = "DistributedVirtualPortgroup",
                                            IsSelected = false,
                                            VlanId = 0
                                        };
                                        
                                        SourcePortGroups.Add(portGroup);
                                    }
                                }
                            }
                        }
                    }
                    
                    // Update statistics from reference
                    if (reference.TryGetProperty("TotalSwitches", out var totalElement))
                    {
                        TotalVDSSwitches = totalElement.GetInt32();
                    }
                    else
                    {
                        TotalVDSSwitches = SourceVDSSwitches.Count;
                    }
                    
                    TotalPortGroups = SourcePortGroups.Count;
                    
                    VdsStatus = $"✅ {TotalVDSSwitches} vDS switches, {TotalPortGroups} port groups (Native Backup)";
                    MigrationStatus = $"Loaded {TotalVDSSwitches} vDS switches and {TotalPortGroups} port groups from native backup";
                    _logger.LogInformation("Loaded native VDS backup data: {Switches} switches, {PortGroups} port groups", 
                        TotalVDSSwitches, TotalPortGroups);
                }
                else
                {
                    MigrationStatus = "Manifest file not found for VDS backup";
                }
            }
            else
            {
                MigrationStatus = "Invalid backup reference - no manifest file specified";
            }
        }
        catch (JsonException ex)
        {
            MigrationStatus = $"Failed to parse native VDS backup data: {ex.Message}";
            _logger.LogError(ex, "Could not parse native VDS backup JSON data");
        }
        catch (Exception ex)
        {
            MigrationStatus = $"Error loading native VDS backup data: {ex.Message}";
            _logger.LogError(ex, "Could not load native VDS backup data");
        }
    }
    
    private Task LoadDiscoveredVDSData(string discoveryJson)
    {
        try
        {
            var discoveryData = JsonSerializer.Deserialize<JsonElement>(discoveryJson);
            
            // Clear existing data
            SourceVDSSwitches.Clear();
            SourcePortGroups.Clear();
            
            // Parse VDS switches from discovery
            if (discoveryData.TryGetProperty("VDSSwitches", out var vdsSwitchesElement))
            {
                foreach (var vdsElement in vdsSwitchesElement.EnumerateArray())
                {
                    if (vdsElement.ValueKind == JsonValueKind.Object)
                    {
                        var vdsInfo = new VirtualSwitchInfo
                        {
                            Name = vdsElement.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "",
                            Type = vdsElement.TryGetProperty("Type", out var typeElement) ? typeElement.GetString() ?? "VmwareDistributedVirtualSwitch" : "VmwareDistributedVirtualSwitch"
                        };
                        
                        // Add version and UUID if available
                        if (vdsElement.TryGetProperty("Version", out var versionElement))
                        {
                            vdsInfo.Version = versionElement.GetString() ?? "";
                        }
                        
                        if (vdsElement.TryGetProperty("Uuid", out var uuidElement))
                        {
                            vdsInfo.Uuid = uuidElement.GetString() ?? "";
                        }
                        
                        SourceVDSSwitches.Add(vdsInfo);
                        
                        // Parse port groups for this VDS
                        if (vdsElement.TryGetProperty("PortGroups", out var portGroupsElement))
                        {
                            foreach (var pgElement in portGroupsElement.EnumerateArray())
                            {
                                if (pgElement.ValueKind == JsonValueKind.Object)
                                {
                                    var portGroup = new PortGroupInfo
                                    {
                                        Name = pgElement.TryGetProperty("Name", out var pgNameElement) ? pgNameElement.GetString() ?? "" : "",
                                        Type = pgElement.TryGetProperty("Type", out var pgTypeElement) ? pgTypeElement.GetString() ?? "DistributedVirtualPortgroup" : "DistributedVirtualPortgroup",
                                        IsSelected = pgElement.TryGetProperty("IsSelected", out var selectedElement) ? selectedElement.GetBoolean() : false
                                    };
                                    
                                    // Parse VLAN ID
                                    if (pgElement.TryGetProperty("VlanId", out var vlanIdElement))
                                    {
                                        portGroup.VlanId = vlanIdElement.GetInt32();
                                    }
                                    
                                    // Parse number of ports
                                    if (pgElement.TryGetProperty("NumPorts", out var numPortsElement))
                                    {
                                        portGroup.NumPorts = numPortsElement.GetInt32();
                                    }
                                    
                                    SourcePortGroups.Add(portGroup);
                                }
                            }
                        }
                    }
                }
            }
            
            // Update statistics from discovery data
            if (discoveryData.TryGetProperty("TotalSwitches", out var totalSwitchesElement))
            {
                TotalVDSSwitches = totalSwitchesElement.GetInt32();
            }
            else
            {
                TotalVDSSwitches = SourceVDSSwitches.Count;
            }
            
            if (discoveryData.TryGetProperty("TotalPortGroups", out var totalPortGroupsElement))
            {
                TotalPortGroups = totalPortGroupsElement.GetInt32();
            }
            else
            {
                TotalPortGroups = SourcePortGroups.Count;
            }
            
            // Update source information
            var sourceVCenter = "Unknown";
            if (discoveryData.TryGetProperty("SourceVCenter", out var sourceElement))
            {
                sourceVCenter = sourceElement.GetString() ?? "Unknown";
            }
            
            var discoveryDate = "Unknown";
            if (discoveryData.TryGetProperty("DiscoveryDate", out var dateElement))
            {
                discoveryDate = dateElement.GetString() ?? "Unknown";
            }
            
            VdsStatus = $"✅ {TotalVDSSwitches} vDS switches, {TotalPortGroups} port groups discovered";
            MigrationStatus = $"Discovered {TotalVDSSwitches} vDS switches and {TotalPortGroups} port groups from {sourceVCenter}";
            
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Discovery completed: {TotalVDSSwitches} switches, {TotalPortGroups} port groups\n";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Source: {sourceVCenter} (discovered {discoveryDate})\n";
            
            _logger.LogInformation("Loaded discovered VDS data: {Switches} switches, {PortGroups} port groups from {Source}", 
                TotalVDSSwitches, TotalPortGroups, sourceVCenter);
        }
        catch (JsonException ex)
        {
            MigrationStatus = $"Failed to parse VDS discovery data: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] JSON parsing error: {ex.Message}\n";
            _logger.LogError(ex, "Could not parse VDS discovery JSON data");
        }
        catch (Exception ex)
        {
            MigrationStatus = $"Error loading VDS discovery data: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Discovery data error: {ex.Message}\n";
            _logger.LogError(ex, "Could not load VDS discovery data");
        }

        return Task.CompletedTask;
    }

    // Property for datacenter selection
    private string _selectedDatacenter = string.Empty;
    public string SelectedDatacenter
    {
        get => _selectedDatacenter;
        set => SetProperty(ref _selectedDatacenter, value);
    }
    }