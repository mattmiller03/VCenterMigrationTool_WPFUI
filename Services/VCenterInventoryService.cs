using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Service for managing vCenter inventory cache and providing fast access to all vCenter objects
/// </summary>
public class VCenterInventoryService
{
    private readonly PersistentExternalConnectionService _persistentConnectionService;
    private readonly ILogger<VCenterInventoryService> _logger;
    
    private readonly Dictionary<string, VCenterInventory> _inventoryCache = new();
    private readonly object _cacheLock = new();

    public VCenterInventoryService(
        PersistentExternalConnectionService persistentConnectionService, 
        ILogger<VCenterInventoryService> logger)
    {
        _persistentConnectionService = persistentConnectionService;
        _logger = logger;
    }

    /// <summary>
    /// Event fired when inventory is updated
    /// </summary>
    public event EventHandler<InventoryUpdatedEventArgs>? InventoryUpdated;

    /// <summary>
    /// Get cached inventory for a vCenter, or null if not cached
    /// </summary>
    public VCenterInventory? GetCachedInventory(string vCenterName)
    {
        lock (_cacheLock)
        {
            return _inventoryCache.TryGetValue(vCenterName, out var inventory) ? inventory : null;
        }
    }

    /// <summary>
    /// Load complete inventory for a vCenter and cache it
    /// </summary>
    public async Task<VCenterInventory> LoadInventoryAsync(string vCenterName, string username, string password, string connectionType = "source")
    {
        _logger.LogInformation("Loading complete inventory for vCenter: {VCenterName} (connection: {ConnectionType})", vCenterName, connectionType);

        try
        {
            var inventory = new VCenterInventory
            {
                VCenterName = vCenterName,
                LastUpdated = DateTime.Now
            };

            // Get vCenter version and basic info
            inventory.VCenterVersion = await GetVCenterVersionAsync(vCenterName, username, password, connectionType);

            // Load all inventory components sequentially to avoid stream concurrency issues
            // The PersistentExternalConnectionService uses a single StreamWriter that cannot handle parallel access
            _logger.LogInformation("Loading inventory components sequentially for {VCenterName}", vCenterName);
            
            await LoadDatacentersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadClustersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadHostsAsync(vCenterName, username, password, inventory, connectionType);
            await LoadDatastoresAsync(vCenterName, username, password, inventory, connectionType);
            await LoadVirtualMachinesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadResourcePoolsAsync(vCenterName, username, password, inventory, connectionType);
            await LoadVirtualSwitchesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadFoldersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadTagsAndCategoriesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadRolesAndPermissionsAsync(vCenterName, username, password, inventory, connectionType);
            await LoadCustomAttributesAsync(vCenterName, username, password, inventory, connectionType);

            // Cache the inventory
            lock (_cacheLock)
            {
                _inventoryCache[vCenterName] = inventory;
            }

            // Notify subscribers
            InventoryUpdated?.Invoke(this, new InventoryUpdatedEventArgs(vCenterName, inventory));

            _logger.LogInformation("Successfully loaded inventory for {VCenterName}: {Stats}", 
                vCenterName, GetInventorySummary(inventory));

            return inventory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load inventory for vCenter: {VCenterName}", vCenterName);
            throw;
        }
    }

    /// <summary>
    /// Refresh inventory for a vCenter (reload from source)
    /// </summary>
    public async Task<VCenterInventory> RefreshInventoryAsync(string vCenterName, string username, string password, string connectionType = "source")
    {
        _logger.LogInformation("Refreshing inventory for vCenter: {VCenterName} (connection: {ConnectionType})", vCenterName, connectionType);
        
        // Remove from cache first
        lock (_cacheLock)
        {
            _inventoryCache.Remove(vCenterName);
        }

        return await LoadInventoryAsync(vCenterName, username, password, connectionType);
    }

    /// <summary>
    /// Clear cached inventory for a vCenter (called on disconnect)
    /// </summary>
    public void ClearInventory(string vCenterName)
    {
        lock (_cacheLock)
        {
            if (_inventoryCache.Remove(vCenterName))
            {
                _logger.LogInformation("Cleared inventory cache for vCenter: {VCenterName}", vCenterName);
            }
        }
    }

    /// <summary>
    /// Load infrastructure inventory only (datacenters, clusters, hosts, datastores)
    /// </summary>
    public async Task<VCenterInventory> LoadInfrastructureInventoryAsync(string vCenterName, string username, string password, string connectionType = "source")
    {
        _logger.LogInformation("Loading infrastructure inventory for vCenter: {VCenterName} (connection: {ConnectionType})", vCenterName, connectionType);

        try
        {
            var inventory = new VCenterInventory
            {
                VCenterName = vCenterName,
                LastUpdated = DateTime.Now
            };

            // Get vCenter version and basic info
            inventory.VCenterVersion = await GetVCenterVersionAsync(vCenterName, username, password, connectionType);

            // Load infrastructure components only
            _logger.LogInformation("Loading infrastructure components for {VCenterName}", vCenterName);
            
            await LoadDatacentersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadClustersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadHostsAsync(vCenterName, username, password, inventory, connectionType);
            await LoadDatastoresAsync(vCenterName, username, password, inventory, connectionType);

            // Cache the inventory
            lock (_cacheLock)
            {
                _inventoryCache[vCenterName] = inventory;
            }

            // Notify subscribers
            InventoryUpdated?.Invoke(this, new InventoryUpdatedEventArgs(vCenterName, inventory));

            _logger.LogInformation("Successfully loaded infrastructure inventory for {VCenterName}: {Stats}", 
                vCenterName, GetInventorySummary(inventory));

            return inventory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load infrastructure inventory for vCenter: {VCenterName}", vCenterName);
            throw;
        }
    }

    /// <summary>
    /// Load virtual machines inventory only
    /// </summary>
    public async Task LoadVirtualMachinesInventoryAsync(string vCenterName, string username, string password, string connectionType = "source")
    {
        _logger.LogInformation("Loading VM inventory for vCenter: {VCenterName} (connection: {ConnectionType})", vCenterName, connectionType);

        try
        {
            // Get or create existing inventory
            VCenterInventory inventory;
            lock (_cacheLock)
            {
                if (!_inventoryCache.TryGetValue(vCenterName, out inventory!))
                {
                    inventory = new VCenterInventory
                    {
                        VCenterName = vCenterName,
                        LastUpdated = DateTime.Now
                    };
                    _inventoryCache[vCenterName] = inventory;
                }
            }

            // Load VMs and Resource Pools
            await LoadVirtualMachinesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadResourcePoolsAsync(vCenterName, username, password, inventory, connectionType);

            // Update timestamp
            inventory.LastUpdated = DateTime.Now;

            // Notify subscribers
            InventoryUpdated?.Invoke(this, new InventoryUpdatedEventArgs(vCenterName, inventory));

            _logger.LogInformation("Successfully loaded VM inventory for {VCenterName}: {VMCount} VMs", 
                vCenterName, inventory.VirtualMachines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VM inventory for vCenter: {VCenterName}", vCenterName);
            throw;
        }
    }

    /// <summary>
    /// Load administrative configuration inventory only
    /// </summary>
    public async Task LoadAdminConfigInventoryAsync(string vCenterName, string username, string password, string connectionType = "source")
    {
        _logger.LogInformation("Loading admin config inventory for vCenter: {VCenterName} (connection: {ConnectionType})", vCenterName, connectionType);

        try
        {
            // Get or create existing inventory
            VCenterInventory inventory;
            lock (_cacheLock)
            {
                if (!_inventoryCache.TryGetValue(vCenterName, out inventory!))
                {
                    inventory = new VCenterInventory
                    {
                        VCenterName = vCenterName,
                        LastUpdated = DateTime.Now
                    };
                    _inventoryCache[vCenterName] = inventory;
                }
            }

            // Load admin configuration components
            await LoadVirtualSwitchesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadFoldersAsync(vCenterName, username, password, inventory, connectionType);
            await LoadTagsAndCategoriesAsync(vCenterName, username, password, inventory, connectionType);
            await LoadRolesAndPermissionsAsync(vCenterName, username, password, inventory, connectionType);
            await LoadCustomAttributesAsync(vCenterName, username, password, inventory, connectionType);

            // Update timestamp
            inventory.LastUpdated = DateTime.Now;

            // Notify subscribers
            InventoryUpdated?.Invoke(this, new InventoryUpdatedEventArgs(vCenterName, inventory));

            _logger.LogInformation("Successfully loaded admin config inventory for {VCenterName}", vCenterName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load admin config inventory for vCenter: {VCenterName}", vCenterName);
            throw;
        }
    }

    /// <summary>
    /// Get all cached vCenter names
    /// </summary>
    public List<string> GetCachedVCenterNames()
    {
        lock (_cacheLock)
        {
            return new List<string>(_inventoryCache.Keys);
        }
    }

    private async Task<string> GetVCenterVersionAsync(string vCenterName, string username, string password, string connectionType)
    {
        var script = @"
            $global:DefaultVIServers | Select-Object -First 1 | Select-Object -ExpandProperty Version
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            return result?.Trim() ?? "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get vCenter version for {VCenterName}", vCenterName);
            return "Unknown";
        }
    }

    private async Task LoadDatacentersAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # OPTIMIZED: Use simple SearchRoot approach for speed
            $datacenters = Get-View -ViewType Datacenter | ForEach-Object {
                $dc = $_
                
                # Use SearchRoot for fast counting within each datacenter
                $clusterCount = [int](Get-View -ViewType ClusterComputeResource -SearchRoot $dc.MoRef | Measure-Object).Count
                $hostCount = [int](Get-View -ViewType HostSystem -SearchRoot $dc.MoRef | Measure-Object).Count  
                $vmCount = [int](Get-View -ViewType VirtualMachine -SearchRoot $dc.MoRef | Measure-Object).Count
                $datastoreCount = [int](Get-View -ViewType Datastore -SearchRoot $dc.MoRef | Measure-Object).Count
                
                [PSCustomObject]@{
                    Name = $dc.Name
                    Id = $dc.MoRef.Value
                    ClusterCount = $clusterCount
                    HostCount = $hostCount  
                    VmCount = $vmCount
                    DatastoreCount = $datastoreCount
                }
            }
            $datacenters | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                // Check if result looks like JSON before attempting to deserialize
                if (result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"))
                {
                    var datacenters = JsonSerializer.Deserialize<DatacenterInfo[]>(result);
                    if (datacenters != null)
                    {
                        inventory.Datacenters.AddRange(datacenters);
                        _logger.LogInformation("Loaded {Count} datacenters for {VCenterName}", datacenters.Length, vCenterName);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid JSON response for datacenters from {VCenterName}: {Response}", vCenterName, result);
                }
            }
            else
            {
                _logger.LogWarning("Empty response for datacenters from {VCenterName}", vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load datacenters for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadClustersAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # OPTIMIZED: Get clusters with bulk host data for performance
            $clusters = Get-View -ViewType ClusterComputeResource
            $allHosts = Get-View -ViewType HostSystem
            
            $clusterData = $clusters | ForEach-Object {
                $cluster = $_
                $datacenter = Get-View $cluster.Parent -ErrorAction SilentlyContinue
                
                # Fast counts from cluster object properties
                $hostCount = [int]$cluster.Host.Count
                $datastoreCount = [int]$cluster.Datastore.Count
                
                # Get VM count efficiently
                $vmCount = [int](Get-View -ViewType VirtualMachine -SearchRoot $cluster.MoRef | Measure-Object).Count
                
                # Calculate hardware totals from cluster's host array
                $totalCpuMhz = 0
                $totalMemoryMB = 0
                
                if ($cluster.Host.Count -gt 0) {
                    # Get only hosts that belong to this cluster for efficiency
                    $clusterHosts = $allHosts | Where-Object { $cluster.Host -contains $_.MoRef }
                    foreach ($hostView in $clusterHosts) {
                        try {
                            $totalCpuMhz += [int]($hostView.Hardware.CpuInfo.Hz * $hostView.Hardware.CpuInfo.NumCpuCores / 1000000)
                            $totalMemoryMB += [int]($hostView.Hardware.MemorySize / 1024 / 1024)
                        } catch {
                            # Skip if host data unavailable
                        }
                    }
                }
                
                [PSCustomObject]@{
                    Name = $cluster.Name
                    Id = $cluster.MoRef.Value
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { """" }
                    FullName = if ($datacenter) { ""$($datacenter.Name)/$($cluster.Name)"" } else { $cluster.Name }
                    HostCount = $hostCount
                    VmCount = $vmCount
                    DatastoreCount = $datastoreCount
                    TotalCpuGhz = [math]::Round($totalCpuMhz / 1000, 2)
                    TotalMemoryGB = [math]::Round($totalMemoryMB / 1024, 2)
                    HAEnabled = if ($cluster.Configuration.DasConfig) { $cluster.Configuration.DasConfig.Enabled } else { $false }
                    DrsEnabled = if ($cluster.Configuration.DrsConfig) { $cluster.Configuration.DrsConfig.Enabled } else { $false }
                    EVCMode = if ($cluster.Summary.CurrentEVCModeKey) { $cluster.Summary.CurrentEVCModeKey } else { """" }
                }
            }
            $clusterData | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                // Check if result looks like JSON before attempting to deserialize
                if (result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"))
                {
                    var clusters = JsonSerializer.Deserialize<ClusterInfo[]>(result);
                    if (clusters != null)
                    {
                        inventory.Clusters.AddRange(clusters);
                        _logger.LogInformation("Loaded {Count} clusters for {VCenterName}", clusters.Length, vCenterName);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid JSON response for clusters from {VCenterName}: {Response}", vCenterName, result);
                }
            }
            else
            {
                _logger.LogWarning("Empty response for clusters from {VCenterName}", vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load clusters for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadHostsAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # OPTIMIZED: Get hosts with minimal parent lookups and no VM counting
            $esxiHosts = Get-View -ViewType HostSystem
            $clusters = Get-View -ViewType ClusterComputeResource
            $datacenters = Get-View -ViewType Datacenter
            
            $hostData = $esxiHosts | ForEach-Object {
                $esxiHost = $_
                
                # Find parent cluster efficiently
                $cluster = $clusters | Where-Object { $_.Host -contains $esxiHost.MoRef } | Select-Object -First 1
                
                # Find datacenter efficiently  
                $datacenter = if ($cluster) {
                    $datacenters | Where-Object { $_.MoRef.Value -eq $cluster.Parent.Value } | Select-Object -First 1
                } else {
                    $datacenters | Where-Object { $_.MoRef.Value -eq $esxiHost.Parent.Value } | Select-Object -First 1
                }
                
                # Skip expensive VM count for now - can be calculated later if needed
                
                [PSCustomObject]@{
                    Name = $esxiHost.Name
                    Id = $esxiHost.MoRef.Value
                    ClusterName = if ($cluster) { $cluster.Name } else { """" }
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { """" }
                    Version = if ($esxiHost.Config.Product) { $esxiHost.Config.Product.Version } else { ""Unknown"" }
                    Build = if ($esxiHost.Config.Product) { $esxiHost.Config.Product.Build } else { ""Unknown"" }
                    ConnectionState = $esxiHost.Runtime.ConnectionState.ToString()
                    PowerState = $esxiHost.Runtime.PowerState.ToString()
                    CpuCores = [int]$esxiHost.Hardware.CpuInfo.NumCpuCores
                    CpuMhz = [int]($esxiHost.Hardware.CpuInfo.Hz / 1000000)
                    MemoryGB = [math]::Round($esxiHost.Hardware.MemorySize / 1024 / 1024 / 1024, 2)
                    VmCount = 0  # Skip expensive VM counting for performance
                }
            }
            $hostData | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                var hosts = JsonSerializer.Deserialize<EsxiHost[]>(result);
                if (hosts != null)
                {
                    inventory.Hosts.AddRange(hosts);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hosts for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadDatastoresAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # OPTIMIZED: Get datastores without expensive VM counting
            $datastores = Get-View -ViewType Datastore
            $allHosts = Get-View -ViewType HostSystem
            
            $datastoreData = $datastores | ForEach-Object {
                $datastore = $_
                
                # Get connected hosts efficiently without individual Get-View calls
                $connectedHosts = @()
                if ($datastore.Host) {
                    foreach ($hostMount in $datastore.Host) {
                        $hostView = $allHosts | Where-Object { $_.MoRef.Value -eq $hostMount.Key.Value } | Select-Object -First 1
                        if ($hostView) {
                            $connectedHosts += $hostView.Name
                        }
                    }
                }
                
                # Skip expensive VM counting for performance
                
                [PSCustomObject]@{
                    Name = $datastore.Name
                    Id = $datastore.MoRef.Value
                    Type = $datastore.Summary.Type
                    CapacityGB = [math]::Round($datastore.Summary.Capacity / 1024 / 1024 / 1024, 2)
                    UsedGB = [math]::Round(($datastore.Summary.Capacity - $datastore.Summary.FreeSpace) / 1024 / 1024 / 1024, 2)
                    VmCount = 0  # Skip expensive VM counting for performance
                    ConnectedHosts = $connectedHosts
                }
            }
            $datastoreData | ConvertTo-Json -Depth 3
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                var datastores = JsonSerializer.Deserialize<DatastoreInfo[]>(result);
                if (datastores != null)
                {
                    inventory.Datastores.AddRange(datastores);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load datastores for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadVirtualMachinesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # Use vSphere API for much faster VM data collection
            $vms = Get-View -ViewType VirtualMachine | ForEach-Object {
                $vm = $_
                
                # Get parent objects using API relationships (much faster)
                $vmHost = if ($vm.Runtime.Host) { Get-View $vm.Runtime.Host } else { $null }
                $cluster = if ($vmHost -and $vmHost.Parent) { 
                    $parent = Get-View $vmHost.Parent
                    if ($parent.GetType().Name -eq ""ClusterComputeResource"") { $parent } else { $null }
                } else { $null }
                $datacenter = if ($cluster) { Get-View $cluster.Parent } else { 
                    if ($vmHost) { 
                        $hostParent = Get-View $vmHost.Parent
                        if ($hostParent.GetType().Name -eq ""Datacenter"") { $hostParent } else { Get-View $hostParent.Parent }
                    } else { $null }
                }
                $resourcePool = if ($vm.ResourcePool) { Get-View $vm.ResourcePool } else { $null }
                $folder = if ($vm.Parent) { Get-View $vm.Parent } else { $null }
                
                # Calculate total disk space from API data
                $totalDiskGB = 0
                foreach ($device in $vm.Config.Hardware.Device) {
                    if ($device.GetType().Name -eq ""VirtualDisk"") {
                        $totalDiskGB += $device.CapacityInKB / 1024 / 1024
                    }
                }
                
                [PSCustomObject]@{
                    Name = $vm.Name
                    Id = $vm.MoRef.Value
                    PowerState = $vm.Runtime.PowerState.ToString()
                    GuestOS = if ($vm.Config.GuestFullName) { $vm.Config.GuestFullName } else { """" }
                    CpuCount = [int]$vm.Config.Hardware.NumCPU
                    MemoryGB = [math]::Round($vm.Config.Hardware.MemoryMB / 1024, 2)
                    DiskGB = [math]::Round($totalDiskGB, 2)
                    HostName = if ($vmHost) { $vmHost.Name } else { """" }
                    ClusterName = if ($cluster) { $cluster.Name } else { """" }
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { """" }
                    FolderPath = if ($folder) { $folder.Name } else { """" }
                    ResourcePoolName = if ($resourcePool) { $resourcePool.Name } else { """" }
                    Tags = @()
                }
            }
            $vms | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                var vms = JsonSerializer.Deserialize<VirtualMachineInfo[]>(result);
                if (vms != null)
                {
                    inventory.VirtualMachines.AddRange(vms);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load virtual machines for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadResourcePoolsAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # OPTIMIZED: Use vSphere API for fast resource pool collection
            $resourcePools = Get-View -ViewType ResourcePool | Where-Object { $_.Name -ne 'Resources' } | ForEach-Object {
                $rp = $_
                
                # Find parent cluster efficiently using API
                $cluster = $null
                $datacenter = $null
                
                try {
                    # Check if parent is a cluster
                    if ($rp.Parent.Type -eq 'ClusterComputeResource') {
                        $cluster = Get-View $rp.Parent -ErrorAction SilentlyContinue
                        if ($cluster) {
                            $datacenter = Get-View $cluster.Parent -ErrorAction SilentlyContinue
                        }
                    }
                    # If not directly under cluster, might be under host system
                    elseif ($rp.Parent.Type -eq 'ComputeResource') {
                        $computeResource = Get-View $rp.Parent -ErrorAction SilentlyContinue
                        if ($computeResource) {
                            $datacenter = Get-View $computeResource.Parent -ErrorAction SilentlyContinue
                        }
                    }
                } catch {
                    # Continue without parent info if lookup fails
                }
                
                # Get resource limits safely
                $cpuLimit = 0
                $memoryLimit = 0
                if ($rp.Config -and $rp.Config.CpuAllocation -and $rp.Config.CpuAllocation.Limit -and $rp.Config.CpuAllocation.Limit -ne -1) {
                    $cpuLimit = [int]$rp.Config.CpuAllocation.Limit
                }
                if ($rp.Config -and $rp.Config.MemoryAllocation -and $rp.Config.MemoryAllocation.Limit -and $rp.Config.MemoryAllocation.Limit -ne -1) {
                    $memoryLimit = [int]($rp.Config.MemoryAllocation.Limit / 1024 / 1024)  # Convert to MB
                }
                
                [PSCustomObject]@{
                    Name = $rp.Name
                    Id = $rp.MoRef.Value
                    ClusterName = if ($cluster) { $cluster.Name } else { """" }
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { """" }
                    ParentPath = if ($rp.Parent) { $rp.Parent.Value } else { """" }
                    CpuLimitMhz = $cpuLimit
                    MemoryLimitMB = $memoryLimit
                    VmCount = [int]$rp.Vm.Count  # Fast count from API object property
                }
            }
            $resourcePools | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                var resourcePools = JsonSerializer.Deserialize<ResourcePoolInventoryInfo[]>(result);
                if (resourcePools != null)
                {
                    inventory.ResourcePools.AddRange(resourcePools);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load resource pools for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadVirtualSwitchesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        // Placeholder - virtual switch discovery is complex and optional for MVP
        await Task.CompletedTask;
    }

    private async Task LoadFoldersAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        var script = @"
            # Get all VM folders (not including root 'vm' folders)
            $folders = Get-Folder -Type VM | Where-Object { 
                $_.Name -ne 'vm' -and 
                $_.Name -ne 'Datacenters' -and
                $_.ParentId -ne $null
            } | ForEach-Object {
                $folder = $_
                
                # Get parent datacenter
                $datacenter = $null
                try {
                    $parent = $folder.Parent
                    while ($parent -and !$datacenter) {
                        if ($parent.GetType().Name -like '*Datacenter*') {
                            $datacenter = $parent
                            break
                        }
                        # Check if parent is root folder
                        if ($parent.Name -eq 'Datacenters' -or !$parent.Parent) {
                            break
                        }
                        $parent = $parent.Parent
                    }
                } catch {
                    # If we can't traverse parents, continue without datacenter
                }
                
                # Build folder path
                $path = $folder.Name
                $currentFolder = $folder
                while ($currentFolder.Parent -and $currentFolder.Parent.Name -ne 'vm' -and $currentFolder.Parent.Name -ne 'Datacenters') {
                    $currentFolder = $currentFolder.Parent
                    $path = $currentFolder.Name + '/' + $path
                }
                
                [PSCustomObject]@{
                    Name = $folder.Name
                    Id = $folder.Id
                    Type = 'VM'
                    Path = $path
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { '' }
                    ChildItemCount = ($folder | Get-ChildItem -ErrorAction SilentlyContinue | Measure-Object).Count
                }
            }
            $folders | ConvertTo-Json -Depth 2
        ";

        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                // Check if result looks like JSON before attempting to deserialize
                if (result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"))
                {
                    var folders = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(result);
                    if (folders != null && folders.Count > 0)
                    {
                        // Add folders to inventory (you may need to add a Folders property to VCenterInventory if not present)
                        _logger.LogInformation("Loaded {Count} folders for {VCenterName}", folders.Count, vCenterName);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid JSON response for folders from {VCenterName}: {Response}", vCenterName, result);
                }
            }
            else
            {
                _logger.LogWarning("Empty response for folders from {VCenterName}", vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load folders for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadTagsAndCategoriesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        // Placeholder - tag discovery requires REST API calls, optional for MVP
        await Task.CompletedTask;
    }

    private async Task LoadRolesAndPermissionsAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        try
        {
            _logger.LogInformation("Loading roles and permissions for {VCenterName} using SSO Admin script", vCenterName);

            // Execute the SSO Admin config script using direct PowerShell execution
            var scriptContent = await BuildSSOAdminScriptAsync(vCenterName);
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, scriptContent);

            if (result.StartsWith("SUCCESS:") || result.TrimStart().StartsWith("{"))
            {
                // Parse the JSON output from the script
                var jsonStart = result.IndexOf('{');
                if (jsonStart >= 0)
                {
                    var jsonContent = result.Substring(jsonStart);
                    var ssoData = JsonSerializer.Deserialize<SSOAdminConfigData>(jsonContent);
                    
                    if (ssoData != null)
                    {
                        // Process roles
                        if (ssoData.Roles != null)
                        {
                            foreach (var role in ssoData.Roles)
                            {
                                var roleInfo = new RoleInfo
                                {
                                    Name = role.Name ?? "",
                                    Id = role.Id ?? "",
                                    IsSystem = role.IsSystem,
                                    Privileges = role.Privileges ?? Array.Empty<string>(),
                                    AssignmentCount = role.AssignmentCount
                                };
                                inventory.Roles.Add(roleInfo);
                            }
                        }

                        // Process permissions
                        if (ssoData.Permissions != null)
                        {
                            foreach (var perm in ssoData.Permissions)
                            {
                                var permissionInfo = new PermissionInfo
                                {
                                    Id = perm.Id ?? Guid.NewGuid().ToString(),
                                    Principal = perm.Principal ?? "",
                                    RoleName = perm.Role ?? "",
                                    EntityName = perm.Entity ?? "",
                                    EntityType = perm.EntityType ?? "",
                                    Propagate = perm.Propagate
                                };
                                inventory.Permissions.Add(permissionInfo);
                            }
                        }

                        // Process global permissions
                        if (ssoData.GlobalPermissions != null)
                        {
                            foreach (var globalPerm in ssoData.GlobalPermissions)
                            {
                                var permissionInfo = new PermissionInfo
                                {
                                    Id = globalPerm.Id ?? Guid.NewGuid().ToString(),
                                    Principal = globalPerm.Principal ?? "",
                                    RoleName = globalPerm.Role ?? "",
                                    EntityName = "Root",
                                    EntityType = "Root",
                                    Propagate = globalPerm.Propagate
                                };
                                inventory.Permissions.Add(permissionInfo);
                            }
                        }

                        _logger.LogInformation("Successfully loaded {RoleCount} roles and {PermissionCount} permissions for {VCenterName}", 
                            inventory.Roles.Count, inventory.Permissions.Count, vCenterName);
                    }
                }
            }
            else if (result.StartsWith("ERROR:"))
            {
                _logger.LogWarning("SSO Admin script returned error for {VCenterName}: {Error}", vCenterName, result);
                // Fallback to basic role/permission discovery
                await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
            }
            else
            {
                _logger.LogWarning("Unexpected result from SSO Admin script for {VCenterName}: {Result}", vCenterName, result);
                // Fallback to basic role/permission discovery
                await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roles and permissions using SSO Admin script for {VCenterName}", vCenterName);
            // Fallback to basic role/permission discovery
            await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
        }
    }

    private async Task LoadBasicRolesAndPermissionsAsync(string vCenterName, VCenterInventory inventory, string connectionType)
    {
        try
        {
            _logger.LogInformation("Loading basic roles and permissions for {VCenterName} as fallback", vCenterName);
            
            // Basic roles discovery script
            var rolesScript = @"
                $roles = Get-VIRole | Where-Object { $_.IsSystem -eq $false }
                $roleData = $roles | ForEach-Object {
                    $assignmentCount = 0
                    try {
                        $assignments = Get-VIPermission | Where-Object { $_.Role -eq $_.Name }
                        $assignmentCount = @($assignments).Count
                    } catch {}
                    
                    @{
                        Name = if ($_.Name -and $_.Name -ne $null) { $_.Name.ToString() } else { """" }
                        Id = if ($_.Id -and $_.Id -ne $null) { $_.Id.ToString() } else { """" }
                        Description = if ($_.Description -and $_.Description -ne $null) { $_.Description.ToString() } else { """" }
                        IsSystem = if ($_.IsSystem -ne $null) { [bool]$_.IsSystem } else { $false }
                        Privileges = @($_.PrivilegeList | Where-Object { $_ -ne $null } | ForEach-Object { $_.ToString() })
                        AssignmentCount = [int]$assignmentCount
                    }
                }
                $roleData | ConvertTo-Json -Depth 3
            ";

            var roleResult = await _persistentConnectionService.ExecuteCommandAsync(connectionType, rolesScript);
            if (!string.IsNullOrEmpty(roleResult))
            {
                var roles = JsonSerializer.Deserialize<BasicRoleData[]>(roleResult);
                if (roles != null)
                {
                    foreach (var role in roles)
                    {
                        var roleInfo = new RoleInfo
                        {
                            Name = role.Name ?? "",
                            Id = role.Id ?? "",
                            IsSystem = role.IsSystem,
                            Privileges = role.Privileges ?? Array.Empty<string>(),
                            AssignmentCount = role.AssignmentCount
                        };
                        inventory.Roles.Add(roleInfo);
                    }
                }
            }

            // Basic permissions discovery script
            var permissionsScript = @"
                $permissions = Get-VIPermission
                $permData = $permissions | ForEach-Object {
                    @{
                        Id = [System.Guid]::NewGuid().ToString()
                        Principal = if ($_.Principal -and $_.Principal -ne $null) { $_.Principal.ToString() } else { """" }
                        PrincipalType = if ($_.IsGroup) { ""Group"" } else { ""User"" }
                        Role = if ($_.Role -and $_.Role -ne $null) { $_.Role.ToString() } else { """" }
                        Entity = if ($_.Entity -and $_.Entity.Name -and $_.Entity.Name -ne $null) { $_.Entity.Name.ToString() } else { ""Root"" }
                        EntityType = if ($_.Entity -and $_.Entity -ne $null) { $_.Entity.GetType().Name.ToString() } else { ""Root"" }
                        EntityId = if ($_.Entity -and $_.Entity.Id -and $_.Entity.Id -ne $null) { $_.Entity.Id.ToString() } else { ""Root"" }
                        Propagate = if ($_.Propagate -ne $null) { [bool]$_.Propagate } else { $false }
                        Type = ""Permission""
                    }
                }
                $permData | ConvertTo-Json -Depth 2
            ";

            var permResult = await _persistentConnectionService.ExecuteCommandAsync(connectionType, permissionsScript);
            if (!string.IsNullOrEmpty(permResult))
            {
                var permissions = JsonSerializer.Deserialize<BasicPermissionData[]>(permResult);
                if (permissions != null)
                {
                    foreach (var perm in permissions)
                    {
                        var permissionInfo = new PermissionInfo
                        {
                            Id = perm.Id ?? Guid.NewGuid().ToString(),
                            Principal = perm.Principal ?? "",
                            RoleName = perm.Role ?? "",
                            EntityName = perm.Entity ?? "",
                            EntityType = perm.EntityType ?? "",
                            Propagate = perm.Propagate
                        };
                        inventory.Permissions.Add(permissionInfo);
                    }
                }
            }

            _logger.LogInformation("Loaded {RoleCount} basic roles and {PermissionCount} basic permissions for {VCenterName}", 
                inventory.Roles.Count, inventory.Permissions.Count, vCenterName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load basic roles and permissions for {VCenterName}", vCenterName);
        }
    }

    private async Task<string> BuildSSOAdminScriptAsync(string vCenterServer)
    {
        var scriptContent = $@"
# SSO Admin Config Discovery Script (Embedded)
param()

try {{
    # Check for and import SSO Admin module if available
    $ssoModuleAvailable = $false
    try {{
        $ssoModule = Get-Module -ListAvailable -Name VMware.vSphere.SsoAdmin
        if ($ssoModule) {{
            Import-Module VMware.vSphere.SsoAdmin -Force -ErrorAction Stop
            $ssoModuleAvailable = $true
            Write-Host ""SSO Admin module imported successfully""
        }} else {{
            Write-Host ""VMware.vSphere.SsoAdmin module not found - SSO data will be limited""
        }}
    }} catch {{
        Write-Host ""Could not import SSO Admin module: $($_.Exception.Message)""
    }}

    # Initialize result data
    $ssoData = @{{
        CollectionDate = Get-Date -Format ""yyyy-MM-dd HH:mm:ss""
        VCenterServer = ""{vCenterServer}""
        Roles = @()
        Permissions = @()
        GlobalPermissions = @()
        TotalRoles = 0
        TotalPermissions = 0
    }}

    # Get Roles (including SSO roles if available)
    Write-Host ""Retrieving roles...""
    $viRoles = Get-VIRole -ErrorAction SilentlyContinue
    foreach ($role in $viRoles) {{
        $assignmentCount = 0
        try {{
            $assignments = Get-VIPermission | Where-Object {{ $_.Role -eq $role.Name }}
            $assignmentCount = @($assignments).Count
        }} catch {{
            Write-Host ""Could not count assignments for role '$($role.Name)'""
        }}
        
        $roleInfo = @{{
            Name = if ($role.Name -and $role.Name -ne $null) {{ $role.Name.ToString() }} else {{ """" }}
            Id = if ($role.Id -and $role.Id -ne $null) {{ $role.Id.ToString() }} else {{ """" }}
            IsSystem = if ($role.IsSystem -ne $null) {{ [bool]$role.IsSystem }} else {{ $false }}
            Description = if ($role.Description -and $role.Description -ne $null) {{ $role.Description.ToString() }} else {{ """" }}
            Privileges = @($role.PrivilegeList | Where-Object {{ $_ -ne $null }} | ForEach-Object {{ $_.ToString() }})
            AssignmentCount = [int]$assignmentCount
            Type = ""VIRole""
        }}
        
        $ssoData.Roles += $roleInfo
    }}
    
    $ssoData.TotalRoles = $ssoData.Roles.Count
    Write-Host ""Found $($ssoData.TotalRoles) roles""

    # Get Permissions (including global permissions)
    Write-Host ""Retrieving permissions...""
    $viPermissions = Get-VIPermission -ErrorAction SilentlyContinue
    foreach ($perm in $viPermissions) {{
        $permInfo = @{{
            Id = [System.Guid]::NewGuid().ToString()
            Principal = if ($perm.Principal -and $perm.Principal -ne $null) {{ $perm.Principal.ToString() }} else {{ """" }}
            PrincipalType = if ($perm.IsGroup) {{ ""Group"" }} else {{ ""User"" }}
            Role = if ($perm.Role -and $perm.Role -ne $null) {{ $perm.Role.ToString() }} else {{ """" }}
            Entity = if ($perm.Entity -and $perm.Entity.Name -and $perm.Entity.Name -ne $null) {{ $perm.Entity.Name.ToString() }} else {{ ""Root"" }}
            EntityType = if ($perm.Entity -and $perm.Entity -ne $null) {{ $perm.Entity.GetType().Name.ToString() }} else {{ ""Root"" }}
            EntityId = if ($perm.Entity -and $perm.Entity.Id -and $perm.Entity.Id -ne $null) {{ $perm.Entity.Id.ToString() }} else {{ ""Root"" }}
            Propagate = if ($perm.Propagate -ne $null) {{ [bool]$perm.Propagate }} else {{ $false }}
            Type = ""VIPermission""
        }}
        
        $ssoData.Permissions += $permInfo
    }}

    # Get Global Permissions using Get-VIPermission with root folder
    try {{
        Write-Host ""Retrieving global permissions...""
        $rootFolder = Get-Folder -NoRecursion -ErrorAction SilentlyContinue
        $globalPerms = Get-VIPermission -Entity $rootFolder -ErrorAction SilentlyContinue
        
        foreach ($globalPerm in $globalPerms) {{
            $globalPermInfo = @{{
                Id = [System.Guid]::NewGuid().ToString()
                Principal = if ($globalPerm.Principal -and $globalPerm.Principal -ne $null) {{ $globalPerm.Principal.ToString() }} else {{ """" }}
                PrincipalType = if ($globalPerm.IsGroup) {{ ""Group"" }} else {{ ""User"" }}
                Role = if ($globalPerm.Role -and $globalPerm.Role -ne $null) {{ $globalPerm.Role.ToString() }} else {{ """" }}
                Propagate = if ($globalPerm.Propagate -ne $null) {{ [bool]$globalPerm.Propagate }} else {{ $false }}
                Type = ""GlobalPermission""
            }}
            
            $ssoData.GlobalPermissions += $globalPermInfo
        }}
        
        Write-Host ""Found $($ssoData.GlobalPermissions.Count) global permissions""
    }} catch {{
        Write-Host ""Could not retrieve global permissions: $($_.Exception.Message)""
    }}

    $ssoData.TotalPermissions = $ssoData.Permissions.Count + $ssoData.GlobalPermissions.Count
    Write-Host ""Found $($ssoData.TotalPermissions) total permissions""

    # Output result as JSON
    $jsonOutput = $ssoData | ConvertTo-Json -Depth 10
    Write-Output $jsonOutput

}} catch {{
    Write-Output ""ERROR: $($_.Exception.Message)""
}}
";

        return await Task.FromResult(scriptContent);
    }

    private async Task LoadCustomAttributesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        // Placeholder - custom attributes discovery is optional for MVP
        await Task.CompletedTask;
    }

    private string GetInventorySummary(VCenterInventory inventory)
    {
        var stats = inventory.Statistics;
        return $"{stats.DatacenterCount} DCs, {stats.ClusterCount} clusters, {stats.HostCount} hosts, {stats.VirtualMachineCount} VMs, {stats.DatastoreCount} datastores";
    }

    /// <summary>
    /// Execute PowerShell script and safely deserialize JSON response
    /// </summary>
    private async Task<T[]?> ExecuteAndDeserializeAsync<T>(string script, string connectionType, string objectType, string vCenterName)
    {
        try
        {
            var result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            if (!string.IsNullOrEmpty(result))
            {
                // Check if result looks like JSON before attempting to deserialize
                if (result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"))
                {
                    var objects = JsonSerializer.Deserialize<T[]>(result);
                    if (objects != null)
                    {
                        _logger.LogInformation("Loaded {Count} {ObjectType} for {VCenterName}", objects.Length, objectType, vCenterName);
                        return objects;
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid JSON response for {ObjectType} from {VCenterName}: {Response}", objectType, vCenterName, result);
                }
            }
            else
            {
                _logger.LogWarning("Empty response for {ObjectType} from {VCenterName}", objectType, vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {ObjectType} for {VCenterName}", objectType, vCenterName);
        }

        return null;
    }
}

/// <summary>
/// Event args for inventory update notifications
/// </summary>
public class InventoryUpdatedEventArgs : EventArgs
{
    public string VCenterName { get; }
    public VCenterInventory Inventory { get; }

    public InventoryUpdatedEventArgs(string vCenterName, VCenterInventory inventory)
    {
        VCenterName = vCenterName;
        Inventory = inventory;
    }
}

/// <summary>
/// Data structures for deserializing SSO Admin Config script output
/// </summary>
public class SSOAdminConfigData
{
    public string? CollectionDate { get; set; }
    public string? VCenterServer { get; set; }
    public List<SSORoleData>? Roles { get; set; }
    public List<SSOPermissionData>? Permissions { get; set; }
    public List<SSOPermissionData>? GlobalPermissions { get; set; }
    public List<SSOUserData>? SSOUsers { get; set; }
    public List<SSOGroupData>? SSOGroups { get; set; }
    public int TotalRoles { get; set; }
    public int TotalPermissions { get; set; }
}

public class SSORoleData
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
    public string[]? Privileges { get; set; }
    public int AssignmentCount { get; set; }
    public string? Type { get; set; }
}

public class SSOPermissionData
{
    public string? Id { get; set; }
    public string? Principal { get; set; }
    public string? PrincipalType { get; set; }
    public string? Role { get; set; }
    public string? Entity { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public bool Propagate { get; set; }
    public string? Type { get; set; }
}

public class SSOUserData
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Description { get; set; }
    public bool Disabled { get; set; }
    public string? Type { get; set; }
}

public class SSOGroupData
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
}

/// <summary>
/// Basic role data for fallback scenarios
/// </summary>
public class BasicRoleData
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public string[]? Privileges { get; set; }
    public int AssignmentCount { get; set; }
}

/// <summary>
/// Basic permission data for fallback scenarios
/// </summary>
public class BasicPermissionData
{
    public string? Id { get; set; }
    public string? Principal { get; set; }
    public string? PrincipalType { get; set; }
    public string? Role { get; set; }
    public string? Entity { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public bool Propagate { get; set; }
    public string? Type { get; set; }
}