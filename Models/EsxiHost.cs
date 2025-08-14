using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public class EsxiHost : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}