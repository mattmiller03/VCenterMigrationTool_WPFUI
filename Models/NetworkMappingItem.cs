using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents a network mapping between source and target vCenter environments
/// </summary>
public partial class NetworkMappingItem : ObservableObject
{
    [ObservableProperty]
    private string? _sourceNetwork;

    [ObservableProperty]
    private string? _targetNetwork;
}