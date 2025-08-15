using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents information about a host configuration backup
/// </summary>
public partial class HostBackupInfo : ObservableObject
{
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _hostName = string.Empty;

    [ObservableProperty]
    private DateTime _backupDate;

    [ObservableProperty]
    private long _fileSizeBytes;

    [ObservableProperty]
    private string _sourceVCenter = string.Empty;

    /// <summary>
    /// Gets a user-friendly display name for the backup
    /// </summary>
    public string DisplayName => $"{HostName} - {BackupDate:yyyy-MM-dd HH:mm}";

    /// <summary>
    /// Gets the file size in a human-readable format
    /// </summary>
    public string FileSizeDisplay
    {
        get
        {
            if (FileSizeBytes < 1024)
                return $"{FileSizeBytes} B";
            else if (FileSizeBytes < 1024 * 1024)
                return $"{FileSizeBytes / 1024:F1} KB";
            else
                return $"{FileSizeBytes / (1024 * 1024):F1} MB";
        }
    }
}