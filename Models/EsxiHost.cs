using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public class EsxiHost
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ClusterName { get; set; } = string.Empty;
    public string ConnectionState { get; set; } = string.Empty;
    public string PowerState { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public int CpuMhz { get; set; }
    public double MemoryGB { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public int VmCount { get; set; }
    public bool IsSelected { get; set; }

    // For display in UI
    public string DisplayName => Name;
    public string ResourceInfo => $"{CpuCores} cores / {MemoryGB:F0} GB";
    public string StatusInfo => $"{ConnectionState} / {PowerState}";
    public string VersionInfo => $"ESXi {Version} ({Build})";
    public string VmInfo => $"{VmCount} VMs";
}