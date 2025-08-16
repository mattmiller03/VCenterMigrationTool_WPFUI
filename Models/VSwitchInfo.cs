// Models/VSwitchInfo.cs
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public partial class VSwitchInfo : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _type = "Standard"; // "Standard" or "Distributed"

    public ObservableCollection<PortGroupInfo> PortGroups { get; set; } = new();
}