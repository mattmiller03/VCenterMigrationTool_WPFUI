using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace VCenterMigrationTool.Models;

public partial class EsxiHost : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}