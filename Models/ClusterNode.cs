// In Models/ClusterNode.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public partial class ClusterNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    public ObservableCollection<HostNode> Hosts { get; set; } = new();
}