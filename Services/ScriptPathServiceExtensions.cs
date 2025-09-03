using System;
using System.IO;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Extension methods to help migrate existing hardcoded script paths to use ScriptPathService.
/// These methods provide PowerShell-style path shortcuts for common patterns.
/// 
/// Migration Guide:
/// OLD: Path.Combine("Scripts", "Active", "Test.ps1") 
/// NEW: scriptPathService.GetActiveScript("Test.ps1")
/// 
/// OLD: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Active", "Get-Prerequisites.ps1")
/// NEW: scriptPathService.GetActiveScript("Get-Prerequisites.ps1")
/// </summary>
public static class ScriptPathServiceExtensions
{
    /// <summary>
    /// Gets path to script in Scripts/Active directory.
    /// Equivalent to PowerShell: Join-Path $PSScriptRoot "Active" $ScriptName
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptName">Script filename (e.g., "Test-VCenterConnection.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public static string GetActiveScript(this ScriptPathService service, string scriptName)
    {
        return service.GetActiveScriptPath(scriptName);
    }

    /// <summary>
    /// Gets path to script in Scripts/Active/Infrastructure Discovery directory.
    /// Common pattern for infrastructure discovery scripts.
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptName">Script filename (e.g., "Get-Clusters.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public static string GetInfrastructureDiscoveryScript(this ScriptPathService service, string scriptName)
    {
        return service.GetScriptPath("Active", "Infrastructure Discovery", scriptName);
    }

    /// <summary>
    /// Gets path to script in Scripts/Active/Core Migration directory.
    /// Common pattern for core migration scripts.
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptName">Script filename (e.g., "Migrate-VCenterObject.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public static string GetCoreMigrationScript(this ScriptPathService service, string scriptName)
    {
        return service.GetScriptPath("Active", "Core Migration", scriptName);
    }

    /// <summary>
    /// Gets path to script in Scripts/Active/VM Migration-Backup directory.
    /// Common pattern for VM migration and backup scripts.
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptName">Script filename (e.g., "BackupVMConfigurations.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public static string GetVMMigrationBackupScript(this ScriptPathService service, string scriptName)
    {
        return service.GetScriptPath("Active", "VM Migration-Backup", scriptName);
    }

    /// <summary>
    /// Gets path to script in Scripts/Active/Network Management directory.
    /// Common pattern for network management scripts.
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptName">Script filename (e.g., "Get-VDSSwitches.ps1")</param>
    /// <returns>Absolute path to the script</returns>
    public static string GetNetworkManagementScript(this ScriptPathService service, string scriptName)
    {
        return service.GetScriptPath("Active", "Network Management", scriptName);
    }

    /// <summary>
    /// Validates that a script exists and provides detailed error information if not.
    /// Equivalent to PowerShell: Test-Path $ScriptPath
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="scriptPath">Path returned by any Get*Script method</param>
    /// <param name="errorDetails">Detailed error information if script doesn't exist</param>
    /// <returns>True if script exists, false otherwise</returns>
    public static bool ValidateScriptExists(this ScriptPathService service, string scriptPath, out string errorDetails)
    {
        errorDetails = string.Empty;
        
        try
        {
            if (File.Exists(scriptPath))
            {
                return true;
            }
            
            var diagnostics = service.GetDiagnostics();
            errorDetails = $"Script not found at: {scriptPath}\n" +
                          $"Diagnostics: {diagnostics}";
            
            return false;
        }
        catch (Exception ex)
        {
            errorDetails = $"Error validating script path: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Provides a migration-friendly method that accepts old-style relative paths
    /// and converts them to use the proper ScriptPathService resolution.
    /// 
    /// This helps during the migration process by accepting both old patterns:
    /// - "Scripts\\Active\\Test.ps1" 
    /// - ".\\Scripts\\Active\\Test.ps1"
    /// - "Scripts/Active/Test.ps1"
    /// 
    /// And converting them to use proper base directory resolution.
    /// </summary>
    /// <param name="service">ScriptPathService instance</param>
    /// <param name="oldRelativePath">Old-style relative path</param>
    /// <returns>Properly resolved absolute path</returns>
    public static string MigrateOldRelativePath(this ScriptPathService service, string oldRelativePath)
    {
        if (string.IsNullOrWhiteSpace(oldRelativePath))
            throw new ArgumentException("Path cannot be null or empty", nameof(oldRelativePath));
        
        // Normalize the path separators
        var normalizedPath = oldRelativePath.Replace('\\', Path.DirectorySeparatorChar)
                                           .Replace('/', Path.DirectorySeparatorChar);
        
        // Remove leading ./ or .\ if present
        if (normalizedPath.StartsWith("." + Path.DirectorySeparatorChar))
        {
            normalizedPath = normalizedPath.Substring(2);
        }
        
        // Remove leading Scripts/ if present (since our service assumes Scripts as base)
        if (normalizedPath.StartsWith("Scripts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Substring(("Scripts" + Path.DirectorySeparatorChar).Length);
        }
        
        // Use the service's main GetScriptPath method
        return service.GetScriptPath(normalizedPath);
    }
}