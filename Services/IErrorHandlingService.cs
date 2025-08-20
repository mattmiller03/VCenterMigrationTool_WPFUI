using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

public interface IErrorHandlingService
{
    Task<ErrorHandlingResult> HandleScriptErrorAsync(string scriptPath, string error, Dictionary<string, object> context);
    Task<bool> ShouldRetryOperationAsync(string operation, int attemptCount, Exception exception);
    UserFriendlyError TranslateError(string technicalError, string operation = "");
    Task ShowErrorDialogAsync(UserFriendlyError error);
    Task<ValidationResult> ValidateOperationAsync(string operation, Dictionary<string, object> parameters);
    ErrorStatistics GetErrorStatistics(DateTime? since = null);
    Task LogStructuredErrorAsync(string operation, Exception exception, Dictionary<string, object> context);
}

public class ErrorHandlingResult
{
    public bool Success { get; set; }
    public string UserMessage { get; set; } = "";
    public string TechnicalDetails { get; set; } = "";
    public ErrorSeverity Severity { get; set; }
    public ErrorCategory Category { get; set; }
    public bool CanRetry { get; set; }
    public List<string> SuggestedActions { get; set; } = new();
    public TimeSpan? RetryDelay { get; set; }
}

public class UserFriendlyError
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Details { get; set; } = "";
    public ErrorSeverity Severity { get; set; }
    public List<string> SuggestedActions { get; set; } = new();
    public bool CanRetry { get; set; }
    public string Operation { get; set; } = "";
}

public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical,
    Fatal
}

public enum ErrorCategory
{
    Connection,
    Authentication,
    PowerCLI,
    Configuration,
    Network,
    Permission,
    Resource,
    Timeout,
    Script,
    Validation,
    Unknown
}

public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationIssue> Errors { get; } = new();
    public List<ValidationIssue> Warnings { get; } = new();

    public void AddError(string category, string message, string suggestion = "")
    {
        Errors.Add(new ValidationIssue { Category = category, Message = message, Suggestion = suggestion, IsError = true });
    }

    public void AddWarning(string category, string message, string suggestion = "")
    {
        Warnings.Add(new ValidationIssue { Category = category, Message = message, Suggestion = suggestion, IsError = false });
    }
}

public class ValidationIssue
{
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public bool IsError { get; set; }
}

public class ErrorStatistics
{
    public int TotalErrors { get; set; }
    public int CriticalErrors { get; set; }
    public int ConnectionErrors { get; set; }
    public int AuthenticationErrors { get; set; }
    public int PowerCLIErrors { get; set; }
    public Dictionary<string, int> ErrorsByCategory { get; set; } = new();
    public Dictionary<string, int> ErrorsByOperation { get; set; } = new();
    public DateTime LastError { get; set; }
    public string MostCommonError { get; set; } = "";
}