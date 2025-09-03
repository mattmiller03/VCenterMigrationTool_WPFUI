using Microsoft.Extensions.Logging;
using System;
using System.Text;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Utility class to help diagnose script path resolution issues.
/// This class provides diagnostic methods to help administrators troubleshoot
/// script path problems during deployment and development.
/// </summary>
public class ScriptPathDiagnosticUtility
{
    private readonly ILogger<ScriptPathDiagnosticUtility> _logger;
    private readonly ScriptPathService _scriptPathService;

    public ScriptPathDiagnosticUtility(
        ILogger<ScriptPathDiagnosticUtility> logger,
        ScriptPathService scriptPathService)
    {
        _logger = logger;
        _scriptPathService = scriptPathService;
    }

    /// <summary>
    /// Runs comprehensive script path diagnostics and logs detailed information.
    /// This method helps troubleshoot the "script not found" issues by checking
    /// all possible script locations and resolution strategies.
    /// </summary>
    /// <returns>Diagnostic report as a formatted string</returns>
    public string RunDiagnostics()
    {
        var report = new StringBuilder();
        
        try
        {
            report.AppendLine("=== VCENTER MIGRATION TOOL - SCRIPT PATH DIAGNOSTICS ===");
            report.AppendLine($"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // Get diagnostic information from ScriptPathService
            var diagnostics = _scriptPathService.GetDiagnostics();
            
            report.AppendLine("🔍 PATH RESOLUTION ANALYSIS:");
            report.AppendLine($"   Scripts Base Directory: {diagnostics.ScriptsBaseDirectory}");
            report.AppendLine($"   Directory Exists: {diagnostics.ScriptsDirectoryExists}");
            report.AppendLine($"   AppDomain Base Directory: {diagnostics.AppDomainBaseDirectory}");
            report.AppendLine($"   Current Working Directory: {diagnostics.CurrentWorkingDirectory}");
            report.AppendLine($"   Assembly Location: {diagnostics.AssemblyLocation}");
            report.AppendLine($"   Assembly Directory: {diagnostics.AssemblyDirectory}");
            report.AppendLine();

            // Test common script paths
            report.AppendLine("🧪 SCRIPT PATH TESTING:");
            
            var testScripts = new[]
            {
                ("Active/Test-VCenterConnection.ps1", "Test vCenter Connection"),
                ("Active/Get-Prerequisites.ps1", "Prerequisites Check"),
                ("Active/Write-ScriptLog.ps1", "Script Logging"),
                ("Active/Core Migration/Migrate-VCenterObject.ps1", "Core Migration"),
                ("Active/Infrastructure Discovery/Get-Clusters.ps1", "Infrastructure Discovery")
            };

            foreach (var (relativePath, description) in testScripts)
            {
                try
                {
                    var resolvedPath = _scriptPathService.GetScriptPath(relativePath);
                    var exists = _scriptPathService.ScriptExists(relativePath);
                    var status = exists ? "✅ EXISTS" : "❌ MISSING";
                    
                    report.AppendLine($"   {description}:");
                    report.AppendLine($"      Relative Path: {relativePath}");
                    report.AppendLine($"      Resolved Path: {resolvedPath}");
                    report.AppendLine($"      Status: {status}");
                    report.AppendLine();
                }
                catch (Exception ex)
                {
                    report.AppendLine($"   {description}: ERROR - {ex.Message}");
                    report.AppendLine();
                }
            }

            // Test the new extension methods
            report.AppendLine("🚀 EXTENSION METHODS TESTING:");
            
            try
            {
                var activeScript = _scriptPathService.GetActiveScript("Test-VCenterConnection.ps1");
                var activeExists = _scriptPathService.ValidateScriptExists("Active/Test-VCenterConnection.ps1", out var errorDetails);
                
                report.AppendLine($"   Extension Method Test:");
                report.AppendLine($"      GetActiveScript('Test-VCenterConnection.ps1'): {activeScript}");
                report.AppendLine($"      Script Exists: {activeExists}");
                if (!string.IsNullOrEmpty(errorDetails))
                {
                    report.AppendLine($"      Error Details: {errorDetails}");
                }
                report.AppendLine();
            }
            catch (Exception ex)
            {
                report.AppendLine($"   Extension Methods: ERROR - {ex.Message}");
                report.AppendLine();
            }

            // Test migration helper
            report.AppendLine("🔄 MIGRATION HELPER TESTING:");
            
            var oldPaths = new[]
            {
                "Scripts\\Active\\Test-VCenterConnection.ps1",
                ".\\Scripts\\Active\\Get-Prerequisites.ps1",
                "Scripts/Active/Write-ScriptLog.ps1"
            };

            foreach (var oldPath in oldPaths)
            {
                try
                {
                    var migratedPath = _scriptPathService.MigrateOldRelativePath(oldPath);
                    var exists = System.IO.File.Exists(migratedPath);
                    var status = exists ? "✅ RESOLVED" : "❌ NOT FOUND";
                    
                    report.AppendLine($"   Old Path Migration:");
                    report.AppendLine($"      Original: {oldPath}");
                    report.AppendLine($"      Migrated: {migratedPath}");
                    report.AppendLine($"      Status: {status}");
                    report.AppendLine();
                }
                catch (Exception ex)
                {
                    report.AppendLine($"   Migration Test for '{oldPath}': ERROR - {ex.Message}");
                    report.AppendLine();
                }
            }

            report.AppendLine("🎯 RECOMMENDATIONS:");
            if (!diagnostics.ScriptsDirectoryExists)
            {
                report.AppendLine("   ⚠️  CRITICAL: Scripts directory not found!");
                report.AppendLine("   📋 Solution: Ensure build configuration copies Scripts/** to output directory");
                report.AppendLine("   📋 Check: VCenterMigrationTool.csproj should include Scripts/**/* with CopyToOutputDirectory");
            }
            else
            {
                report.AppendLine("   ✅ Scripts directory found and accessible");
                report.AppendLine("   📋 All ViewModels should use ScriptPathService instead of hardcoded paths");
                report.AppendLine("   📋 Use extension methods for cleaner, more maintainable code");
            }

            var reportText = report.ToString();
            _logger.LogInformation("Script path diagnostics completed");
            return reportText;
        }
        catch (Exception ex)
        {
            var errorReport = $"DIAGNOSTIC ERROR: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
            _logger.LogError(ex, "Failed to run script path diagnostics");
            return errorReport;
        }
    }

    /// <summary>
    /// Quick validation method to check if the core script path resolution is working.
    /// This can be called at application startup to verify the fix is working.
    /// </summary>
    /// <returns>True if script path resolution is working, false otherwise</returns>
    public bool ValidateScriptPathResolution()
    {
        try
        {
            // Test resolution of a known script
            var testScript = _scriptPathService.GetActiveScriptPath("Test-VCenterConnection.ps1");
            var diagnostics = _scriptPathService.GetDiagnostics();
            
            // Check if base directory exists (even if specific script doesn't exist)
            if (diagnostics.ScriptsDirectoryExists)
            {
                _logger.LogInformation("✅ Script path resolution validation passed");
                return true;
            }
            else
            {
                _logger.LogError("❌ Script path resolution validation failed - Scripts directory not found");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Script path resolution validation failed with exception");
            return false;
        }
    }
}