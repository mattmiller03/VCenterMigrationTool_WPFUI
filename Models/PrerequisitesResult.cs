// In Models/PrerequisitesResult.cs
using System.Text.Json.Serialization;

namespace VCenterMigrationTool.Models;

public class PrerequisitesResult
{
    [JsonPropertyName("PowerShellVersion")]
    public string PowerShellVersion { get; set; } = "Unknown";

    [JsonPropertyName("IsPowerCliInstalled")]
    public bool IsPowerCliInstalled { get; set; }
}