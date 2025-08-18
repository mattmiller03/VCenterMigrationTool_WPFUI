using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Pages;

public partial class ActivityLogsViewModel : ObservableObject, IDisposable
    {
    private readonly ILogger<ActivityLogsViewModel> _logger;
    private readonly PowerShellLoggingService _powerShellLoggingService;
    private readonly ConfigurationService _configurationService;

    [ObservableProperty]
    private ObservableCollection<LogEntryDisplay> _logEntries;

    [ObservableProperty]
    private LogEntryDisplay? _selectedLogEntry;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _selectedLogLevel = "All";

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private int _maxLogEntries = 1000;

    [ObservableProperty]
    private string _logStats = "";

    private readonly object _logEntriesLock = new object();
    private ICollectionView? _filteredView;

    public ObservableCollection<string> LogLevels { get; }

    public ActivityLogsViewModel (
        ILogger<ActivityLogsViewModel> logger,
        PowerShellLoggingService powerShellLoggingService,
        ConfigurationService configurationService)
        {
        _logger = logger;
        _powerShellLoggingService = powerShellLoggingService;
        _configurationService = configurationService;

        LogEntries = new ObservableCollection<LogEntryDisplay>();
        LogLevels = new ObservableCollection<string> { "All", "DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL", "SUCCESS" };

        // Enable collection synchronization for thread safety
        BindingOperations.EnableCollectionSynchronization(LogEntries, _logEntriesLock);

        // Subscribe to PowerShell logging events
        _powerShellLoggingService.LogEntryAdded += OnPowerShellLogEntryAdded;

        // Load recent logs on startup
        _ = Task.Run(LoadRecentLogsAsync);

        UpdateLogStats();
        }

    private void OnPowerShellLogEntryAdded (object? sender, PowerShellLoggingService.LogEntry e)
        {
        var displayEntry = new LogEntryDisplay
            {
            Timestamp = e.Timestamp,
            Level = e.Level,
            Source = e.Source,
            ScriptName = e.ScriptName,
            SessionId = e.SessionId,
            Message = e.Message,
            FullText = $"{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{e.Level}] [{e.SessionId}] [{e.ScriptName}] {e.Message}"
            };

        // Add on UI thread
        App.Current.Dispatcher.Invoke(() =>
        {
            lock (_logEntriesLock)
                {
                LogEntries.Add(displayEntry);

                // Maintain max entries limit
                while (LogEntries.Count > MaxLogEntries)
                    {
                    LogEntries.RemoveAt(0);
                    }
                }

            UpdateLogStats();
        });
        }

    private async Task LoadRecentLogsAsync ()
        {
        try
            {
            var recentLogs = _powerShellLoggingService.GetRecentLogs(100);

            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_logEntriesLock)
                    {
                    foreach (var log in recentLogs)
                        {
                        var displayEntry = new LogEntryDisplay
                            {
                            Timestamp = log.Timestamp,
                            Level = log.Level,
                            Source = log.Source,
                            ScriptName = log.ScriptName,
                            SessionId = log.SessionId,
                            Message = log.Message,
                            FullText = $"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{log.Level}] [{log.SessionId}] [{log.ScriptName}] {log.Message}"
                            };

                        LogEntries.Add(displayEntry);
                        }
                    }

                UpdateLogStats();
            });
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load recent logs");
            }
        }

    [RelayCommand]
    private void OnClearLogs ()
        {
        lock (_logEntriesLock)
            {
            LogEntries.Clear();
            }
        UpdateLogStats();
        _logger.LogInformation("Activity logs cleared by user");
        }

    [RelayCommand]
    private async Task OnExportLogs ()
        {
        try
            {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Get the configured log path or use a fallback
            var baseLogPath = _configurationService.GetConfiguration().LogPath;
            if (string.IsNullOrEmpty(baseLogPath))
                {
                baseLogPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VCenterMigrationTool");
                }

            // Ensure the directory exists
            if (!System.IO.Directory.Exists(baseLogPath))
                {
                System.IO.Directory.CreateDirectory(baseLogPath);
                }

            var exportPath = System.IO.Path.Combine(baseLogPath, $"ActivityLogs_Export_{timestamp}.txt");

            // Create export content
            var exportContent = new System.Text.StringBuilder();
            exportContent.AppendLine($"Activity Logs Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            exportContent.AppendLine("=".PadRight(80, '='));
            exportContent.AppendLine($"Total Entries: {LogEntries.Count}");
            exportContent.AppendLine($"Export Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            exportContent.AppendLine("=".PadRight(80, '='));
            exportContent.AppendLine();

            lock (_logEntriesLock)
                {
                foreach (var entry in LogEntries)
                    {
                    exportContent.AppendLine(entry.FullText);
                    }
                }

            await System.IO.File.WriteAllTextAsync(exportPath, exportContent.ToString());

            _logger.LogInformation("Activity logs exported successfully to: {ExportPath}", exportPath);

            // Show success message to user (you could also show a toast notification)
            System.Windows.MessageBox.Show(
                $"Activity logs exported successfully to:\n{exportPath}",
                "Export Complete",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to export activity logs");

            // Show error message to user
            System.Windows.MessageBox.Show(
                $"Failed to export activity logs:\n{ex.Message}",
                "Export Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            }
        }


    [RelayCommand]
    private void OnApplyFilter ()
        {
        _filteredView?.Refresh();
        }

    [RelayCommand]
    private void OnClearFilter ()
        {
        FilterText = "";
        SelectedLogLevel = "All";
        _filteredView?.Refresh();
        }



    public void SetCollectionView (ICollectionView collectionView)
        {
        _filteredView = collectionView;
        _filteredView.Filter = FilterLogEntries;
        }

    private bool FilterLogEntries (object item)
        {
        if (item is not LogEntryDisplay entry) return false;

        // Filter by log level
        if (SelectedLogLevel != "All" && !entry.Level.Equals(SelectedLogLevel, StringComparison.OrdinalIgnoreCase))
            return false;

        // Filter by text
        if (!string.IsNullOrWhiteSpace(FilterText))
            {
            var filterLower = FilterText.ToLower();
            return entry.Message.ToLower().Contains(filterLower) ||
                   entry.ScriptName.ToLower().Contains(filterLower) ||
                   entry.SessionId.ToLower().Contains(filterLower);
            }

        return true;
        }

    partial void OnFilterTextChanged (string value)
        {
        _filteredView?.Refresh();
        }

    partial void OnSelectedLogLevelChanged (string value)
        {
        _filteredView?.Refresh();
        }

    partial void OnMaxLogEntriesChanged (int value)
        {
        lock (_logEntriesLock)
            {
            while (LogEntries.Count > value)
                {
                LogEntries.RemoveAt(0);
                }
            }
        UpdateLogStats();
        }

    private void UpdateLogStats ()
        {
        var total = LogEntries.Count;
        var errors = LogEntries.Count(e => e.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase));
        var warnings = LogEntries.Count(e => e.Level.Equals("WARNING", StringComparison.OrdinalIgnoreCase));

        LogStats = $"Total: {total} | Errors: {errors} | Warnings: {warnings}";
        }

    public void Dispose ()
        {
        _powerShellLoggingService.LogEntryAdded -= OnPowerShellLogEntryAdded;
        }
    }

public class LogEntryDisplay
    {
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Source { get; set; } = "";
    public string ScriptName { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Message { get; set; } = "";
    public string FullText { get; set; } = "";

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss.fff");
    public string LevelColor => Level.ToUpper() switch
        {
            "ERROR" => "#FF6B6B",
            "CRITICAL" => "#FF3838",
            "WARNING" => "#FFB366",
            "SUCCESS" => "#51CF66",
            "DEBUG" => "#868E96",
            "INFO" => "#339AF0",
            _ => "#212529"
            };
    }