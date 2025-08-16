using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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

    [ObservableProperty]
    private int _vmCount;

    /// <summary>
    /// Gets the number of VMs in this resource pool
    /// </summary>
    [JsonIgnore]
    public int VmCountProperty => Vms?.Count ?? VmCount;

    /// <summary>
    /// Gets a display string for VM information
    /// </summary>
    [JsonIgnore]
    public string VmInfo => VmCountProperty == 0 ? "No VMs" : $"{VmCountProperty} VM{(VmCountProperty == 1 ? "" : "s")}";

    /// <summary>
    /// Gets a display string for CPU configuration
    /// </summary>
    [JsonIgnore]
    public string CpuInfo => $"{CpuSharesLevel} ({CpuShares} shares, {CpuReservationMHz} MHz reserved)";

    /// <summary>
    /// Gets a display string for memory configuration
    /// </summary>
    [JsonIgnore]
    public string MemoryInfo => $"{MemSharesLevel} ({MemShares} shares, {MemReservationMB} MB reserved)";

    /// <summary>
    /// Gets a display string for the VM list
    /// </summary>
    [JsonIgnore]
    public string VmListDisplay => Vms?.Count > 0 ? string.Join(", ", Vms.Take(5)) + (Vms.Count > 5 ? "..." : "") : "No VMs";
    }