using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Centralized service for resolving PowerShell script paths consistently across the application.
/// Handles both development and deployed scenarios with proper fallback logic.
/// 
/// This addresses the critical issue where different parts of the codebase used inconsistent
/// path resolution methods, causing "script not found" errors.
/// </summary>
public class ScriptPathService
{
    private readonly ILogger<ScriptPathService> _logger;
    private static string? _cachedScriptsBaseDirectory;

    public ScriptPathService(ILogger<ScriptPathService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the absolute path to a PowerShell script using standardized resolution logic.
    /// 
    /// Priority order:
    /// 1. AppDomain.CurrentDomain.BaseDirectory (for deployed applications)
    /// 2. Assembly location directory (fallback for some deployment scenarios)  
    /// 3. Current working directory (development scenario)
    /// 
    /// Equivalent PowerShell concepts:
    /// - This is like $PSScriptRoot in PowerShell - provides the script's base directory
    /// - Similar to how PowerShell resolves relative paths from the script's location
    /// </summary>
    /// <param name="relativePath">Relative path from Scripts directory (e.g., "Active/Test-VCenterConnection.ps1")</param>
    /// <returns>Absolute path to the script file</returns>
    public string GetScriptPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Script path cannot be null or empty", nameof(relativePath));

        // Normalize path separators for cross-platform compatibility
        relativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                                 .Replace('/', Path.DirectorySeparatorChar);

        var scriptsDirectory = GetScriptsBaseDirectory();
        var fullPath = Path.Combine(scriptsDirectory, relativePath);
        
        _logger.LogDebug("Resolved script path: {RelativePath} -> {FullPath}", relativePath, fullPath);
        
        return fullPath;
    }

    /// <summary>
    /// Gets the absolute path to a script in the Scripts/Active directory.
    /// This is a convenience method for the most common script location.
    /// </summary>
    /// <param name="scriptFileName">Just the script filename (e.g., "Test-VCenterConnection.ps1")</param>
    /// <returns>Absolute path to the script in Scripts/Active</returns>
    public string GetActiveScriptPath(string scriptFileName)
    {
        return GetScriptPath(Path.Combine("Active", scriptFileName));
    }

    /// <summary>
    /// Gets the absolute path to a script in a specific Scripts subdirectory.
    /// </summary>
    /// <param name="subdirectory">Subdirectory under Scripts (e.g., "Active", "Infrastructure Discovery")</param>
    /// <param name="scriptFileName">Script filename (e.g., "Get-Clusters.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public string GetScriptPath(string subdirectory, string scriptFileName)
    {
        return GetScriptPath(Path.Combine(subdirectory, scriptFileName));
    }

    /// <summary>
    /// Gets the absolute path to a script in nested subdirectories.
    /// </summary>
    /// <param name="subdirectory1">First subdirectory under Scripts (e.g., "Active")</param>
    /// <param name="subdirectory2">Second subdirectory (e.g., "Core Migration")</param>
    /// <param name="scriptFileName">Script filename (e.g., "Migrate-VCenterObject.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public string GetScriptPath(string subdirectory1, string subdirectory2, string scriptFileName)
    {
        return GetScriptPath(Path.Combine(subdirectory1, subdirectory2, scriptFileName));
    }

    /// <summary>
    /// Validates that a script exists at the specified path.
    /// </summary>
    /// <param name="relativePath">Relative path from Scripts directory</param>
    /// <returns>True if script exists, false otherwise</returns>
    public bool ScriptExists(string relativePath)
    {
        try
        {
            var fullPath = GetScriptPath(relativePath);
            var exists = File.Exists(fullPath);
            
            if (!exists)
            {
                _logger.LogWarning("Script not found: {RelativePath} (resolved to: {FullPath})", relativePath, fullPath);
            }
            
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking script existence: {RelativePath}", relativePath);
            return false;
        }
    }

    /// <summary>
    /// Gets diagnostic information about script path resolution.
    /// Useful for troubleshooting path resolution issues.
    /// </summary>
    /// <returns>Diagnostic information about the current path resolution</returns>
    public ScriptPathDiagnostics GetDiagnostics()
    {
        var scriptsDirectory = GetScriptsBaseDirectory();
        
        return new ScriptPathDiagnostics
        {
            ScriptsBaseDirectory = scriptsDirectory,
            ScriptsDirectoryExists = Directory.Exists(scriptsDirectory),
            AppDomainBaseDirectory = AppDomain.CurrentDomain.BaseDirectory,
            CurrentWorkingDirectory = Environment.CurrentDirectory,
            AssemblyLocation = Assembly.GetExecutingAssembly().Location,
            AssemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty
        };
    }

    /// <summary>
    /// Gets the base Scripts directory using fallback logic for different deployment scenarios.
    /// 
    /// This method handles the core path resolution problem by trying multiple strategies:
    /// 1. Look for Scripts in AppDomain.CurrentDomain.BaseDirectory (deployed app)
    /// 2. Look for Scripts relative to assembly location (some deployment scenarios)
    /// 3. Look for Scripts in current working directory (development)
    /// </summary>
    /// <returns>Absolute path to the Scripts directory</returns>
    private string GetScriptsBaseDirectory()
    {
        // Use cached value if available for performance
        if (!string.IsNullOrEmpty(_cachedScriptsBaseDirectory) && Directory.Exists(_cachedScriptsBaseDirectory))
        {
            return _cachedScriptsBaseDirectory;
        }

        // Strategy 1: Try AppDomain.CurrentDomain.BaseDirectory (deployed applications)
        var appDomainScripts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        if (Directory.Exists(appDomainScripts))
        {
            _logger.LogDebug("Using AppDomain base directory for scripts: {Path}", appDomainScripts);
            _cachedScriptsBaseDirectory = appDomainScripts;
            return appDomainScripts;
        }

        // Strategy 2: Try assembly location directory (alternative deployment scenario)
        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            var assemblyScripts = Path.Combine(assemblyDirectory, "Scripts");
            if (Directory.Exists(assemblyScripts))
            {
                _logger.LogDebug("Using assembly location for scripts: {Path}", assemblyScripts);
                _cachedScriptsBaseDirectory = assemblyScripts;
                return assemblyScripts;
            }
        }

        // Strategy 3: Try current working directory (development scenario)
        var workingDirScripts = Path.Combine(Environment.CurrentDirectory, "Scripts");
        if (Directory.Exists(workingDirScripts))
        {
            _logger.LogDebug("Using working directory for scripts: {Path}", workingDirScripts);
            _cachedScriptsBaseDirectory = workingDirScripts;
            return workingDirScripts;
        }

        // Strategy 4: Try going up directories to find Scripts folder (development with nested build output)
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++) // Search up to 5 levels up
        {
            var parentScripts = Path.Combine(currentDir, "Scripts");
            if (Directory.Exists(parentScripts))
            {
                _logger.LogDebug("Found Scripts directory by searching up {Levels} levels: {Path}", i + 1, parentScripts);
                _cachedScriptsBaseDirectory = parentScripts;
                return parentScripts;
            }
            
            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (string.IsNullOrEmpty(parentDir) || parentDir == currentDir)
                break;
            
            currentDir = parentDir;
        }

        // Final fallback - return a path even if it doesn't exist (will cause clear error messages)
        var fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        _logger.LogWarning("Could not find Scripts directory. Using fallback: {Path}", fallbackPath);
        
        // Log diagnostic information to help troubleshoot
        _logger.LogWarning("Script resolution diagnostics: AppDomain.BaseDirectory={AppDomain}, WorkingDirectory={WorkingDir}, AssemblyLocation={Assembly}",
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory, 
            Assembly.GetExecutingAssembly().Location);

        _cachedScriptsBaseDirectory = fallbackPath;
        return fallbackPath;
    }

    /// <summary>
    /// Clears the cached Scripts directory path. 
    /// Useful for testing or if the Scripts directory location changes at runtime.
    /// </summary>
    public void ClearCache()
    {
        _cachedScriptsBaseDirectory = null;
        _logger.LogDebug("Script path cache cleared");
    }
}

/// <summary>
/// Diagnostic information about script path resolution.
/// Useful for troubleshooting path resolution issues in logs.
/// </summary>
public class ScriptPathDiagnostics
{
    public string ScriptsBaseDirectory { get; set; } = string.Empty;
    public bool ScriptsDirectoryExists { get; set; }
    public string AppDomainBaseDirectory { get; set; } = string.Empty;
    public string CurrentWorkingDirectory { get; set; } = string.Empty;
    public string AssemblyLocation { get; set; } = string.Empty;
    public string AssemblyDirectory { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"ScriptsBase: {ScriptsBaseDirectory} (Exists: {ScriptsDirectoryExists}), " +
               $"AppDomain: {AppDomainBaseDirectory}, " +
               $"WorkingDir: {CurrentWorkingDirectory}, " +
               $"Assembly: {AssemblyDirectory}";
    }
}