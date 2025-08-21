using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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

    public VCenterInventoryService(PersistentExternalConnectionService persistentConnectionService, ILogger<VCenterInventoryService> logger)
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
            $resourcePools = Get-ResourcePool | ForEach-Object {
                $rp = $_
                $cluster = $null
                $datacenter = $null
                
                # Try to find parent cluster
                try {
                    $parent = $rp.Parent
                    while ($parent -and !$cluster) {
                        if ($parent.GetType().Name -eq 'ClusterImpl') {
                            $cluster = $parent
                            break
                        }
                        $parent = $parent.Parent
                    }
                    
                    if ($cluster) {
                        $datacenter = Get-Datacenter -Cluster $cluster -ErrorAction SilentlyContinue
                    }
                } catch {
                    # If we can't find parent, continue without it
                }
                [PSCustomObject]@{
                    Name = $rp.Name
                    Id = $rp.Id
                    ClusterName = if ($cluster) { $cluster.Name } else { """" }
                    DatacenterName = if ($datacenter) { $datacenter.Name } else { """" }
                    ParentPath = $rp.Parent.Name
                    CpuLimitMhz = if ($rp.CpuLimitMhz -ne -1) { [int]$rp.CpuLimitMhz } else { 0 }
                    MemoryLimitMB = if ($rp.MemoryLimitMB -ne -1) { [int]$rp.MemoryLimitMB } else { 0 }
                    VmCount = [int]($rp | Get-VM | Measure-Object).Count
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
        // Placeholder - roles/permissions discovery is optional for MVP
        await Task.CompletedTask;
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