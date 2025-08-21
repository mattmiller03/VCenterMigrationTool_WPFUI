
// In Models/ClusterInfo.cs

using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public class ClusterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int HostCount { get; set; }
    public int VmCount { get; set; }
    public int DatastoreCount { get; set; }
    public double TotalCpuGhz { get; set; }
    public double TotalMemoryGB { get; set; }
    public bool HAEnabled { get; set; }
    public bool DrsEnabled { get; set; }
    public string EVCMode { get; set; } = string.Empty;
    public string DatacenterName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public ObservableCollection<EsxiHost> Hosts { get; set; } = new();

    // For display in UI - show datacenter/cluster format
    public string DisplayName => !string.IsNullOrEmpty(DatacenterName) 
        ? $"{DatacenterName}/{Name}" 
        : Name;
    public string ResourceSummary => $"{TotalCpuGhz:F1} GHz / {TotalMemoryGB:F0} GB RAM";
    public string ClusterSummary => $"{HostCount} hosts • {VmCount} VMs • {DatastoreCount} datastores";
    public string DatacenterInfo => !string.IsNullOrEmpty(DatacenterName) 
        ? $"Datacenter: {DatacenterName}" 
        : "Unknown Datacenter";
}
