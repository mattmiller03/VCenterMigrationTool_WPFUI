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
    private readonly Dictionary<string, StreamWriter> _scriptLogWriters = new();
    private readonly Dictionary<string, string> _scriptLogFiles = new();

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
                baseLogDirectory = Path.GetDirectoryName(configuredLogPath) ?? "";
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
        }

    private StreamWriter CreateScriptLogFile (string sessionId, string scriptName)
        {
        lock (_fileLock)
            {
            // Create individual log file for this script execution
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var safeScriptName = Path.GetFileNameWithoutExtension(scriptName).Replace(" ", "_");
            var logFileName = $"{safeScriptName}_{sessionId}_{timestamp}.log";
            var logFilePath = Path.Combine(_logDirectory, logFileName);

            // Store the file path for this session
            _scriptLogFiles[sessionId] = logFilePath;

            // Create and configure the StreamWriter
            var writer = new StreamWriter(logFilePath, append: false, Encoding.UTF8)
                {
                AutoFlush = true
                };

            // Write header for this script execution
            WriteScriptLogHeader(writer, scriptName, sessionId);

            // Store the writer for this session
            _scriptLogWriters[sessionId] = writer;

            return writer;
            }
        }

    private void WriteScriptLogHeader (StreamWriter writer, string scriptName, string sessionId)
        {
        writer.WriteLine($"=================================================================");
        writer.WriteLine($"PowerShell Script Execution Log");
        writer.WriteLine($"Script: {scriptName}");
        writer.WriteLine($"Session ID: {sessionId}");
        writer.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        writer.WriteLine($"=================================================================");
        writer.WriteLine($"");
        }

    public string StartScriptLogging (string scriptName, string connectionType = "source")
        {
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);

        // Create individual log file for this script execution
        var scriptWriter = CreateScriptLogFile(sessionId, scriptName);

        // Write session start info to the individual log file
        scriptWriter.WriteLine($"[SESSION START] {sessionId} - {scriptName}");
        scriptWriter.WriteLine($"Connection: {connectionType}");
        scriptWriter.WriteLine($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        scriptWriter.WriteLine($"----------------------------------------");
        scriptWriter.WriteLine($"");

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

        _logger.LogInformation("Created individual log file for script: {ScriptName} with session: {SessionId} at: {LogPath}", 
            scriptName, sessionId, _scriptLogFiles[sessionId]);

        return sessionId;
        }

    public void EndScriptLogging (string sessionId, string scriptName, bool success, string? summary = null)
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

        // Write session footer to individual script file and close it
        lock (_fileLock)
            {
            if (_scriptLogWriters.TryGetValue(sessionId, out var scriptWriter))
                {
                scriptWriter.WriteLine($"");
                scriptWriter.WriteLine($"----------------------------------------");
                scriptWriter.WriteLine($"[SESSION END] {sessionId} - {scriptName}");
                scriptWriter.WriteLine($"Result: {(success ? "SUCCESS" : "FAILED")}");
                if (!string.IsNullOrEmpty(summary))
                    {
                    scriptWriter.WriteLine($"Summary: {summary}");
                    }
                scriptWriter.WriteLine($"Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                scriptWriter.WriteLine($"=================================================================");

                // Close and dispose the script-specific writer
                scriptWriter.Close();
                scriptWriter.Dispose();
                
                // Remove from tracking dictionaries
                _scriptLogWriters.Remove(sessionId);
                
                _logger.LogInformation("Completed and closed log file for script: {ScriptName} session: {SessionId}", 
                    scriptName, sessionId);
                }
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

        // Write to individual script file if available
        lock (_fileLock)
            {
            if (!string.IsNullOrEmpty(entry.SessionId) && 
                _scriptLogWriters.TryGetValue(entry.SessionId, out var scriptWriter))
                {
                var logLine = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.Source}] {entry.Message}";
                scriptWriter.WriteLine(logLine);
                }
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

    public void LogScriptAction (string sessionId, string scriptName, string action, string details = "", string level = "INFO")
        {
        var message = string.IsNullOrEmpty(details) ? action : $"{action}: {details}";
        
        var entry = new LogEntry
            {
            Timestamp = DateTime.Now,
            Level = level,
            Source = "ACTION",
            ScriptName = scriptName,
            SessionId = sessionId,
            Message = message
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
        // Return the log directory path since we now have individual files
        return _logDirectory;
        }

    public List<string> GetAvailableLogFiles ()
        {
        // Return all individual script log files, sorted by creation time (newest first)
        return Directory.GetFiles(_logDirectory, "*.log")
                       .OrderByDescending(f => File.GetCreationTime(f))
                       .ToList();
        }

    public string? GetScriptLogFilePath (string sessionId)
        {
        return _scriptLogFiles.TryGetValue(sessionId, out var filePath) ? filePath : null;
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
            // Close and dispose all open script log writers
            foreach (var writer in _scriptLogWriters.Values)
                {
                try
                    {
                    writer?.Close();
                    writer?.Dispose();
                    }
                catch (Exception ex)
                    {
                    _logger.LogWarning(ex, "Error disposing script log writer");
                    }
                }
            
            _scriptLogWriters.Clear();
            _scriptLogFiles.Clear();
            }
        }
    }