// Models/NetworkHostNode.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public partial class NetworkHostNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    public ObservableCollection<VSwitchInfo> VSwitches { get; set; } = new();
    public ObservableCollection<VmKernelPortInfo> VmKernelPorts { get; set; } = new();
}