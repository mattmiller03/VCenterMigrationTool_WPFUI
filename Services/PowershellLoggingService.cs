using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.Services;

public class PowerShellLoggingService : IDisposable
    {
    private readonly ILogger<PowerShellLoggingService> _logger;
    private readonly string _logDirectory;
    private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
    private readonly object _fileLock = new();
    private string _currentLogFile;
    private StreamWriter _currentStreamWriter;

    public class LogEntry
        {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string ScriptName { get; set; }
        public string SessionId { get; set; }
        }

    public event EventHandler<LogEntry> LogEntryAdded;

    public PowerShellLoggingService (ILogger<PowerShellLoggingService> logger, ConfigurationService configurationService)
        {
        _logger = logger;

        // Create PowerShell logs directory using the same logic as App.xaml.cs
        var appConfig = configurationService.GetConfiguration();

        // Use the configured log path or fallback to default (same logic as App.xaml.cs)
        var baseLogPath = !string.IsNullOrEmpty(appConfig.LogPath)
            ? appConfig.LogPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs");

        // Create PowerShell subdirectory within the configured log path
        _logDirectory = Path.Combine(baseLogPath, "PowerShell");

        if (!Directory.Exists(_logDirectory))
            {
            Directory.CreateDirectory(_logDirectory);
            }

        // Initialize daily log file
        InitializeDailyLogFile();
        }

    private void InitializeDailyLogFile ()
        {
        lock (_fileLock)
            {
            // Close existing writer if any
            _currentStreamWriter?.Close();
            _currentStreamWriter?.Dispose();

            // Create new daily log file
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _currentLogFile = Path.Combine(_logDirectory, $"powershell_{date}.log");

            // Open file in append mode
            _currentStreamWriter = new StreamWriter(_currentLogFile, append: true, Encoding.UTF8)
                {
                AutoFlush = true
                };

            WriteLogHeader();
            }
        }

    private void WriteLogHeader ()
        {
        _currentStreamWriter.WriteLine($"");
        _currentStreamWriter.WriteLine($"=================================================================");
        _currentStreamWriter.WriteLine($"PowerShell Script Execution Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _currentStreamWriter.WriteLine($"=================================================================");
        _currentStreamWriter.WriteLine($"");
        }

    public string StartScriptLogging (string scriptName, string connectionType = "source")
        {
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var entry = new LogEntry
            {
            Timestamp = DateTime.Now,
            Level = "INFO",
            Source = "SYSTEM",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = $"Starting script execution: {scriptName} on {connectionType} connection"
            };

        WriteLog(entry);

        // Write session header to file
        lock (_fileLock)
            {
            _currentStreamWriter.WriteLine($"");
            _currentStreamWriter.WriteLine($"[SESSION START] {sessionId} - {scriptName}");
            _currentStreamWriter.WriteLine($"Connection: {connectionType}");
            _currentStreamWriter.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _currentStreamWriter.WriteLine($"----------------------------------------");
            }

        return sessionId;
        }

    public void EndScriptLogging (string sessionId, string scriptName, bool success, string summary = null)
        {
        var entry = new LogEntry
            {
            Timestamp = DateTime.Now,
            Level = success ? "INFO" : "ERROR",
            Source = "SYSTEM",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = $"Script execution completed: {scriptName} - {(success ? "SUCCESS" : "FAILED")}"
            };

        WriteLog(entry);

        // Write session footer to file
        lock (_fileLock)
            {
            _currentStreamWriter.WriteLine($"----------------------------------------");
            _currentStreamWriter.WriteLine($"[SESSION END] {sessionId} - {scriptName}");
            _currentStreamWriter.WriteLine($"Result: {(success ? "SUCCESS" : "FAILED")}");
            if (!string.IsNullOrEmpty(summary))
                {
                _currentStreamWriter.WriteLine($"Summary: {summary}");
                }
            _currentStreamWriter.WriteLine($"Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _currentStreamWriter.WriteLine($"");
            }
        }

    public void WriteLog (LogEntry entry)
        {
        // Add to buffer for real-time viewing
        _logBuffer.Enqueue(entry);

        // Keep buffer size reasonable
        while (_logBuffer.Count > 1000)
            {
            _logBuffer.TryDequeue(out _);
            }

        // Write to file
        lock (_fileLock)
            {
            // Check if we need a new daily file
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_currentLogFile.Contains(currentDate))
                {
                InitializeDailyLogFile();
                }

            var logLine = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.SessionId}] [{entry.ScriptName}] {entry.Message}";
            _currentStreamWriter.WriteLine(logLine);
            }

        // Also log to application logger for critical entries
        if (entry.Level == "ERROR" || entry.Level == "CRITICAL")
            {
            _logger.LogError("[PowerShell] [{SessionId}] [{ScriptName}] {Message}",
                entry.SessionId, entry.ScriptName, entry.Message);
            }

        // Raise event for real-time monitoring
        LogEntryAdded?.Invoke(this, entry);
        }

    public void LogScriptOutput (string sessionId, string scriptName, string output, string level = "INFO")
        {
        if (string.IsNullOrWhiteSpace(output)) return;

        var entry = new LogEntry
            {
            Timestamp = DateTime.Now,
            Level = level,
            Source = "SCRIPT",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = output.Trim()
            };

        WriteLog(entry);
        }

    public void LogScriptError (string sessionId, string scriptName, string error)
        {
        var entry = new LogEntry
            {
            Timestamp = DateTime.Now,
            Level = "ERROR",
            Source = "SCRIPT",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = error.Trim()
            };

        WriteLog(entry);
        }

    public void LogParameters (string sessionId, string scriptName, Dictionary<string, object> parameters)
        {
        var safeParams = parameters.Where(p => !IsSensitiveParameter(p.Key))
                                 .Select(p => $"{p.Key}={p.Value}")
                                 .ToList();

        if (safeParams.Any())
            {
            var entry = new LogEntry
                {
                Timestamp = DateTime.Now,
                Level = "DEBUG",
                Source = "PARAMS",
                ScriptName = scriptName,
                SessionId = sessionId,
                Message = $"Parameters: {string.Join(", ", safeParams)}"
                };

            WriteLog(entry);
            }
        }

    private bool IsSensitiveParameter (string paramName)
        {
        var sensitiveParams = new[] { "password", "pwd", "secret", "key", "token", "credential" };
        return sensitiveParams.Any(s => paramName.ToLower().Contains(s));
        }

    // Methods for future Activity Page
    public List<LogEntry> GetTodaysLogs ()
        {
        return _logBuffer.Where(e => e.Timestamp.Date == DateTime.Today).ToList();
        }

    public List<LogEntry> GetRecentLogs (int count = 100)
        {
        return _logBuffer.TakeLast(count).ToList();
        }

    public string GetCurrentLogFilePath ()
        {
        return _currentLogFile;
        }

    public List<string> GetAvailableLogFiles ()
        {
        return Directory.GetFiles(_logDirectory, "powershell_*.log")
                       .OrderByDescending(f => f)
                       .ToList();
        }

    public async Task<string> ReadLogFileAsync (string filePath)
        {
        if (!File.Exists(filePath)) return string.Empty;

        return await File.ReadAllTextAsync(filePath);
        }

    public void Dispose ()
        {
        lock (_fileLock)
            {
            _currentStreamWriter?.Close();
            _currentStreamWriter?.Dispose();
            }
        }
    }