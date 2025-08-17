using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

public class PowerShellLoggingService
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

    public PowerShellLoggingService (ILogger<PowerShellLoggingService> logger)
        {
        _logger = logger;

        // Create PowerShell logs directory
        _logDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Logs",
            "PowerShell"
        );

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

        if (!string.IsNullOrEmpty(summary))
            {
            entry.Message += $" - {summary}";
            }

        WriteLog(entry);

        // Write session footer to file
        lock (_fileLock)
            {
            _currentStreamWriter.WriteLine($"----------------------------------------");
            _currentStreamWriter.WriteLine($"[SESSION END] {sessionId} - {scriptName}");
            _currentStreamWriter.WriteLine($"Status: {(success ? "SUCCESS" : "FAILED")}");
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
        // Add to buffer for activity viewer
        _logBuffer.Enqueue(entry);

        // Keep buffer size manageable (last 1000 entries)
        while (_logBuffer.Count > 1000)
            {
            _logBuffer.TryDequeue(out _);
            }

        // Write to file
        lock (_fileLock)
            {
            // Check if we need to roll over to a new daily file
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            if (!_currentLogFile.Contains(currentDate))
                {
                InitializeDailyLogFile();
                }

            // Format: [TIMESTAMP] [LEVEL] [SESSION] [SOURCE] Message
            var logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level,-5}] [{entry.SessionId}] [{entry.Source}] {entry.Message}";
            _currentStreamWriter.WriteLine(logLine);
            }

        // Also log to application logger for debugging
        _logger.LogDebug("PS Script Log: {Message}", entry.Message);

        // Raise event for real-time monitoring
        LogEntryAdded?.Invoke(this, entry);
        }

    public void WriteScriptOutput (string sessionId, string scriptName, string output, string level = "OUTPUT")
        {
        if (string.IsNullOrWhiteSpace(output)) return;

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
            {
            var entry = new LogEntry
                {
                Timestamp = DateTime.Now,
                Level = level,
                Source = "SCRIPT",
                ScriptName = scriptName,
                SessionId = sessionId,
                Message = line
                };

            WriteLog(entry);
            }
        }

    public void WriteScriptError (string sessionId, string scriptName, string error)
        {
        WriteScriptOutput(sessionId, scriptName, error, "ERROR");
        }

    public ConcurrentQueue<LogEntry> GetRecentLogs ()
        {
        return _logBuffer;
        }

    public async Task<string> GetTodaysLogContent ()
        {
        try
            {
            lock (_fileLock)
                {
                _currentStreamWriter?.Flush();
                }

            // Read file with sharing enabled
            using var fileStream = new FileStream(_currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            return await reader.ReadToEndAsync();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error reading log file");
            return $"Error reading log file: {ex.Message}";
            }
        }

    public string[] GetAvailableLogFiles ()
        {
        try
            {
            return Directory.GetFiles(_logDirectory, "powershell_*.log")
                .OrderByDescending(f => f)
                .ToArray();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error getting log files");
            return Array.Empty<string>();
            }
        }

    public async Task<string> GetLogFileContent (string fileName)
        {
        try
            {
            var filePath = Path.Combine(_logDirectory, fileName);
            if (!File.Exists(filePath)) return "File not found";

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            return await reader.ReadToEndAsync();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error reading log file {FileName}", fileName);
            return $"Error reading file: {ex.Message}";
            }
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