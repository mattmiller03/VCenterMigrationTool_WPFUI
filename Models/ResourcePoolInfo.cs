using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents information about a vSphere Resource Pool
/// </summary>
public partial class ResourcePoolInfo : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _parentType = string.Empty;

    [ObservableProperty]
    private string _parentName = string.Empty;

    [ObservableProperty]
    private string _cpuSharesLevel = string.Empty;

    [ObservableProperty]
    private int _cpuShares;

    [ObservableProperty]
    private int _cpuReservationMHz;

    [ObservableProperty]
    private string _memSharesLevel = string.Empty;

    [ObservableProperty]
    private int _memShares;

    [ObservableProperty]
    private int _memReservationMB;

    [ObservableProperty]
    private List<string> _vms = new();

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets the number of VMs in this resource pool
    /// </summary>
    public int VmCount => Vms?.Count ?? 0;

    /// <summary>
    /// Gets a display string for VM information
    /// </summary>
    public string VmInfo => VmCount == 0 ? "No VMs" : $"{VmCount} VM{(VmCount == 1 ? "" : "s")}";

    /// <summary>
    /// Gets a display string for CPU configuration
    /// </summary>
    public string CpuInfo => $"{CpuSharesLevel} ({CpuShares} shares, {CpuReservationMHz} MHz reserved)";

    /// <summary>
    /// Gets a display string for memory configuration
    /// </summary>
    public string MemoryInfo => $"{MemSharesLevel} ({MemShares} shares, {MemReservationMB} MB reserved)";
}