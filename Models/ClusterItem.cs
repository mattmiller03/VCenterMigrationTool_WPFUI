using System.ComponentModel;

namespace VCenterMigrationTool.Models;

public class ClusterItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private string _status = "Ready";
    
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Role", "Folder", "Tag", "Permission", "ResourcePool", "CustomAttribute"
    public string Path { get; set; } = string.Empty;
    public int ItemCount { get; set; } = 0;
    
    public bool IsSelected 
    { 
        get => _isSelected; 
        set 
        { 
            if (_isSelected != value) 
            { 
                _isSelected = value; 
                OnPropertyChanged(nameof(IsSelected)); 
            } 
        } 
    }
    
    public string Status 
    { 
        get => _status; 
        set 
        { 
            if (_status != value) 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status)); 
                OnPropertyChanged(nameof(StatusColor)); 
            } 
        } 
    }

    // UI Helper Properties
    public string TypeIcon => Type switch
    {
        "Role" => "People24",
        "Folder" => "Folder24", 
        "Tag" => "Tag24",
        "Permission" => "Shield24",
        "ResourcePool" => "DataUsageSettings24",
        "CustomAttribute" => "Properties24",
        _ => "DatabaseMultiple24"
    };

    public string TypeColor => Type switch
    {
        "Role" => "#2196F3",
        "Folder" => "#FF9800", 
        "Tag" => "#4CAF50",
        "Permission" => "#9C27B0",
        "ResourcePool" => "#607D8B",
        "CustomAttribute" => "#795548",
        _ => "#666666"
    };

    public string StatusColor => Status switch
    {
        "Ready" => "#2196F3",
        "Migrating" => "#FF9800",
        "Migrated" => "#4CAF50",
        "Failed" => "#F44336",
        "Skipped" => "#9E9E9E",
        _ => "#666666"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}