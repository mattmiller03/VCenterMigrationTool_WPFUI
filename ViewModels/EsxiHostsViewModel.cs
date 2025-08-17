using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Generic;

namespace VCenterMigrationTool.ViewModels;

public partial class EsxiHostsViewModel : ObservableObject
    {
    private readonly PersistentExternalConnectionService _persistentConnectionService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ILogger<EsxiHostsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _sourceClusters = new();

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedSourceCluster;

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private ObservableCollection<EsxiHost> _selectedSourceHosts = new();

    [ObservableProperty]
    private ObservableCollection<EsxiHost> _availableTargetHosts = new();

    [ObservableProperty]
    private string _migrationStatus = "Ready to migrate hosts";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingMessage = "";

    [ObservableProperty]
    private string _sourceConnectionStatus = "Not connected";

    [ObservableProperty]
    private string _targetConnectionStatus = "Not connected";

    public EsxiHostsViewModel (
        PersistentExternalConnectionService persistentConnectionService,
        SharedConnectionService sharedConnectionService,
        ILogger<EsxiHostsViewModel> logger)
        {
        _persistentConnectionService = persistentConnectionService;
        _sharedConnectionService = sharedConnectionService;
        _logger = logger;
        }

    /// <summary>
    /// Initialize the view model and load data
    /// </summary>
    public async Task InitializeAsync ()
        {
        await CheckConnectionsAndLoadData();
        }

    /// <summary>
    /// Check connections and load cluster/host data
    /// </summary>
    private async Task CheckConnectionsAndLoadData ()
        {
        IsLoading = true;
        LoadingMessage = "Checking connections...";

        try
            {
            // Check source connection
            var sourceConnected = await _persistentConnectionService.IsConnectedAsync("source");
            if (sourceConnected && _sharedConnectionService.SourceConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("source");
                SourceConnectionStatus = $"✅ {_sharedConnectionService.SourceConnection.ServerAddress}";
                _logger.LogInformation("Source vCenter connected: {Server}", _sharedConnectionService.SourceConnection.ServerAddress);
                }
            else
                {
                SourceConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Source vCenter not connected");
                }

            // Check target connection
            var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");
            if (targetConnected && _sharedConnectionService.TargetConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("target");
                TargetConnectionStatus = $"✅ {_sharedConnectionService.TargetConnection.ServerAddress}";
                _logger.LogInformation("Target vCenter connected: {Server}", _sharedConnectionService.TargetConnection.ServerAddress);
                }
            else
                {
                TargetConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Target vCenter not connected");
                }

            // Load data if both connections are active
            if (sourceConnected && targetConnected)
                {
                await LoadClustersAndHosts();
                }
            else
                {
                MigrationStatus = "⚠️ Please connect to both vCenters from the Dashboard";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error checking connections");
            MigrationStatus = $"Error: {ex.Message}";
            }
        finally
            {
            IsLoading = false;
            LoadingMessage = "";
            }
        }

    /// <summary>
    /// Load clusters and hosts from both vCenters
    /// </summary>
    private async Task LoadClustersAndHosts ()
        {
        LoadingMessage = "Loading clusters and hosts...";

        try
            {
            // Load source clusters and hosts
            _logger.LogInformation("Loading source clusters and hosts...");
            var sourceClustersTask = LoadSourceClusters();

            // Load target clusters and hosts
            _logger.LogInformation("Loading target clusters and hosts...");
            var targetClustersTask = LoadTargetClusters();

            await Task.WhenAll(sourceClustersTask, targetClustersTask);

            MigrationStatus = $"Loaded {SourceClusters.Count} source clusters and {TargetClusters.Count} target clusters";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading clusters and hosts");
            MigrationStatus = $"Error loading data: {ex.Message}";
            }
        }

    /// <summary>
    /// Load source clusters and their hosts
    /// </summary>
    private async Task LoadSourceClusters ()
        {
        try
            {
            var script = @"
                $clusters = Get-Cluster -ErrorAction SilentlyContinue
                $result = @()
                
                foreach ($cluster in $clusters) {
                    $hosts = Get-VMHost -Location $cluster -ErrorAction SilentlyContinue
                    
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = $hosts.Count
                        TotalCpuGhz = [math]::Round(($hosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($hosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $hosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $clusterInfo.Hosts += $hostInfo
                    }
                    
                    $result += $clusterInfo
                }
                
                # Also get standalone hosts (not in any cluster)
                $standaloneHosts = Get-VMHost -ErrorAction SilentlyContinue | Where-Object { $_.Parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Cluster] }
                
                if ($standaloneHosts.Count -gt 0) {
                    $standaloneCluster = @{
                        Name = 'Standalone Hosts'
                        Id = 'standalone'
                        HostCount = $standaloneHosts.Count
                        TotalCpuGhz = [math]::Round(($standaloneHosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($standaloneHosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $standaloneHosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $standaloneCluster.Hosts += $hostInfo
                    }
                    
                    $result += $standaloneCluster
                }
                
                $result | ConvertTo-Json -Depth 10
            ";

            var result = await _persistentConnectionService.ExecuteCommandAsync("source", script);

            if (result.StartsWith("ERROR:"))
                {
                _logger.LogError("Failed to load source clusters: {Error}", result);
                return;
                }

            // Parse the JSON result
            var clusters = JsonSerializer.Deserialize<List<dynamic>>(result);

            SourceClusters.Clear();

            if (clusters != null)
                {
                foreach (var cluster in clusters)
                    {
                    var clusterInfo = new ClusterInfo
                        {
                        Name = cluster.GetProperty("Name").GetString(),
                        Id = cluster.GetProperty("Id").GetString(),
                        HostCount = cluster.GetProperty("HostCount").GetInt32(),
                        TotalCpuGhz = cluster.GetProperty("TotalCpuGhz").GetDouble(),
                        TotalMemoryGB = cluster.GetProperty("TotalMemoryGB").GetDouble()
                        };

                    var hosts = cluster.GetProperty("Hosts").EnumerateArray();
                    foreach (var host in hosts)
                        {
                        var esxiHost = new EsxiHost
                            {
                            Name = host.GetProperty("Name").GetString(),
                            Id = host.GetProperty("Id").GetString(),
                            ClusterName = clusterInfo.Name,
                            ConnectionState = host.GetProperty("ConnectionState").GetString(),
                            PowerState = host.GetProperty("PowerState").GetString(),
                            CpuCores = host.GetProperty("CpuCores").GetInt32(),
                            CpuMhz = host.GetProperty("CpuMhz").GetInt32(),
                            MemoryGB = host.GetProperty("MemoryGB").GetDouble(),
                            Version = host.GetProperty("Version").GetString(),
                            Build = host.GetProperty("Build").GetString(),
                            Model = host.GetProperty("Model").GetString(),
                            Vendor = host.GetProperty("Vendor").GetString(),
                            VmCount = host.GetProperty("VMs").GetInt32()
                            };
                        clusterInfo.Hosts.Add(esxiHost);
                        }

                    SourceClusters.Add(clusterInfo);
                    }

                _logger.LogInformation("Loaded {Count} source clusters", SourceClusters.Count);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading source clusters");
            }
        }

    /// <summary>
    /// Load target clusters and their hosts
    /// </summary>
    private async Task LoadTargetClusters ()
        {
        try
            {
            // Use the same script as source but on target connection
            var script = @"
                $clusters = Get-Cluster -ErrorAction SilentlyContinue
                $result = @()
                
                foreach ($cluster in $clusters) {
                    $hosts = Get-VMHost -Location $cluster -ErrorAction SilentlyContinue
                    
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = $hosts.Count
                        TotalCpuGhz = [math]::Round(($hosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($hosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $hosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $clusterInfo.Hosts += $hostInfo
                    }
                    
                    $result += $clusterInfo
                }
                
                # Also get standalone hosts
                $standaloneHosts = Get-VMHost -ErrorAction SilentlyContinue | Where-Object { $_.Parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Cluster] }
                
                if ($standaloneHosts.Count -gt 0) {
                    $standaloneCluster = @{
                        Name = 'Standalone Hosts'
                        Id = 'standalone'
                        HostCount = $standaloneHosts.Count
                        TotalCpuGhz = [math]::Round(($standaloneHosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($standaloneHosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $standaloneHosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $standaloneCluster.Hosts += $hostInfo
                    }
                    
                    $result += $standaloneCluster
                }
                
                $result | ConvertTo-Json -Depth 10
            ";

            var result = await _persistentConnectionService.ExecuteCommandAsync("target", script);

            if (result.StartsWith("ERROR:"))
                {
                _logger.LogError("Failed to load target clusters: {Error}", result);
                return;
                }

            // Parse the JSON result
            var clusters = JsonSerializer.Deserialize<List<dynamic>>(result);

            TargetClusters.Clear();

            if (clusters != null)
                {
                foreach (var cluster in clusters)
                    {
                    var clusterInfo = new ClusterInfo
                        {
                        Name = cluster.GetProperty("Name").GetString(),
                        Id = cluster.GetProperty("Id").GetString(),
                        HostCount = cluster.GetProperty("HostCount").GetInt32(),
                        TotalCpuGhz = cluster.GetProperty("TotalCpuGhz").GetDouble(),
                        TotalMemoryGB = cluster.GetProperty("TotalMemoryGB").GetDouble()
                        };

                    var hosts = cluster.GetProperty("Hosts").EnumerateArray();
                    foreach (var host in hosts)
                        {
                        var esxiHost = new EsxiHost
                            {
                            Name = host.GetProperty("Name").GetString(),
                            Id = host.GetProperty("Id").GetString(),
                            ClusterName = clusterInfo.Name,
                            ConnectionState = host.GetProperty("ConnectionState").GetString(),
                            PowerState = host.GetProperty("PowerState").GetString(),
                            CpuCores = host.GetProperty("CpuCores").GetInt32(),
                            CpuMhz = host.GetProperty("CpuMhz").GetInt32(),
                            MemoryGB = host.GetProperty("MemoryGB").GetDouble(),
                            Version = host.GetProperty("Version").GetString(),
                            Build = host.GetProperty("Build").GetString(),
                            Model = host.GetProperty("Model").GetString(),
                            Vendor = host.GetProperty("Vendor").GetString(),
                            VmCount = host.GetProperty("VMs").GetInt32()
                            };
                        clusterInfo.Hosts.Add(esxiHost);
                        }

                    TargetClusters.Add(clusterInfo);
                    }

                _logger.LogInformation("Loaded {Count} target clusters", TargetClusters.Count);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading target clusters");
            }
        }

    [RelayCommand]
    private async Task RefreshData ()
        {
        await CheckConnectionsAndLoadData();
        }

    [RelayCommand]
    private void SelectAllSourceHosts ()
        {
        if (SelectedSourceCluster != null)
            {
            SelectedSourceHosts.Clear();
            foreach (var host in SelectedSourceCluster.Hosts)
                {
                SelectedSourceHosts.Add(host);
                }
            }
        }

    [RelayCommand]
    private void ClearSourceHostSelection ()
        {
        SelectedSourceHosts.Clear();
        }

    [RelayCommand]
    private async Task MigrateSelectedHosts ()
        {
        if (SelectedSourceHosts.Count == 0)
            {
            MigrationStatus = "⚠️ Please select hosts to migrate";
            return;
            }

        if (SelectedTargetCluster == null)
            {
            MigrationStatus = "⚠️ Please select a target cluster";
            return;
            }

        IsLoading = true;
        LoadingMessage = $"Migrating {SelectedSourceHosts.Count} hosts to {SelectedTargetCluster.Name}...";

        try
            {
            foreach (var host in SelectedSourceHosts)
                {
                LoadingMessage = $"Migrating {host.Name}...";

                // Build migration script
                var migrateScript = $@"
                    $sourceHost = Get-VMHost -Name '{host.Name}' -ErrorAction Stop
                    
                    # Put host in maintenance mode
                    Write-Output 'Entering maintenance mode...'
                    Set-VMHost -VMHost $sourceHost -State Maintenance -Evacuate:$true -Confirm:$false
                    
                    # Disconnect from source vCenter
                    Write-Output 'Disconnecting from source vCenter...'
                    Disconnect-VIServer -Server $sourceHost -Confirm:$false
                    
                    Write-Output 'Host ready for migration to target vCenter'
                    'SUCCESS'
                ";

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", migrateScript);

                if (result.Contains("SUCCESS"))
                    {
                    _logger.LogInformation("Host {Host} prepared for migration", host.Name);

                    // Now add to target cluster
                    var addScript = $@"
                        $targetCluster = Get-Cluster -Name '{SelectedTargetCluster.Name}' -ErrorAction Stop
                        
                        # Add host to target cluster
                        Write-Output 'Adding host to target cluster...'
                        Add-VMHost -Name '{host.Name}' -Location $targetCluster -User 'root' -Password 'YourPassword' -Force -Confirm:$false
                        
                        Write-Output 'Host successfully migrated'
                        'SUCCESS'
                    ";

                    // Note: You'll need to handle host credentials properly here
                    result = await _persistentConnectionService.ExecuteCommandAsync("target", addScript);

                    if (result.Contains("SUCCESS"))
                        {
                        _logger.LogInformation("Host {Host} successfully migrated to {Cluster}",
                            host.Name, SelectedTargetCluster.Name);
                        }
                    }
                }

            MigrationStatus = $"✅ Successfully migrated {SelectedSourceHosts.Count} hosts";

            // Refresh the data
            await RefreshData();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during host migration");
            MigrationStatus = $"❌ Migration failed: {ex.Message}";
            }
        finally
            {
            IsLoading = false;
            LoadingMessage = "";
            }
        }

    partial void OnSelectedSourceClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _logger.LogInformation("Selected source cluster: {Cluster} with {Count} hosts",
                value.Name, value.Hosts.Count);
            }
        SelectedSourceHosts.Clear();
        }

    partial void OnSelectedTargetClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _logger.LogInformation("Selected target cluster: {Cluster}", value.Name);

            // Update available hosts for display
            AvailableTargetHosts.Clear();
            foreach (var host in value.Hosts)
                {
                AvailableTargetHosts.Add(host);
                }
            }
        }
    }