using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace VCenterMigrationTool.Models;

public partial class VirtualMachine : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string PowerState { get; set; } = string.Empty;
    public string EsxiHost { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}