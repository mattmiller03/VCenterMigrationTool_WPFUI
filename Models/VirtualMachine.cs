using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public partial class VirtualMachine : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _powerState = string.Empty;
    [ObservableProperty] private string _esxiHost = string.Empty;
    [ObservableProperty] private string _datastore = string.Empty;
    [ObservableProperty] private string _cluster = string.Empty;
}