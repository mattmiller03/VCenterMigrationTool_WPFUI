using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace VCenterMigrationTool.Models;

public partial class SettingsCategory : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private object? _content;

    // --- FIX: Add the missing IsSelected property ---
    [ObservableProperty]
    private bool _isSelected;

    public ObservableCollection<SettingsCategory> SubCategories { get; } = new();
}