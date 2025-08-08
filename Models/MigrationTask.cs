// In Models/MigrationTask.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public partial class MigrationTask : ObservableObject
{
    [ObservableProperty] private string _objectName = string.Empty;
    [ObservableProperty] private string _status = "Pending";
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _details = string.Empty;
}