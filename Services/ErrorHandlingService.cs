using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

public class ErrorHandlingService : IErrorHandlingService
{
    private readonly ILogger<ErrorHandlingService> _logger;
    private readonly IDialogService _dialogService;
    private readonly PowerShellLoggingService _powerShellLoggingService;
    private readonly ConfigurationService _configurationService;
    private readonly List<StructuredError> _recentErrors = new();
    private readonly object _errorsLock = new();

    public ErrorHandlingService(
        ILogger<ErrorHandlingService> logger,
        IDialogService dialogService,
        PowerShellLoggingService powerShellLoggingService,
        ConfigurationService configurationService)
    {
        _logger = logger;
        _dialogService = dialogService;
        _powerShellLoggingService = powerShellLoggingService;
        _configurationService = configurationService;
    }

    public async Task<ErrorHandlingResult> HandleScriptErrorAsync(string scriptPath, string error, Dictionary<string, object> context)
    {
        var analysis = AnalyzeError(error);
        var userFriendlyError = TranslateError(error, Path.GetFileName(scriptPath));

        // Log structured error
        await LogStructuredErrorAsync(Path.GetFileName(scriptPath), new Exception(error), context);

        // Create result
        var result = new ErrorHandlingResult
        {
            Success = false,
            UserMessage = userFriendlyError.Message,
            TechnicalDetails = error,
            Severity = analysis.Severity,
            Category = analysis.Category,
            CanRetry = analysis.CanRetry,
            SuggestedActions = analysis.SuggestedActions,
            RetryDelay = analysis.RetryDelay
        };

        // Show dialog for critical errors
        if (analysis.Severity >= ErrorSeverity.Error)
        {
            await ShowErrorDialogAsync(userFriendlyError);
        }

        return result;
    }

    public UserFriendlyError TranslateError(string technicalError, string operation = "")
    {
        var analysis = AnalyzeError(technicalError);

        return new UserFriendlyError
        {
            Title = GetErrorTitle(analysis.Category, analysis.Severity),
            Message = GetUserFriendlyMessage(analysis.Category, technicalError),
            Details = SanitizeTechnicalDetails(technicalError),
            Severity = analysis.Severity,
            SuggestedActions = analysis.SuggestedActions,
            CanRetry = analysis.CanRetry,
            Operation = operation
        };
    }

    private (ErrorSeverity Severity, ErrorCategory Category, bool CanRetry, List<string> SuggestedActions, TimeSpan? RetryDelay) AnalyzeError(string error)
    {
        var errorLower = error.ToLower();

        // Connection errors
        if (IsConnectionError(errorLower))
        {
            return (ErrorSeverity.Error, ErrorCategory.Connection, true,
                new List<string>
                {
                    "Check network connectivity to vCenter server",
                    "Verify vCenter server is running and accessible",
                    "Check firewall settings and port access",
                    "Try connecting again in a few moments"
                }, TimeSpan.FromSeconds(30));
        }

        // Authentication errors
        if (IsAuthenticationError(errorLower))
        {
            return (ErrorSeverity.Error, ErrorCategory.Authentication, true,
                new List<string>
                {
                    "Verify username and password are correct",
                    "Check if account is locked or disabled",
                    "Ensure account has sufficient permissions",
                    "Try re-entering credentials"
                }, null);
        }

        // PowerCLI errors
        if (IsPowerCLIError(errorLower))
        {
            return (ErrorSeverity.Warning, ErrorCategory.PowerCLI, true,
                new List<string>
                {
                    "Install PowerCLI: Install-Module VMware.PowerCLI -Force",
                    "Update PowerCLI to latest version",
                    "Check PowerShell execution policy",
                    "Run PowerShell as Administrator"
                }, TimeSpan.FromSeconds(10));
        }

        // Permission errors
        if (IsPermissionError(errorLower))
        {
            return (ErrorSeverity.Error, ErrorCategory.Permission, false,
                new List<string>
                {
                    "Contact vCenter administrator for permission review",
                    "Ensure account has required roles assigned",
                    "Check object-level permissions",
                    "Verify privilege inheritance settings"
                }, null);
        }

        // Timeout errors
        if (IsTimeoutError(errorLower))
        {
            return (ErrorSeverity.Warning, ErrorCategory.Timeout, true,
                new List<string>
                {
                    "Try the operation again",
                    "Check network latency to vCenter",
                    "Consider breaking operation into smaller parts",
                    "Increase timeout settings if available"
                }, TimeSpan.FromSeconds(60));
        }

        // Resource errors
        if (IsResourceError(errorLower))
        {
            return (ErrorSeverity.Error, ErrorCategory.Resource, false,
                new List<string>
                {
                    "Verify the resource exists and is accessible",
                    "Check resource name spelling and path",
                    "Ensure you have access to the resource",
                    "Refresh the resource list and try again"
                }, null);
        }

        // Script/Configuration errors
        if (IsScriptError(errorLower))
        {
            return (ErrorSeverity.Critical, ErrorCategory.Script, false,
                new List<string>
                {
                    "Check PowerShell execution policy",
                    "Verify script file exists and is accessible",
                    "Run application as Administrator",
                    "Check script syntax and parameters"
                }, null);
        }

        // Default classification
        return (ErrorSeverity.Error, ErrorCategory.Unknown, true,
            new List<string>
            {
                "Review the technical details below",
                "Check application logs for more information",
                "Try the operation again",
                "Contact support if problem persists"
            }, TimeSpan.FromSeconds(30));
    }

    private bool IsConnectionError(string error) =>
        error.Contains("could not connect") ||
        error.Contains("connection refused") ||
        error.Contains("network unreachable") ||
        error.Contains("timeout") ||
        error.Contains("connection lost") ||
        error.Contains("server not found") ||
        error.Contains("connection failed");

    private bool IsAuthenticationError(string error) =>
        error.Contains("invalid credentials") ||
        error.Contains("login failed") ||
        error.Contains("authentication") ||
        error.Contains("unauthorized") ||
        error.Contains("access denied") ||
        error.Contains("incorrect password") ||
        error.Contains("bad credentials");

    private bool IsPowerCLIError(string error) =>
        error.Contains("powercli") ||
        error.Contains("vmware.powercli") ||
        error.Contains("connect-viserver") ||
        error.Contains("module 'vmware") ||
        error.Contains("import-module") ||
        error.Contains("powershell module");

    private bool IsPermissionError(string error) =>
        error.Contains("permission") ||
        error.Contains("insufficient privileges") ||
        error.Contains("not authorized") ||
        error.Contains("access denied") ||
        error.Contains("forbidden");

    private bool IsTimeoutError(string error) =>
        error.Contains("timeout") ||
        error.Contains("timed out") ||
        error.Contains("operation timeout") ||
        error.Contains("request timeout");

    private bool IsResourceError(string error) =>
        error.Contains("not found") ||
        error.Contains("does not exist") ||
        error.Contains("invalid path") ||
        error.Contains("file not found") ||
        error.Contains("object not found") ||
        error.Contains("resource not found");

    private bool IsScriptError(string error) =>
        error.Contains("syntax error") ||
        error.Contains("parse error") ||
        error.Contains("execution policy") ||
        error.Contains("cannot load") ||
        error.Contains("script not found") ||
        error.Contains("powershell error");

    private string GetErrorTitle(ErrorCategory category, ErrorSeverity severity)
    {
        var severityText = severity switch
        {
            ErrorSeverity.Critical => "Critical Error",
            ErrorSeverity.Error => "Error",
            ErrorSeverity.Warning => "Warning",
            _ => "Information"
        };

        var categoryText = category switch
        {
            ErrorCategory.Connection => "Connection Problem",
            ErrorCategory.Authentication => "Authentication Issue",
            ErrorCategory.PowerCLI => "PowerCLI Issue",
            ErrorCategory.Permission => "Permission Problem",
            ErrorCategory.Timeout => "Operation Timeout",
            ErrorCategory.Resource => "Resource Not Found",
            ErrorCategory.Script => "Script Error",
            _ => "Operation Error"
        };

        return $"{severityText} - {categoryText}";
    }

    private string GetUserFriendlyMessage(ErrorCategory category, string technicalError)
    {
        return category switch
        {
            ErrorCategory.Connection => "Unable to connect to the vCenter server. Please check your network connection and server availability.",
            ErrorCategory.Authentication => "Authentication failed. Please verify your username and password are correct.",
            ErrorCategory.PowerCLI => "PowerCLI is not properly installed or configured. This is required for vCenter operations.",
            ErrorCategory.Permission => "You don't have sufficient permissions to perform this operation. Contact your vCenter administrator.",
            ErrorCategory.Timeout => "The operation took too long to complete and was cancelled. This may be due to network latency or server load.",
            ErrorCategory.Resource => "The requested resource could not be found. It may have been moved, deleted, or renamed.",
            ErrorCategory.Script => "There was an error in the PowerShell script execution. This may be due to configuration or system settings.",
            _ => "An unexpected error occurred during the operation."
        };
    }

    private string SanitizeTechnicalDetails(string technicalError)
    {
        // Remove any sensitive information from technical details
        var sanitized = technicalError;

        // Remove password patterns
        sanitized = Regex.Replace(sanitized, @"password[=:\s]+[^\s]+", "password=***", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"-password\s+[^\s]+", "-password ***", RegexOptions.IgnoreCase);

        // Limit length for UI display
        if (sanitized.Length > 500)
        {
            sanitized = sanitized.Substring(0, 500) + "... (truncated)";
        }

        return sanitized;
    }

    public async Task<bool> ShouldRetryOperationAsync(string operation, int attemptCount, Exception exception)
    {
        // Don't retry after 3 attempts
        if (attemptCount >= 3) return false;

        var analysis = AnalyzeError(exception.Message);

        // Don't retry permission or authentication errors
        if (analysis.Category == ErrorCategory.Permission ||
            analysis.Category == ErrorCategory.Authentication)
        {
            return false;
        }

        // Retry connection and timeout errors
        if (analysis.Category == ErrorCategory.Connection ||
            analysis.Category == ErrorCategory.Timeout)
        {
            // Add exponential backoff delay
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptCount));
            await Task.Delay(delay);
            return true;
        }

        return analysis.CanRetry && attemptCount < 2;
    }

    public async Task ShowErrorDialogAsync(UserFriendlyError error)
    {
        await Task.Run(() =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var errorDialog = new Views.Dialogs.ErrorDialog
                {
                    Title = error.Title,
                    Owner = Application.Current.MainWindow
                };

                var viewModel = new ViewModels.Dialogs.ErrorDialogViewModel
                {
                    Title = error.Title,
                    Message = error.Message,
                    Details = error.Details,
                    SuggestedActions = error.SuggestedActions,
                    CanRetry = error.CanRetry,
                    Severity = error.Severity
                };

                errorDialog.DataContext = viewModel;
                errorDialog.ShowDialog();
            });
        });
    }

    public async Task<ValidationResult> ValidateOperationAsync(string operation, Dictionary<string, object> parameters)
    {
        var result = new ValidationResult();

        try
        {
            _logger.LogInformation("Validating operation: {Operation}", operation);

            // Basic parameter validation
            if (parameters == null || parameters.Count == 0)
            {
                result.AddError("Parameters", "No parameters provided for operation");
                return result;
            }

            // Connection validation
            if (parameters.ContainsKey("VCenterServer"))
            {
                var server = parameters["VCenterServer"]?.ToString();
                if (string.IsNullOrEmpty(server))
                {
                    result.AddError("Connection", "vCenter server address is required");
                }
            }

            // Credential validation
            if (parameters.ContainsKey("Username"))
            {
                var username = parameters["Username"]?.ToString();
                if (string.IsNullOrEmpty(username))
                {
                    result.AddError("Authentication", "Username is required");
                }
            }

            // Script path validation
            if (parameters.ContainsKey("ScriptPath"))
            {
                var scriptPath = parameters["ScriptPath"]?.ToString();
                if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
                {
                    result.AddError("Script", "Script file not found or invalid path");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during operation validation");
            result.AddError("Validation", $"Validation failed: {ex.Message}");
            return result;
        }
    }

    public ErrorStatistics GetErrorStatistics(DateTime? since = null)
    {
        var sinceDate = since ?? DateTime.Today;

        lock (_errorsLock)
        {
            var recentErrors = _recentErrors.Where(e => e.Timestamp >= sinceDate).ToList();

            var stats = new ErrorStatistics
            {
                TotalErrors = recentErrors.Count,
                CriticalErrors = recentErrors.Count(e => e.Severity == ErrorSeverity.Critical),
                ConnectionErrors = recentErrors.Count(e => e.Category == ErrorCategory.Connection),
                AuthenticationErrors = recentErrors.Count(e => e.Category == ErrorCategory.Authentication),
                PowerCLIErrors = recentErrors.Count(e => e.Category == ErrorCategory.PowerCLI),
                LastError = recentErrors.LastOrDefault()?.Timestamp ?? DateTime.MinValue,
                MostCommonError = recentErrors
                    .GroupBy(e => e.Message)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "None"
            };

            // Group by category
            stats.ErrorsByCategory = recentErrors
                .GroupBy(e => e.Category.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Group by operation
            stats.ErrorsByOperation = recentErrors
                .GroupBy(e => e.Operation)
                .ToDictionary(g => g.Key, g => g.Count());

            return stats;
        }
    }

    public async Task LogStructuredErrorAsync(string operation, Exception exception, Dictionary<string, object> context)
    {
        var structuredError = new StructuredError
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Operation = operation,
            Exception = exception.GetType().Name,
            Message = exception.Message,
            StackTrace = exception.StackTrace ?? "",
            Context = context,
            Analysis = AnalyzeError(exception.Message)
        };

        lock (_errorsLock)
        {
            _recentErrors.Add(structuredError);

            // Keep only recent errors (last 100)
            while (_recentErrors.Count > 100)
            {
                _recentErrors.RemoveAt(0);
            }
        }

        // Log to application logger
        _logger.LogError(exception,
            "Operation: {Operation}, Analysis: {@Analysis}, Context: {@Context}",
            operation, structuredError.Analysis, context);

        // Log to PowerShell logging service for dashboard integration
        _powerShellLoggingService.LogScriptError(
            structuredError.Id,
            operation,
            $"{exception.GetType().Name}: {exception.Message}");
    }

    public async Task<ValidationResult> ValidateOperationAsync(string operation, Dictionary<string, object> parameters)
    {
        var result = new ValidationResult();

        try
        {
            _logger.LogInformation("Validating operation: {Operation}", operation);

            // Basic parameter validation
            if (parameters == null || parameters.Count == 0)
            {
                result.AddError("Parameters", "No parameters provided for operation",
                    "Ensure required parameters are provided before executing the operation");
                return result;
            }

            // Connection validation
            if (parameters.ContainsKey("VCenterServer"))
            {
                var server = parameters["VCenterServer"]?.ToString();
                if (string.IsNullOrEmpty(server))
                {
                    result.AddError("Connection", "vCenter server address is required",
                        "Provide a valid vCenter server address or FQDN");
                }
                else
                {
                    // Basic format validation
                    if (!IsValidServerAddress(server))
                    {
                        result.AddWarning("Connection", $"Server address '{server}' may not be valid",
                            "Ensure the server address is a valid hostname, FQDN, or IP address");
                    }
                }
            }

            // Credential validation
            if (parameters.ContainsKey("Username"))
            {
                var username = parameters["Username"]?.ToString();
                if (string.IsNullOrEmpty(username))
                {
                    result.AddError("Authentication", "Username is required",
                        "Provide a valid username for vCenter authentication");
                }
            }

            if (parameters.ContainsKey("Password"))
            {
                var password = parameters["Password"]?.ToString();
                if (string.IsNullOrEmpty(password))
                {
                    result.AddError("Authentication", "Password is required",
                        "Provide a valid password for vCenter authentication");
                }
            }

            // Script path validation
            if (parameters.ContainsKey("ScriptPath"))
            {
                var scriptPath = parameters["ScriptPath"]?.ToString();
                if (string.IsNullOrEmpty(scriptPath))
                {
                    result.AddError("Script", "Script path is required",
                        "Specify a valid path to the PowerShell script");
                }
                else if (!File.Exists(scriptPath))
                {
                    result.AddError("Script", $"Script file not found: {scriptPath}",
                        "Ensure the script file exists and the path is correct");
                }
            }

            // Log path validation
            if (parameters.ContainsKey("LogPath"))
            {
                var logPath = parameters["LogPath"]?.ToString();
                if (!string.IsNullOrEmpty(logPath))
                {
                    var logDirectory = Path.GetDirectoryName(logPath);
                    if (!Directory.Exists(logDirectory))
                    {
                        result.AddWarning("Logging", $"Log directory does not exist: {logDirectory}",
                            "The log directory will be created automatically, or specify an existing directory");
                    }
                }
            }

            // Operation-specific validation
            await ValidateOperationSpecificRequirements(operation, parameters, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during operation validation");
            result.AddError("Validation", $"Validation failed: {ex.Message}",
                "Review the operation parameters and try again");
            return result;
        }
    }

    private async Task ValidateOperationSpecificRequirements(string operation, Dictionary<string, object> parameters, ValidationResult result)
    {
        switch (operation.ToLower())
        {
            case "vm backup":
            case "vm migration":
                ValidateVMOperationRequirements(parameters, result);
                break;

            case "host backup":
            case "host migration":
                ValidateHostOperationRequirements(parameters, result);
                break;

            case "network migration":
                ValidateNetworkOperationRequirements(parameters, result);
                break;

            case "resource pool migration":
                ValidateResourcePoolOperationRequirements(parameters, result);
                break;
        }
    }

    private void ValidateVMOperationRequirements(Dictionary<string, object> parameters, ValidationResult result)
    {
        // VM-specific validation
        if (parameters.ContainsKey("VMNames"))
        {
            var vmNames = parameters["VMNames"];
            if (vmNames is string[] vmArray && vmArray.Length == 0)
            {
                result.AddError("VM Selection", "No VMs selected for operation",
                    "Select at least one VM to perform the operation");
            }
        }

        if (parameters.ContainsKey("BackupFilePath"))
        {
            var backupPath = parameters["BackupFilePath"]?.ToString();
            if (!string.IsNullOrEmpty(backupPath))
            {
                var directory = Path.GetDirectoryName(backupPath);
                if (!Directory.Exists(directory))
                {
                    result.AddWarning("Backup Path", $"Backup directory does not exist: {directory}",
                        "The directory will be created automatically, or specify an existing path");
                }
            }
        }
    }

    private void ValidateHostOperationRequirements(Dictionary<string, object> parameters, ValidationResult result)
    {
        // Host-specific validation
        if (parameters.ContainsKey("HostNames"))
        {
            var hostNames = parameters["HostNames"];
            if (hostNames is string[] hostArray && hostArray.Length == 0)
            {
                result.AddError("Host Selection", "No ESXi hosts selected for operation",
                    "Select at least one ESXi host to perform the operation");
            }
        }
    }

    private void ValidateNetworkOperationRequirements(Dictionary<string, object> parameters, ValidationResult result)
    {
        // Network-specific validation
        if (parameters.ContainsKey("NetworkMappings"))
        {
            // Validate network mappings are provided for migration
            result.AddWarning("Network Mapping", "Ensure network mappings are configured correctly",
                "Review source to target network mappings before proceeding");
        }
    }

    private void ValidateResourcePoolOperationRequirements(Dictionary<string, object> parameters, ValidationResult result)
    {
        // Resource pool specific validation
        if (parameters.ContainsKey("SourceCluster") && parameters.ContainsKey("TargetCluster"))
        {
            var sourceCluster = parameters["SourceCluster"]?.ToString();
            var targetCluster = parameters["TargetCluster"]?.ToString();

            if (string.Equals(sourceCluster, targetCluster, StringComparison.OrdinalIgnoreCase))
            {
                result.AddWarning("Cluster Selection", "Source and target clusters are the same",
                    "Ensure you want to perform the operation within the same cluster");
            }
        }
    }

    private bool IsValidServerAddress(string serverAddress)
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
            return false;

        // Basic validation - could be enhanced with regex for IP/FQDN validation
        return !serverAddress.Contains(" ") &&
               serverAddress.Length > 3 &&
               !serverAddress.StartsWith(".") &&
               !serverAddress.EndsWith(".");
    }

    public ErrorStatistics GetErrorStatistics(DateTime? since = null)
    {
        var sinceDate = since ?? DateTime.Today;

        lock (_errorsLock)
        {
            var recentErrors = _recentErrors.Where(e => e.Timestamp >= sinceDate).ToList();

            var stats = new ErrorStatistics
            {
                TotalErrors = recentErrors.Count,
                CriticalErrors = recentErrors.Count(e => e.Analysis.Severity == ErrorSeverity.Critical),
                ConnectionErrors = recentErrors.Count(e => e.Analysis.Category == ErrorCategory.Connection),
                AuthenticationErrors = recentErrors.Count(e => e.Analysis.Category == ErrorCategory.Authentication),
                PowerCLIErrors = recentErrors.Count(e => e.Analysis.Category == ErrorCategory.PowerCLI),
                LastError = recentErrors.LastOrDefault()?.Timestamp ?? DateTime.MinValue,
                MostCommonError = recentErrors
                    .GroupBy(e => e.Message)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key ?? "None"
            };

            // Group by category
            stats.ErrorsByCategory = recentErrors
                .GroupBy(e => e.Analysis.Category.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Group by operation
            stats.ErrorsByOperation = recentErrors
                .GroupBy(e => e.Operation)
                .ToDictionary(g => g.Key, g => g.Count());

            return stats;
        }
    }
}

// Supporting classes
public class StructuredError
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Operation { get; set; } = "";
    public string Exception { get; set; } = "";
    public string Message { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public Dictionary<string, object> Context { get; set; } = new();
    public (ErrorSeverity Severity, ErrorCategory Category, bool CanRetry, List<string> SuggestedActions, TimeSpan? RetryDelay) Analysis { get; set; }
}