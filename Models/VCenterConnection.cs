// In Models/VCenterConnection.cs
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VCenterMigrationTool.Models;

public partial class VCenterConnection : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    // This will hold the encrypted password for storing in the JSON file.
    public string? ProtectedPassword { get; set; }

    // This property is for UI binding and will not be saved in the JSON file.
    [JsonIgnore]
    public bool ShouldSavePassword { get; set; } = false;
}