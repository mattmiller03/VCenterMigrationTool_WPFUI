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
                    var folderDictionaries = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(result);
                    if (folderDictionaries != null && folderDictionaries.Count > 0)
                    {
                        // Convert dictionaries to FolderInfo objects and add to inventory
                        foreach (var folderDict in folderDictionaries)
                        {
                            var folderInfo = new FolderInfo
                            {
                                Name = GetStringValueFromDict(folderDict, "Name", ""),
                                Id = GetStringValueFromDict(folderDict, "Id", ""),
                                Type = GetStringValueFromDict(folderDict, "Type", "VM"),
                                Path = GetStringValueFromDict(folderDict, "Path", ""),
                                DatacenterName = GetStringValueFromDict(folderDict, "DatacenterName", ""),
                                ChildCount = GetIntValueFromDict(folderDict, "ChildItemCount", 0)
                            };
                            inventory.Folders.Add(folderInfo);
                        }
                        _logger.LogInformation("Successfully loaded {Count} folders for {VCenterName}", inventory.Folders.Count, vCenterName);
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

    private async Task<string> BuildTagsAndCategoriesScriptWithLoggingAsync(string vCenterServer, string logPath)
    {
        var scriptContent = $@"
# Tags and Categories Discovery Script with Embedded Logging
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $false

function Write-LogInfo {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Info] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor White }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogSuccess {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Success] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Green }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogWarning {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Warning] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Yellow }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogError {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Error] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Red }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Start-ScriptLogging {{
    param([string]$ScriptName = '', [string]$LogPath = $null)
    
    if ($LogPath) {{
        if ([System.IO.Path]::HasExtension($LogPath)) {{
            $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
        }} else {{
            $logDir = $LogPath
        }}
        
        $psLogDir = Join-Path $logDir ""PowerShell""
        if (-not (Test-Path $psLogDir)) {{
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }}
        
        $timestamp = Get-Date -Format ""yyyyMMdd_HHmmss""
        $sessionId = [System.Guid]::NewGuid().ToString(""N"").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir ""${{ScriptName}}_${{timestamp}}_${{sessionId}}.log""
        
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        ""SCRIPT START: $ScriptName"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}

# Start logging
Start-ScriptLogging -ScriptName ""TagCategoryDiscovery"" -LogPath ""{logPath.Replace("\\", "\\\\")}""

Write-LogInfo ""Starting optimized tag and category discovery"" -Category ""Discovery""

try {{
    # Initialize result object
    $result = @{{
        CollectionDate = Get-Date -Format ""yyyy-MM-dd HH:mm:ss""
        VCenterServer = ""{vCenterServer}""
        Categories = @()
        Tags = @()
    }}
    
    # Get tag categories
    Write-LogInfo ""Retrieving tag categories..."" -Category ""Discovery""
    $allCategories = Get-TagCategory -ErrorAction SilentlyContinue
    
    if ($allCategories -and $allCategories.Count -gt 0) {{
        Write-LogInfo ""Found $($allCategories.Count) tag categories"" -Category ""Discovery""
        
        foreach ($category in $allCategories) {{
            Write-LogInfo ""Processing category: $($category.Name)"" -Category ""Discovery""
            
            # Count tags in this category
            $tagCount = 0
            try {{
                $categoryTags = Get-Tag -Category $category -ErrorAction SilentlyContinue
                $tagCount = if ($categoryTags) {{ @($categoryTags).Count }} else {{ 0 }}
            }} catch {{
                Write-LogWarning ""Could not count tags for category '$($category.Name)': $($_.Exception.Message)"" -Category ""Discovery""
            }}
            
            $categoryInfo = @{{
                Name = if ($category.Name) {{ $category.Name.ToString() }} else {{ """" }}
                Id = if ($category.Id) {{ $category.Id.ToString() }} else {{ """" }}
                Description = if ($category.Description) {{ $category.Description.ToString() }} else {{ """" }}
                TagCount = $tagCount
                IsMultipleCardinality = if ($category.Cardinality -eq ""Multiple"") {{ $true }} else {{ $false }}
            }}
            
            $result.Categories += $categoryInfo
        }}
        
        Write-LogSuccess ""Successfully processed $($result.Categories.Count) categories"" -Category ""Discovery""
    }} else {{
        Write-LogInfo ""No tag categories found"" -Category ""Discovery""
    }}
    
    # OPTIMIZED: Get all tags and all tag assignments in bulk operations
    Write-LogInfo ""Retrieving all tags (bulk operation)..."" -Category ""Discovery""
    $allTags = Get-Tag -ErrorAction SilentlyContinue
    
    if ($allTags -and $allTags.Count -gt 0) {{
        Write-LogInfo ""Found $($allTags.Count) tags - now retrieving assignments in bulk"" -Category ""Discovery""
        
        # OPTIMIZATION: Get all tag assignments in one operation instead of per-tag
        $allAssignments = @()
        try {{
            Write-LogInfo ""Retrieving all tag assignments (bulk operation)..."" -Category ""Discovery""
            $allAssignments = Get-TagAssignment -ErrorAction SilentlyContinue
            Write-LogInfo ""Retrieved $($allAssignments.Count) total tag assignments"" -Category ""Discovery""
        }} catch {{
            Write-LogWarning ""Could not retrieve bulk tag assignments: $($_.Exception.Message)"" -Category ""Discovery""
            $allAssignments = @()
        }}
        
        # Create a hashtable for fast assignment counting
        $assignmentCounts = @{{}}
        foreach ($assignment in $allAssignments) {{
            if ($assignment.Tag -and $assignment.Tag.Id) {{
                $tagId = $assignment.Tag.Id.ToString()
                if (-not $assignmentCounts.ContainsKey($tagId)) {{
                    $assignmentCounts[$tagId] = 0
                }}
                $assignmentCounts[$tagId]++
            }}
        }}
        
        Write-LogInfo ""Processing $($allTags.Count) tags with pre-calculated assignment counts"" -Category ""Discovery""
        
        foreach ($tag in $allTags) {{
            # Get assignment count from hashtable (much faster than individual queries)
            $assignedCount = 0
            if ($tag.Id -and $assignmentCounts.ContainsKey($tag.Id.ToString())) {{
                $assignedCount = $assignmentCounts[$tag.Id.ToString()]
            }}
            
            $tagInfo = @{{
                Name = if ($tag.Name) {{ $tag.Name.ToString() }} else {{ """" }}
                Id = if ($tag.Id) {{ $tag.Id.ToString() }} else {{ """" }}
                CategoryName = if ($tag.Category -and $tag.Category.Name) {{ $tag.Category.Name.ToString() }} else {{ """" }}
                Description = if ($tag.Description) {{ $tag.Description.ToString() }} else {{ """" }}
                AssignedObjectCount = [int]$assignedCount
            }}
            
            $result.Tags += $tagInfo
        }}
        
        Write-LogSuccess ""Successfully processed $($result.Tags.Count) tags with assignment counts"" -Category ""Discovery""
    }} else {{
        Write-LogInfo ""No tags found"" -Category ""Discovery""
    }}
    
    # Output as JSON for C# consumption
    $jsonOutput = @($result) | ConvertTo-Json -Depth 5
    if ($jsonOutput) {{
        Write-Output $jsonOutput
    }} else {{
        Write-Output ""[]""
    }}
    
}} catch {{
    $errorMessage = ""Tag and category discovery failed: $($_.Exception.Message)""
    Write-LogError $errorMessage -Category ""Error""
    Write-LogError ""Stack trace: $($_.ScriptStackTrace)"" -Category ""Error""
    Write-Output ""ERROR: $($_.Exception.Message)""
}} finally {{
    if ($Global:ScriptLogFile) {{
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""SCRIPT COMPLETED: TagCategoryDiscovery"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}
";

        return await Task.FromResult(scriptContent);
    }

    private async Task LoadTagsAndCategoriesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        try
        {
            _logger.LogInformation("Loading tags and categories for {VCenterName} (extended timeout for large datasets)", vCenterName);
            
            var logPath = await GetLogPathAsync();
            var script = await BuildTagsAndCategoriesScriptWithLoggingAsync(vCenterName, logPath);
            
            // Use extended timeout for tag operations due to potential large datasets
            var tagCategoryData = await ExecuteWithExtendedTimeoutAsync<TagCategoryData>(script, connectionType, "TagsAndCategories", vCenterName, TimeSpan.FromMinutes(15));
            
            if (tagCategoryData != null && tagCategoryData.Length > 0)
            {
                var data = tagCategoryData[0]; // Should return a single object with Categories and Tags arrays
                
                // Load categories
                if (data.Categories != null)
                {
                    foreach (var category in data.Categories)
                    {
                        var categoryInfo = new CategoryInfo
                        {
                            Name = category.Name ?? string.Empty,
                            Id = category.Id ?? string.Empty,
                            Description = category.Description ?? string.Empty,
                            TagCount = category.TagCount,
                            IsMultipleCardinality = category.IsMultipleCardinality
                        };
                        
                        inventory.Categories.Add(categoryInfo);
                    }
                }
                
                // Load tags
                if (data.Tags != null)
                {
                    foreach (var tag in data.Tags)
                    {
                        var tagInfo = new TagInfo
                        {
                            Name = tag.Name ?? string.Empty,
                            Id = tag.Id ?? string.Empty,
                            CategoryName = tag.CategoryName ?? string.Empty,
                            Description = tag.Description ?? string.Empty,
                            AssignedObjectCount = tag.AssignedObjectCount
                        };
                        
                        inventory.Tags.Add(tagInfo);
                    }
                }
                
                _logger.LogInformation("Loaded {CategoryCount} categories and {TagCount} tags for {VCenterName}", 
                    inventory.Categories.Count, inventory.Tags.Count, vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tags and categories for {VCenterName}", vCenterName);
        }
    }

    private async Task LoadRolesAndPermissionsAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        try
        {
            _logger.LogInformation("Loading roles and permissions for {VCenterName} using SSO Admin script", vCenterName);

            // Execute enhanced SSO Admin script with integrated logging
            var logPath = await GetLogPathAsync(); // Get the configured log path
            var scriptContent = await BuildSSOAdminScriptWithLoggingAsync(vCenterName, logPath);
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
                        
                        // Log summary to activity log
                        var totalPermissions = inventory.Permissions.Count;
                        var roleCount = inventory.Roles.Count(r => !r.IsSystem);
                        _logger.LogInformation("Admin Config Discovery Complete: {CustomRoles} custom roles, {TotalPermissions} permissions (including global permissions)", 
                            roleCount, totalPermissions);
                    }
                }
            }
            else if (result.StartsWith("ERROR:"))
            {
                _logger.LogWarning("SSO Admin script returned error for {VCenterName}: {Error}", vCenterName, result);
                _logger.LogInformation("Falling back to basic role/permission discovery for {VCenterName}", vCenterName);
                // Fallback to basic role/permission discovery
                await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
            }
            else
            {
                _logger.LogWarning("Unexpected result from SSO Admin script for {VCenterName}: {Result}", vCenterName, result.Substring(0, Math.Min(100, result.Length)));
                _logger.LogInformation("Falling back to basic role/permission discovery for {VCenterName}", vCenterName);
                // Fallback to basic role/permission discovery
                await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load roles and permissions using SSO Admin script for {VCenterName}", vCenterName);
            _logger.LogInformation("Falling back to basic role/permission discovery for {VCenterName}", vCenterName);
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
            
            // Log fallback completion summary
            var totalPermissions = inventory.Permissions.Count;
            var roleCount = inventory.Roles.Count(r => !r.IsSystem);
            _logger.LogInformation("Basic Admin Config Discovery Complete: {CustomRoles} custom roles, {TotalPermissions} permissions", 
                roleCount, totalPermissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load basic roles and permissions for {VCenterName}", vCenterName);
        }
    }

    private async Task<string> GetLogPathAsync()
    {
        try
        {
            // Try to read the log path from configuration files
            string? logPath = null;
            
            // First try to read from appsettings.json in the application directory
            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(appSettingsPath))
            {
                try
                {
                    var appSettingsJson = await File.ReadAllTextAsync(appSettingsPath);
                    var appSettingsData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(appSettingsJson);
                    if (appSettingsData.TryGetProperty("AppConfig", out var appConfigElement) &&
                        appConfigElement.TryGetProperty("LogPath", out var logPathElement))
                    {
                        logPath = logPathElement.GetString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read LogPath from appsettings.json");
                }
            }
            
            // Then try userconfig.json in AppData
            if (string.IsNullOrEmpty(logPath))
            {
                var userConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VCenterMigrationTool",
                    "userconfig.json");
                    
                if (File.Exists(userConfigPath))
                {
                    try
                    {
                        var userConfigJson = await File.ReadAllTextAsync(userConfigPath);
                        var userConfigData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(userConfigJson);
                        if (userConfigData.TryGetProperty("LogPath", out var userLogPathElement))
                        {
                            logPath = userLogPathElement.GetString();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read LogPath from userconfig.json");
                    }
                }
            }
            
            // Default to AppData if no configuration found
            if (string.IsNullOrEmpty(logPath))
            {
                logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs");
                _logger.LogInformation("No LogPath configured, using default: {LogPath}", logPath);
            }
            
            return logPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining log path, using default");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs");
        }
    }

    private async Task<string> BuildSSOAdminScriptWithLoggingAsync(string vCenterServer, string logPath)
    {
        var scriptContent = $@"
# SSO Admin Config Discovery Script with Embedded Logging
param()

# Embedded logging functions for file logging
$Global:ScriptLogFile = $null

function Start-ScriptLogging {{
    param([string]$ScriptName, [string]$LogPath)
    
    if ($LogPath) {{
        $psLogDir = Join-Path $LogPath ""PowerShell""
        if (-not (Test-Path $psLogDir)) {{
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }}
        
        $timestamp = Get-Date -Format ""yyyyMMdd_HHmmss""
        $sessionId = [System.Guid]::NewGuid().ToString(""N"").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir ""${{ScriptName}}_${{timestamp}}_${{sessionId}}.log""
        
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        ""SCRIPT START: $ScriptName"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}

function Write-LogInfo {{
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Info] [$Category] $Message""
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogSuccess {{
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Success] [$Category] $Message""
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogWarning {{
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Warning] [$Category] $Message""
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogError {{
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Error] [$Category] $Message""
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

# Start logging
Start-ScriptLogging -ScriptName ""AdminConfigDiscovery"" -LogPath ""{logPath.Replace("\\", "\\\\")}""

try {{
    Write-LogInfo ""Starting SSO admin configuration discovery for {vCenterServer}"" -Category ""Initialization""
    # Check for and import SSO Admin module if available
    $ssoModuleAvailable = $false
    try {{
        $ssoModule = Get-Module -ListAvailable -Name VMware.vSphere.SsoAdmin
        if ($ssoModule) {{
            Import-Module VMware.vSphere.SsoAdmin -Force -ErrorAction Stop
            $ssoModuleAvailable = $true
            Write-LogSuccess ""SSO Admin module imported successfully"" -Category ""Module""
        }} else {{
            Write-LogWarning ""VMware.vSphere.SsoAdmin module not found - SSO data will be limited"" -Category ""Module""
        }}
    }} catch {{
        Write-LogWarning ""Could not import SSO Admin module: $($_.Exception.Message)"" -Category ""Module""
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
    Write-LogInfo ""Retrieving roles from vCenter"" -Category ""Discovery""
    $viRoles = Get-VIRole -ErrorAction SilentlyContinue
    foreach ($role in $viRoles) {{
        $assignmentCount = 0
        try {{
            $assignments = Get-VIPermission | Where-Object {{ $_.Role -eq $role.Name }}
            $assignmentCount = @($assignments).Count
        }} catch {{
            # Silently continue if assignment counting fails
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
    Write-LogSuccess ""Found $($ssoData.TotalRoles) roles"" -Category ""Discovery""

    # Get Permissions (including global permissions)
    Write-LogInfo ""Retrieving permissions from vCenter"" -Category ""Discovery""
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
        Write-LogInfo ""Retrieving global permissions from vCenter"" -Category ""Discovery""
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
        
    }} catch {{
        Write-LogWarning ""Could not retrieve global permissions: $($_.Exception.Message)"" -Category ""Discovery""
    }}

    $ssoData.TotalPermissions = $ssoData.Permissions.Count + $ssoData.GlobalPermissions.Count
    Write-LogSuccess ""Discovery complete: $($ssoData.TotalRoles) roles, $($ssoData.TotalPermissions) permissions"" -Category ""Summary""

    # Output result as JSON
    $jsonOutput = $ssoData | ConvertTo-Json -Depth 10
    Write-Output $jsonOutput

}} catch {{
    Write-LogError ""Discovery failed: $($_.Exception.Message)"" -Category ""Error""
    Write-Output ""ERROR: $($_.Exception.Message)""
}} finally {{
    # Complete logging
    if ($Global:ScriptLogFile) {{
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""SCRIPT COMPLETED: AdminConfigDiscovery"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}
";

        return await Task.FromResult(scriptContent);
    }

    private async Task<string> BuildCustomAttributeScriptWithLoggingAsync(string vCenterServer, string logPath)
    {
        var scriptContent = $@"
# Custom Attribute Discovery Script with Embedded Logging
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $false

function Write-LogInfo {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Info] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor White }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogSuccess {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Success] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Green }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogWarning {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Warning] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Yellow }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Write-LogError {{ 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss.fff""
    $logEntry = ""$timestamp [Error] [$Category] $Message""
    if (-not $Global:SuppressConsoleOutput) {{ Write-Host $logEntry -ForegroundColor Red }}
    if ($Global:ScriptLogFile) {{ $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }}
}}

function Start-ScriptLogging {{
    param([string]$ScriptName = '', [string]$LogPath = $null)
    
    if ($LogPath) {{
        if ([System.IO.Path]::HasExtension($LogPath)) {{
            $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
        }} else {{
            $logDir = $LogPath
        }}
        
        $psLogDir = Join-Path $logDir ""PowerShell""
        if (-not (Test-Path $psLogDir)) {{
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }}
        
        $timestamp = Get-Date -Format ""yyyyMMdd_HHmmss""
        $sessionId = [System.Guid]::NewGuid().ToString(""N"").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir ""${{ScriptName}}_${{timestamp}}_${{sessionId}}.log""
        
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        ""SCRIPT START: $ScriptName"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}

# Start logging
Start-ScriptLogging -ScriptName ""CustomAttributeDiscovery"" -LogPath ""{logPath.Replace("\\", "\\\\")}""

Write-LogInfo ""Starting custom attribute discovery"" -Category ""Discovery""

try {{
    # Get custom attributes
    $customAttributes = @()
    
    Write-LogInfo ""Retrieving custom attributes..."" -Category ""Discovery""
    $allAttributes = Get-CustomAttribute -ErrorAction SilentlyContinue
    
    if ($allAttributes -and $allAttributes.Count -gt 0) {{
        Write-LogInfo ""Found $($allAttributes.Count) custom attributes"" -Category ""Discovery""
        
        foreach ($attr in $allAttributes) {{
            Write-LogInfo ""Processing custom attribute: $($attr.Name)"" -Category ""Discovery""
            
            # Get assignment count for this attribute
            $assignmentCount = 0
            try {{
                # Try to count entities with this attribute set
                $entities = Get-Inventory | Where-Object {{ $_ | Get-Annotation -CustomAttribute $attr.Name -ErrorAction SilentlyContinue }}
                $assignmentCount = if ($entities) {{ @($entities).Count }} else {{ 0 }}
            }} catch {{
                Write-LogWarning ""Could not count assignments for attribute '$($attr.Name)': $($_.Exception.Message)"" -Category ""Discovery""
            }}
            
            $attributeInfo = @{{
                Name = if ($attr.Name) {{ $attr.Name.ToString() }} else {{ """" }}
                Key = if ($attr.Key) {{ $attr.Key.ToString() }} else {{ """" }}
                Type = if ($attr.Type) {{ $attr.Type.ToString() }} else {{ ""String"" }}
                IsGlobal = if ($attr.TargetType -eq $null -or $attr.TargetType -eq """") {{ $true }} else {{ $false }}
                ApplicableTypes = if ($attr.TargetType) {{ @($attr.TargetType.ToString()) }} else {{ @() }}
                AssignmentCount = $assignmentCount
            }}
            
            $customAttributes += $attributeInfo
        }}
        
        Write-LogSuccess ""Successfully processed $($customAttributes.Count) custom attributes"" -Category ""Discovery""
    }} else {{
        Write-LogInfo ""No custom attributes found"" -Category ""Discovery""
    }}
    
    # Output as JSON for C# consumption
    $jsonOutput = $customAttributes | ConvertTo-Json -Depth 5
    if ($jsonOutput) {{
        Write-Output $jsonOutput
    }} else {{
        Write-Output ""[]""
    }}
    
}} catch {{
    $errorMessage = ""Custom attribute discovery failed: $($_.Exception.Message)""
    Write-LogError $errorMessage -Category ""Error""
    Write-LogError ""Stack trace: $($_.ScriptStackTrace)"" -Category ""Error""
    Write-Output ""ERROR: $($_.Exception.Message)""
}} finally {{
    if ($Global:ScriptLogFile) {{
        $separator = ""="" * 80
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""SCRIPT COMPLETED: CustomAttributeDiscovery"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        ""$separator"" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }}
}}
";

        return await Task.FromResult(scriptContent);
    }

    private async Task LoadCustomAttributesAsync(string vCenterName, string username, string password, VCenterInventory inventory, string connectionType)
    {
        try
        {
            _logger.LogInformation("Loading custom attributes for {VCenterName}", vCenterName);
            
            var logPath = await GetLogPathAsync();
            var script = await BuildCustomAttributeScriptWithLoggingAsync(vCenterName, logPath);
            
            var customAttributeData = await ExecuteAndDeserializeAsync<CustomAttributeData>(script, connectionType, "CustomAttributes", vCenterName);
            
            if (customAttributeData != null)
            {
                foreach (var attr in customAttributeData)
                {
                    var customAttrInfo = new CustomAttributeInfo
                    {
                        Name = attr.Name ?? string.Empty,
                        Id = attr.Key?.ToString() ?? string.Empty,
                        Type = attr.Type ?? string.Empty,
                        IsGlobal = attr.IsGlobal,
                        ApplicableTypes = attr.ApplicableTypes ?? Array.Empty<string>(),
                        AssignmentCount = attr.AssignmentCount
                    };
                    
                    inventory.CustomAttributes.Add(customAttrInfo);
                }
                
                _logger.LogInformation("Loaded {Count} custom attributes for {VCenterName}", inventory.CustomAttributes.Count, vCenterName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load custom attributes for {VCenterName}", vCenterName);
        }
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

    /// <summary>
    /// Execute PowerShell script with extended timeout and safely deserialize JSON response
    /// </summary>
    private async Task<T[]?> ExecuteWithExtendedTimeoutAsync<T>(string script, string connectionType, string objectType, string vCenterName, TimeSpan timeout)
    {
        try
        {
            _logger.LogInformation("Executing {ObjectType} script with extended timeout ({TimeoutMinutes} minutes) for {VCenterName}", 
                objectType, timeout.TotalMinutes, vCenterName);
            
            // Check if the service supports custom timeout through reflection or dynamic invocation
            var serviceType = _persistentConnectionService.GetType();
            var extendedTimeoutMethod = serviceType.GetMethod("ExecuteCommandWithTimeoutAsync", new[] { typeof(string), typeof(string), typeof(TimeSpan) });
            
            string result;
            if (extendedTimeoutMethod != null)
            {
                // Service supports extended timeout
                var task = (Task<string>)extendedTimeoutMethod.Invoke(_persistentConnectionService, new object[] { connectionType, script, timeout })!;
                result = await task;
                _logger.LogInformation("Script executed successfully with extended timeout for {ObjectType}", objectType);
            }
            else
            {
                // Fallback to default timeout with warning
                _logger.LogWarning("Extended timeout not supported by connection service, using default timeout for {ObjectType}", objectType);
                result = await _persistentConnectionService.ExecuteCommandAsync(connectionType, script);
            }

            if (!string.IsNullOrEmpty(result))
            {
                // Check if result looks like JSON before attempting to deserialize
                if (result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"))
                {
                    var objects = JsonSerializer.Deserialize<T[]>(result);
                    if (objects != null)
                    {
                        _logger.LogInformation("Loaded {Count} {ObjectType} for {VCenterName} with extended timeout", objects.Length, objectType, vCenterName);
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
            _logger.LogError(ex, "Failed to load {ObjectType} for {VCenterName} with extended timeout", objectType, vCenterName);
        }

        return null;
    }

    /// <summary>
    /// Safely extracts a string value from a dictionary that may contain JsonElement objects
    /// </summary>
    private static string GetStringValueFromDict(Dictionary<string, object> dict, string key, string defaultValue)
    {
        if (!dict.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() ?? defaultValue : jsonElement.ToString();
        }

        return value.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Safely extracts an integer value from a dictionary that may contain JsonElement objects
    /// </summary>
    private static int GetIntValueFromDict(Dictionary<string, object> dict, string key, int defaultValue)
    {
        if (!dict.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.Number ? jsonElement.GetInt32() : defaultValue;
        }

        if (value is int intValue)
            return intValue;

        if (int.TryParse(value.ToString(), out var parsedValue))
            return parsedValue;

        return defaultValue;
    }

    /// <summary>
    /// Safely extracts a boolean value from a dictionary that may contain JsonElement objects
    /// </summary>
    private static bool GetBoolValueFromDict(Dictionary<string, object> dict, string key, bool defaultValue)
    {
        if (!dict.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind == JsonValueKind.True || 
                   (jsonElement.ValueKind == JsonValueKind.String && bool.TryParse(jsonElement.GetString(), out var boolResult) && boolResult);
        }

        if (value is bool boolValue)
            return boolValue;

        if (bool.TryParse(value.ToString(), out var parsedValue))
            return parsedValue;

        return defaultValue;
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

/// <summary>
/// Custom attribute data for deserializing PowerShell script output
/// </summary>
public class CustomAttributeData
{
    public string? Name { get; set; }
    public object? Key { get; set; }
    public string? Type { get; set; }
    public bool IsGlobal { get; set; }
    public string[]? ApplicableTypes { get; set; }
    public int AssignmentCount { get; set; }
}

/// <summary>
/// Tag and category data container for deserializing PowerShell script output
/// </summary>
public class TagCategoryData
{
    public string? CollectionDate { get; set; }
    public string? VCenterServer { get; set; }
    public List<TagData>? Tags { get; set; }
    public List<CategoryData>? Categories { get; set; }
}

/// <summary>
/// Tag data for deserializing PowerShell script output
/// </summary>
public class TagData
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? CategoryName { get; set; }
    public string? Description { get; set; }
    public int AssignedObjectCount { get; set; }
}

/// <summary>
/// Category data for deserializing PowerShell script output
/// </summary>
public class CategoryData
{
    public string? Name { get; set; }
    public string? Id { get; set; }
    public string? Description { get; set; }
    public int TagCount { get; set; }
    public bool IsMultipleCardinality { get; set; }
}