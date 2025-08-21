using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents a complete snapshot of all objects in a vCenter environment
/// </summary>
public class VCenterInventory : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Connection Information
    public string VCenterName { get; set; } = string.Empty;
    public string VCenterVersion { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public bool IsStale => DateTime.Now - LastUpdated > TimeSpan.FromMinutes(30);

    // Core Infrastructure
    public List<DatacenterInfo> Datacenters { get; set; } = new();
    public List<ClusterInfo> Clusters { get; set; } = new();
    public List<EsxiHost> Hosts { get; set; } = new();
    public List<DatastoreInfo> Datastores { get; set; } = new();

    // Virtual Resources
    public List<VirtualMachineInfo> VirtualMachines { get; set; } = new();
    public List<ResourcePoolInventoryInfo> ResourcePools { get; set; } = new();
    public List<VirtualSwitchInfo> VirtualSwitches { get; set; } = new();

    // Organization Objects
    public List<FolderInfo> Folders { get; set; } = new();
    public List<TagInfo> Tags { get; set; } = new();
    public List<CategoryInfo> Categories { get; set; } = new();
    public List<RoleInfo> Roles { get; set; } = new();
    public List<PermissionInfo> Permissions { get; set; } = new();
    public List<CustomAttributeInfo> CustomAttributes { get; set; } = new();

    // Summary Statistics
    public InventoryStatistics Statistics => CalculateStatistics();

    /// <summary>
    /// Get clusters from a specific datacenter
    /// </summary>
    public List<ClusterInfo> GetClustersInDatacenter(string datacenterName)
    {
        return Clusters.Where(c => c.DatacenterName.Equals(datacenterName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Get all hosts in a specific cluster
    /// </summary>
    public List<EsxiHost> GetHostsInCluster(string clusterName)
    {
        return Hosts.Where(h => h.ClusterName.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Get all VMs in a specific cluster
    /// </summary>
    public List<VirtualMachineInfo> GetVMsInCluster(string clusterName)
    {
        return VirtualMachines.Where(vm => vm.ClusterName.Equals(clusterName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Calculate comprehensive statistics
    /// </summary>
    private InventoryStatistics CalculateStatistics()
    {
        return new InventoryStatistics
        {
            DatacenterCount = Datacenters.Count,
            ClusterCount = Clusters.Count,
            HostCount = Hosts.Count,
            VirtualMachineCount = VirtualMachines.Count,
            DatastoreCount = Datastores.Count,
            ResourcePoolCount = ResourcePools.Count,
            FolderCount = Folders.Count,
            TagCount = Tags.Count,
            RoleCount = Roles.Count,
            PermissionCount = Permissions.Count,
            
            TotalCpuCores = Hosts.Sum(h => h.CpuCores),
            TotalCpuGhz = Hosts.Sum(h => h.CpuMhz / 1000.0),
            TotalMemoryGB = Hosts.Sum(h => h.MemoryGB),
            TotalDatastoreCapacityGB = Datastores.Sum(d => d.CapacityGB),
            TotalDatastoreUsedGB = Datastores.Sum(d => d.UsedGB),
            
            PoweredOnVMs = VirtualMachines.Count(vm => vm.PowerState == "PoweredOn"),
            PoweredOffVMs = VirtualMachines.Count(vm => vm.PowerState == "PoweredOff")
        };
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Summary statistics for the entire vCenter inventory
/// </summary>
public class InventoryStatistics
{
    public int DatacenterCount { get; set; }
    public int ClusterCount { get; set; }
    public int HostCount { get; set; }
    public int VirtualMachineCount { get; set; }
    public int DatastoreCount { get; set; }
    public int ResourcePoolCount { get; set; }
    public int FolderCount { get; set; }
    public int TagCount { get; set; }
    public int RoleCount { get; set; }
    public int PermissionCount { get; set; }
    
    public int TotalCpuCores { get; set; }
    public double TotalCpuGhz { get; set; }
    public double TotalMemoryGB { get; set; }
    public double TotalDatastoreCapacityGB { get; set; }
    public double TotalDatastoreUsedGB { get; set; }
    
    public int PoweredOnVMs { get; set; }
    public int PoweredOffVMs { get; set; }
    
    public double DatastoreUtilizationPercent => TotalDatastoreCapacityGB > 0 
        ? (TotalDatastoreUsedGB / TotalDatastoreCapacityGB) * 100 
        : 0;
}