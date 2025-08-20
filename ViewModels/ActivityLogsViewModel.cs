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

namespace VCenterMigrationTool.ViewModels;

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

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private DateTime _lastRefreshTime = DateTime.Now;

    [ObservableProperty]
    private string _refreshStatus = "Ready";

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
    private async Task RefreshLogs ()
        {
        if (IsRefreshing) return; // Prevent multiple simultaneous refreshes

        IsRefreshing = true;
        RefreshStatus = "Refreshing logs...";

        try
            {
            _logger.LogInformation("Manual refresh of activity logs requested");

            // Clear current logs if desired (optional - you might want to keep them)
            // Uncomment the next line if you want to clear logs before refresh
            // LogEntries.Clear();

            // Load fresh logs from the service
            var recentLogs = _powerShellLoggingService.GetRecentLogs(MaxLogEntries);

            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_logEntriesLock)
                    {
                    // Option 1: Clear and reload all logs
                    LogEntries.Clear();

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

                    // Option 2: Only add new logs (alternative approach)
                    // You could implement logic here to only add logs newer than the latest timestamp
                    // var latestTimestamp = LogEntries.LastOrDefault()?.Timestamp ?? DateTime.MinValue;
                    // var newLogs = recentLogs.Where(l => l.Timestamp > latestTimestamp);
                    // foreach (var log in newLogs) { /* add logic */ }
                    }

                UpdateLogStats();
                LastRefreshTime = DateTime.Now;
                RefreshStatus = $"Refreshed {recentLogs.Count} log entries";
            });

            _logger.LogInformation("Activity logs refreshed successfully. Loaded {Count} entries", recentLogs.Count);

            // Clear the status message after a delay
            await Task.Delay(2000);
            RefreshStatus = "Ready";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to refresh activity logs");
            RefreshStatus = $"Refresh failed: {ex.Message}";

            // Clear error message after a delay
            await Task.Delay(3000);
            RefreshStatus = "Ready";
            }
        finally
            {
            IsRefreshing = false;
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

    [RelayCommand]
    private async Task RefreshAndClear ()
        {
        if (IsRefreshing) return;

        IsRefreshing = true;
        RefreshStatus = "Clearing and refreshing logs...";

        try
            {
            // Clear logs first
            lock (_logEntriesLock)
                {
                LogEntries.Clear();
                }

            // Then refresh
            await RefreshLogs();

            _logger.LogInformation("Activity logs cleared and refreshed");
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to clear and refresh activity logs");
            RefreshStatus = $"Clear and refresh failed: {ex.Message}";

            await Task.Delay(3000);
            RefreshStatus = "Ready";
            }
        finally
            {
            IsRefreshing = false;
            }
        }

    // Enhanced method to get logs with different time ranges
    [RelayCommand]
    private async Task RefreshLogsWithTimeRange (string timeRange)
        {
        if (IsRefreshing) return;

        IsRefreshing = true;

        try
            {
            var logCount = timeRange switch
                {
                    "LastHour" => 500,
                    "Last4Hours" => 1000,
                    "Last24Hours" => 2000,
                    "All" => 5000,
                    _ => MaxLogEntries
                    };

            RefreshStatus = $"Loading {timeRange.ToLower()} logs...";

            var recentLogs = _powerShellLoggingService.GetRecentLogs(logCount);

            // Filter by time range if needed
            var filteredLogs = timeRange switch
                {
                    "LastHour" => recentLogs.Where(l => l.Timestamp > DateTime.Now.AddHours(-1)),
                    "Last4Hours" => recentLogs.Where(l => l.Timestamp > DateTime.Now.AddHours(-4)),
                    "Last24Hours" => recentLogs.Where(l => l.Timestamp > DateTime.Now.AddDays(-1)),
                    _ => recentLogs
                    };

            await App.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_logEntriesLock)
                    {
                    LogEntries.Clear();

                    foreach (var log in filteredLogs)
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
                LastRefreshTime = DateTime.Now;
                RefreshStatus = $"Loaded {filteredLogs.Count()} entries from {timeRange.ToLower()}";
            });

            await Task.Delay(2000);
            RefreshStatus = "Ready";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to refresh logs with time range {TimeRange}", timeRange);
            RefreshStatus = $"Failed to load {timeRange.ToLower()} logs";

            await Task.Delay(3000);
            RefreshStatus = "Ready";
            }
        finally
            {
            IsRefreshing = false;
            }
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
    
    // Format script name for better display
    public string ScriptDisplay => 
        string.IsNullOrEmpty(ScriptName) ? "-" : 
        ScriptName.Replace(".ps1", "").Replace("-", " ");
    
    // Format message for better readability
    public string MessageDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Message))
                return "-";
                
            // Clean up common PowerShell output patterns
            var cleanMessage = Message;
            
            // Remove excessive whitespace and clean common patterns
            cleanMessage = System.Text.RegularExpressions.Regex.Replace(cleanMessage, @"\s+", " ").Trim();
            cleanMessage = cleanMessage.Replace("\r", "").Replace("\n", " ");
            
            // Remove PowerShell verbose noise
            if (cleanMessage.StartsWith("VERBOSE: "))
                cleanMessage = cleanMessage.Substring(9);
            if (cleanMessage.StartsWith("DEBUG: "))
                cleanMessage = cleanMessage.Substring(7);
            if (cleanMessage.StartsWith("WARNING: "))
                cleanMessage = cleanMessage.Substring(9);
            
            // Format specific message types with icons and better text
            if (cleanMessage.StartsWith("Starting script execution:"))
                return "🚀 " + cleanMessage.Replace("Starting script execution: ", "Started: ");
            else if (cleanMessage.StartsWith("Script execution completed:"))
                return cleanMessage.Contains("SUCCESS") ? 
                    "✅ " + cleanMessage.Replace("Script execution completed: ", "Completed: ") : 
                    "❌ " + cleanMessage.Replace("Script execution completed: ", "Failed: ");
            else if (cleanMessage.StartsWith("Script parameters:"))
                return "⚙️ " + cleanMessage.Replace("Script parameters: ", "Parameters: ");
            else if (cleanMessage.StartsWith("Trying PowerShell:"))
                return "🔍 " + cleanMessage;
            else if (cleanMessage.StartsWith("Starting PowerShell process:"))
                return "▶️ " + cleanMessage.Replace("Starting PowerShell process: ", "Starting: ");
            else if (cleanMessage.StartsWith("Process started with PID:"))
                return "🎯 " + cleanMessage;
            else if (cleanMessage.StartsWith("Process completed with exit code:"))
                return cleanMessage.Contains("exit code: 0") ? 
                    "✅ Process completed successfully" : 
                    "❌ " + cleanMessage;
            // Format script output with indentation
            else if (Source == "SCRIPT" && Level == "OUTPUT")
                return "    📜 " + cleanMessage;
            else if (Source == "SCRIPT" && Level == "ERROR")
                return "    ❌ " + cleanMessage;
            // Format script actions
            else if (Source == "ACTION")
                return "⚡ " + cleanMessage;
            // Connection-related messages
            else if (cleanMessage.Contains("Connect-VIServer") || cleanMessage.Contains("connection"))
                return "🔌 " + cleanMessage;
            // VM operations
            else if (cleanMessage.Contains("Virtual Machine") || cleanMessage.Contains("VM"))
                return "💻 " + cleanMessage;
            // Host operations
            else if (cleanMessage.Contains("ESXi") || cleanMessage.Contains("Host"))
                return "🖥️ " + cleanMessage;
            // Migration operations
            else if (cleanMessage.Contains("Migration") || cleanMessage.Contains("Move"))
                return "🚚 " + cleanMessage;
            // Network operations
            else if (cleanMessage.Contains("Network") || cleanMessage.Contains("PortGroup"))
                return "🌐 " + cleanMessage;
                
            return cleanMessage;
        }
    }
    
    // Shortened session ID for display
    public string SessionDisplay => 
        string.IsNullOrEmpty(SessionId) || SessionId.Length < 8 ? SessionId : 
        SessionId.Substring(0, 8);
    
    public string LevelDisplay => Level.ToUpper() switch
    {
        "ERROR" => "ERR",
        "CRITICAL" => "CRIT",
        "WARNING" => "WARN",
        "SUCCESS" => "OK",
        "DEBUG" => "DBG",
        "INFO" => "INFO",
        _ => Level.ToUpper().Length > 4 ? Level.ToUpper().Substring(0, 4) : Level.ToUpper()
    };
    
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