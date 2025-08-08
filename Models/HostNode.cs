// In Models/HostNode.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public partial class HostNode : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}