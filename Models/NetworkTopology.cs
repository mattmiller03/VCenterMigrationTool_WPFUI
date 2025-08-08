// In Models/NetworkTopology.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public partial class VmKernelPortInfo : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _vSwitchName = string.Empty;
}

public partial class PortGroupInfo : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _vlanId;
}

public partial class VSwitchInfo : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _type = "Standard"; // "Standard" or "Distributed"
    public ObservableCollection<PortGroupInfo> PortGroups { get; set; } = new();
}

public partial class NetworkHostNode : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    public ObservableCollection<VSwitchInfo> VSwitches { get; set; } = new();
    public ObservableCollection<VmKernelPortInfo> VmKernelPorts { get; set; } = new();
}