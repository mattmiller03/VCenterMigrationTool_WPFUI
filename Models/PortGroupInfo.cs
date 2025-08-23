using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public partial class PortGroupInfo : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _vlanId;

    [ObservableProperty]
    private string _type = "Standard"; // "Standard" or "Distributed"

    [ObservableProperty]
    private int _numPorts = 0;
}