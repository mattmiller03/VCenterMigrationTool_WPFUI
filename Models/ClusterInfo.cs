
// In Models/ClusterInfo.cs

using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public class ClusterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int HostCount { get; set; }
    public double TotalCpuGhz { get; set; }
    public double TotalMemoryGB { get; set; }
    public ObservableCollection<EsxiHost> Hosts { get; set; } = new();

    // For display in UI
    public string DisplayName => $"{Name} ({HostCount} hosts)";
    public string ResourceSummary => $"{TotalCpuGhz:F1} GHz / {TotalMemoryGB:F0} GB RAM";
}
