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
    private readonly ConfigurationService _configurationService;
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
        _configurationService = configurationService;

        // Get the configured log path from ConfigurationService
        var configuredLogPath = _configurationService.GetConfiguration().LogPath;
        string baseLogDirectory;

        try
            {
            // Check if the configured path is a directory or file path
            if (Directory.Exists(configuredLogPath))
                {
                // It's already a directory
                baseLogDirectory = configuredLogPath;
                _logger.LogDebug("Using configured log directory: {LogPath}", configuredLogPath);
                }
            else if (File.Exists(configuredLogPath) || Path.HasExtension(configuredLogPath))
                {
                // It's a file path, extract the directory
                baseLogDirectory = Path.GetDirectoryName(configuredLogPath);
                _logger.LogDebug("Extracted directory from configured log file path: {LogPath} -> {Directory}",
                    configuredLogPath, baseLogDirectory);
                }
            else
                {
                // Treat it as a directory path (even if it doesn't exist yet)
                baseLogDirectory = configuredLogPath;
                _logger.LogDebug("Treating configured path as directory: {LogPath}", configuredLogPath);
                }

            if (string.IsNullOrEmpty(baseLogDirectory))
                {
                throw new InvalidOperationException("Could not determine base log directory");
                }
            }
        catch (Exception ex)
            {
            _logger.LogWarning(ex, "Could not determine log directory from configured path: {LogPath}, using fallback", configuredLogPath);
            // Fallback to default
            baseLogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs");
            }

        // Create PowerShell subdirectory within the configured log path
        _logDirectory = Path.Combine(baseLogDirectory, "PowerShell");

        _logger.LogInformation("Configured log path: {ConfiguredPath}", configuredLogPath);
        _logger.LogInformation("Base log directory: {BaseDirectory}", baseLogDirectory);
        _logger.LogInformation("PowerShell logs will be written to: {LogDirectory}", _logDirectory);

        if (!Directory.Exists(_logDirectory))
            {
            Directory.CreateDirectory(_logDirectory);
            _logger.LogInformation("Created PowerShell log directory: {LogDirectory}", _logDirectory);
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

        // Don't duplicate PowerShell logs to application logger
        // PowerShell scripts have their own logs in the PowerShell folder

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
                Message = $"Script parameters: {string.Join(", ", safeParams)}"
                };

            WriteLog(entry);
            }

        // Always log that parameters were provided (even if all sensitive)
        var totalParams = parameters.Count;
        var sensitiveParams = parameters.Count(p => IsSensitiveParameter(p.Key));

        if (sensitiveParams > 0)
            {
            var paramSummary = new LogEntry
                {
                Timestamp = DateTime.Now,
                Level = "DEBUG",
                Source = "PARAMS",
                ScriptName = scriptName,
                SessionId = sessionId,
                Message = $"Script parameters: {totalParams} total ({sensitiveParams} redacted for security)"
                };

            WriteLog(paramSummary);
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