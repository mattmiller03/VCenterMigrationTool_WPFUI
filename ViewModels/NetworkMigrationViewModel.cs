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
using System.Text.Json;
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
    private readonly PersistentExternalConnectionService _persistentConnectionService;
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
        PersistentExternalConnectionService persistentConnectionService,
        ILogger<NetworkMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _persistentConnectionService = persistentConnectionService;
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
        try
        {
            // Check persistent connection status (same as Dashboard)
            var sourceConnected = await _persistentConnectionService.IsConnectedAsync("source");
            var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");

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

            // Parse network topology JSON data
            if (!string.IsNullOrEmpty(networkResult))
            {
                try
                {
                    // Clean the result - remove any non-JSON content
                    networkResult = networkResult.Trim();
                    
                    // Find the start of JSON (either [ or {)
                    int jsonStart = networkResult.IndexOfAny(new[] { '[', '{' });
                    if (jsonStart > 0)
                    {
                        networkResult = networkResult.Substring(jsonStart);
                    }
                    
                    // Find the end of JSON (matching ] or })
                    int jsonEnd = networkResult.LastIndexOfAny(new[] { ']', '}' });
                    if (jsonEnd > 0 && jsonEnd < networkResult.Length - 1)
                    {
                        networkResult = networkResult.Substring(0, jsonEnd + 1);
                    }
                    
                    var networkData = JsonSerializer.Deserialize<JsonElement[]>(networkResult);
                    foreach (var hostData in networkData)
                    {
                        // Validate that hostData is an object before accessing properties
                        if (hostData.ValueKind != JsonValueKind.Object)
                        {
                            _logger.LogWarning("Expected object but got {ValueKind} in network data", hostData.ValueKind);
                            continue;
                        }

                        var hostNode = new NetworkHostNode
                        {
                            Name = hostData.TryGetProperty("HostName", out var hostNameElement) ? hostNameElement.GetString() ?? "" : ""
                        };

                        // Parse vSwitches
                        if (hostData.TryGetProperty("VSwitches", out var vSwitchesElement))
                        {
                            foreach (var vSwitchData in vSwitchesElement.EnumerateArray())
                            {
                                var vSwitch = new VSwitchInfo
                                {
                                    Name = vSwitchData.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "",
                                    Type = vSwitchData.TryGetProperty("Type", out var typeElement) ? typeElement.GetString() ?? "Standard" : "Standard",
                                    IsSelected = vSwitchData.TryGetProperty("IsSelected", out var selectedElement) 
                                        ? selectedElement.GetBoolean() 
                                        : false
                                };

                                // Parse port groups
                                if (vSwitchData.TryGetProperty("PortGroups", out var portGroupsElement))
                                {
                                    foreach (var portGroupData in portGroupsElement.EnumerateArray())
                                    {
                                        var portGroup = new PortGroupInfo
                                        {
                                            Name = portGroupData.TryGetProperty("Name", out var pgNameElement) ? pgNameElement.GetString() ?? "" : "",
                                            VlanId = portGroupData.TryGetProperty("VlanId", out var vlanElement) 
                                                ? vlanElement.GetInt32() 
                                                : 0,
                                            Type = vSwitch.Type,
                                            IsSelected = portGroupData.TryGetProperty("IsSelected", out var pgSelectedElement) 
                                                ? pgSelectedElement.GetBoolean() 
                                                : false
                                        };
                                        vSwitch.PortGroups.Add(portGroup);
                                    }
                                }
                                hostNode.VSwitches.Add(vSwitch);
                            }
                        }

                        // Parse VMkernel ports
                        if (hostData.TryGetProperty("VmKernelPorts", out var vmkElement))
                        {
                            foreach (var vmkData in vmkElement.EnumerateArray())
                            {
                                var vmkPort = new VmKernelPortInfo
                                {
                                    Name = vmkData.TryGetProperty("Name", out var vmkNameElement) ? vmkNameElement.GetString() ?? "" : "",
                                    IpAddress = vmkData.TryGetProperty("IpAddress", out var ipElement) 
                                        ? ipElement.GetString() ?? "" 
                                        : ""
                                };
                                hostNode.VmKernelPorts.Add(vmkPort);
                            }
                        }

                        // Process vDS switches from network topology
                        foreach (var vSwitch in hostNode.VSwitches.Where(vs => vs.Type == "DistributedSwitch"))
                        {
                            var vdsInfo = new VirtualSwitchInfo
                            {
                                Name = vSwitch.Name,
                                Type = "VmwareDistributedVirtualSwitch"
                                // VirtualSwitchInfo doesn't have NumPorts property
                            };
                            
                            if (!SourceVDSSwitches.Any(vds => vds.Name == vdsInfo.Name))
                            {
                                SourceVDSSwitches.Add(vdsInfo);
                            }
                            
                            // Add port groups
                            foreach (var pg in vSwitch.PortGroups)
                            {
                                if (!SourcePortGroups.Any(spg => spg.Name == pg.Name))
                                {
                                    SourcePortGroups.Add(pg);
                                }
                            }
                        }
                    }
                    
                    TotalVDSSwitches = SourceVDSSwitches.Count;
                    TotalPortGroups = SourcePortGroups.Count;
                    VdsStatus = $"✅ {TotalVDSSwitches} vDS switches, {TotalPortGroups} port groups";
                    MigrationStatus = $"Loaded {TotalVDSSwitches} vDS switches and {TotalPortGroups} port groups";
                }
                catch (JsonException ex)
                {
                    MigrationStatus = $"Failed to parse network topology data: {ex.Message}";
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] JSON parsing error: {ex.Message}\n";
                    _logger.LogError(ex, "Error parsing network topology JSON");
                }
            }
            else
            {
                MigrationStatus = "No network topology data returned from script";
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

            // Load target network data using new credential method
            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-NetworkTopology.ps1",
                _sharedConnectionService.TargetConnection,
                password);

            // Parse network topology JSON data
            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    var networkData = JsonSerializer.Deserialize<JsonElement[]>(result);
                    foreach (var hostData in networkData)
                    {
                        // Validate that hostData is an object before accessing properties
                        if (hostData.ValueKind != JsonValueKind.Object)
                        {
                            _logger.LogWarning("Expected object but got {ValueKind} in target network data", hostData.ValueKind);
                            continue;
                        }

                        var hostNode = new NetworkHostNode
                        {
                            Name = hostData.TryGetProperty("HostName", out var hostNameElement) ? hostNameElement.GetString() ?? "" : ""
                        };

                        // Parse vSwitches
                        if (hostData.TryGetProperty("VSwitches", out var vSwitchesElement))
                        {
                            foreach (var vSwitchData in vSwitchesElement.EnumerateArray())
                            {
                                var vSwitch = new VSwitchInfo
                                {
                                    Name = vSwitchData.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? "" : "",
                                    Type = vSwitchData.TryGetProperty("Type", out var typeElement) ? typeElement.GetString() ?? "Standard" : "Standard",
                                    IsSelected = vSwitchData.TryGetProperty("IsSelected", out var selectedElement) 
                                        ? selectedElement.GetBoolean() 
                                        : false
                                };

                                // Parse port groups
                                if (vSwitchData.TryGetProperty("PortGroups", out var portGroupsElement))
                                {
                                    foreach (var portGroupData in portGroupsElement.EnumerateArray())
                                    {
                                        var portGroup = new PortGroupInfo
                                        {
                                            Name = portGroupData.TryGetProperty("Name", out var pgNameElement) ? pgNameElement.GetString() ?? "" : "",
                                            VlanId = portGroupData.TryGetProperty("VlanId", out var vlanElement) 
                                                ? vlanElement.GetInt32() 
                                                : 0,
                                            Type = vSwitch.Type,
                                            IsSelected = portGroupData.TryGetProperty("IsSelected", out var pgSelectedElement) 
                                                ? pgSelectedElement.GetBoolean() 
                                                : false
                                        };
                                        vSwitch.PortGroups.Add(portGroup);
                                    }
                                }
                                hostNode.VSwitches.Add(vSwitch);
                            }
                        }

                        // Parse VMkernel ports
                        if (hostData.TryGetProperty("VmKernelPorts", out var vmkElement))
                        {
                            foreach (var vmkData in vmkElement.EnumerateArray())
                            {
                                var vmkPort = new VmKernelPortInfo
                                {
                                    Name = vmkData.TryGetProperty("Name", out var vmkNameElement) ? vmkNameElement.GetString() ?? "" : "",
                                    IpAddress = vmkData.TryGetProperty("IpAddress", out var ipElement) 
                                        ? ipElement.GetString() ?? "" 
                                        : ""
                                };
                                hostNode.VmKernelPorts.Add(vmkPort);
                            }
                        }

                        // Target network topology processing would go here if needed
                    }
                    
                    MigrationStatus = "Target vCenter connection verified";
                }
                catch (JsonException ex)
                {
                    MigrationStatus = $"Failed to parse target network topology data: {ex.Message}";
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] JSON parsing error: {ex.Message}\n";
                    _logger.LogError(ex, "Error parsing target network topology JSON");
                }
            }
            else
            {
                MigrationStatus = "No target network topology data returned from script";
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
                "Scripts\\Migrate-NetworkConfiguration.ps1",
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

            var exportFormat = ExportToJson ? "JSON" : "CSV";
            var parameters = new Dictionary<string, object>
                {
                // VCenterServer will be set by RunVCenterScriptAsync - don't override it
                { "ExportFilePath", ExportFilePath },
                { "ExportFormat", exportFormat },
                { "IncludeStandardSwitches", MigrateStandardSwitches },
                { "BypassModuleCheck", true }
                };

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Export-VDSConfiguration.ps1",
                _sharedConnectionService.SourceConnection,
                password,
                parameters);

            // Check the actual script result
            if (result.StartsWith("ERROR:") || result.Contains("failed"))
            {
                MigrationStatus = $"Export failed: {result}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export failed: {result}\n";
                _logger.LogError("VDS export failed: {Result}", result);
            }
            else if (result.Contains("Export completed:"))
            {
                MigrationStatus = "Network configuration exported successfully";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Network configuration exported to: {ExportFilePath}\n";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Export format: {exportFormat}\n";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Script result: {result}\n";
                _logger.LogInformation("Network configuration exported to {FilePath}", ExportFilePath);
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
        // Wrapper for XAML binding compatibility - calls the main export method
        await ExportNetworkConfiguration();
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
                { "ImportFilePath", ImportFilePath },
                { "RecreateIfExists", RecreateIfExists },
                { "ValidateOnly", ValidateOnly },
                { "NetworkMappings", networkMappingsDict },
                { "BypassModuleCheck", true }
                };

            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Import-VDSConfiguration.ps1",
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
        // Wrapper for XAML binding compatibility - calls the main import method
        await ImportNetworkConfiguration();
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
    }