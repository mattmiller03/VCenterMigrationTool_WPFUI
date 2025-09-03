using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Service to resolve the optimal PowerShell executable path
/// Prioritizes bundled PowerShell global tool over system installations
/// </summary>
public class PowerShellPathService
{
    private readonly ILogger<PowerShellPathService> _logger;
    private static string? _cachedPowerShellPath;

    public PowerShellPathService(ILogger<PowerShellPathService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the best available PowerShell executable path
    /// Priority: 1. Bundled dotnet tool, 2. System pwsh, 3. System powershell
    /// </summary>
    public string GetPowerShellExecutablePath()
    {
        if (_cachedPowerShellPath != null)
        {
            return _cachedPowerShellPath;
        }

        _logger.LogInformation("Resolving optimal PowerShell executable path...");

        // Priority 1: Try bundled dotnet tool PowerShell
        var bundledPath = GetBundledPowerShellPath();
        if (!string.IsNullOrEmpty(bundledPath))
        {
            _logger.LogInformation("✅ Found bundled PowerShell: {Path}", bundledPath);
            _cachedPowerShellPath = bundledPath;
            return bundledPath;
        }

        // Priority 2: Try system pwsh (PowerShell 7+)
        var systemPwshPath = GetSystemPowerShellPath("pwsh");
        if (!string.IsNullOrEmpty(systemPwshPath))
        {
            _logger.LogInformation("✅ Found system PowerShell 7+: {Path}", systemPwshPath);
            _cachedPowerShellPath = systemPwshPath;
            return systemPwshPath;
        }

        // Priority 3: Fallback to system powershell (Windows PowerShell 5.1)
        var systemPowerShellPath = GetSystemPowerShellPath("powershell");
        if (!string.IsNullOrEmpty(systemPowerShellPath))
        {
            _logger.LogWarning("⚠️ Using legacy Windows PowerShell 5.1: {Path}", systemPowerShellPath);
            _cachedPowerShellPath = systemPowerShellPath;
            return systemPowerShellPath;
        }

        // No PowerShell found
        _logger.LogError("❌ No PowerShell executable found on system");
        throw new InvalidOperationException("No PowerShell executable found. Please ensure PowerShell is installed.");
    }

    /// <summary>
    /// Gets information about the resolved PowerShell installation
    /// </summary>
    public async Task<PowerShellInfo> GetPowerShellInfoAsync()
    {
        var executablePath = GetPowerShellExecutablePath();
        
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "-NoProfile -NonInteractive -Command \"$PSVersionTable | ConvertTo-Json\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start PowerShell process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new PowerShellInfo
            {
                ExecutablePath = executablePath,
                IsBundled = IsBundledPowerShell(executablePath),
                RawVersionInfo = output,
                IsAvailable = process.ExitCode == 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PowerShell version information from {Path}", executablePath);
            return new PowerShellInfo
            {
                ExecutablePath = executablePath,
                IsBundled = IsBundledPowerShell(executablePath),
                RawVersionInfo = $"Error: {ex.Message}",
                IsAvailable = false
            };
        }
    }

    /// <summary>
    /// Gets the path to bundled PowerShell dotnet tool
    /// </summary>
    private string? GetBundledPowerShellPath()
    {
        try
        {
            // Check if we're in a project with dotnet tools
            var currentDir = Directory.GetCurrentDirectory();
            var appDir = AppContext.BaseDirectory;
            
            // Try current working directory first
            var bundledPath = TryGetDotnetToolPath(currentDir);
            if (!string.IsNullOrEmpty(bundledPath))
            {
                return bundledPath;
            }

            // Try application base directory
            bundledPath = TryGetDotnetToolPath(appDir);
            if (!string.IsNullOrEmpty(bundledPath))
            {
                return bundledPath;
            }

            _logger.LogDebug("No bundled PowerShell found in current or app directory");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for bundled PowerShell");
            return null;
        }
    }

    /// <summary>
    /// Tries to get dotnet tool path from a specific directory
    /// </summary>
    private string? TryGetDotnetToolPath(string directory)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool run pwsh --version",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            process.WaitForExit(5000); // 5 second timeout
            
            if (process.ExitCode == 0)
            {
                return $"dotnet tool run pwsh"; // Special marker for dotnet tool
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to check dotnet tool in {Directory}", directory);
        }

        return null;
    }

    /// <summary>
    /// Gets system PowerShell path by executable name
    /// </summary>
    private string? GetSystemPowerShellPath(string executableName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = executableName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                // Return first path if multiple found
                var firstPath = output.Split('\n')[0].Trim();
                if (File.Exists(firstPath))
                {
                    return firstPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to find system PowerShell: {Executable}", executableName);
        }

        return null;
    }

    /// <summary>
    /// Determines if the PowerShell path is the bundled version
    /// </summary>
    private bool IsBundledPowerShell(string path)
    {
        return path.Contains("dotnet tool run");
    }
}

/// <summary>
/// Information about the resolved PowerShell installation
/// </summary>
public class PowerShellInfo
{
    public string ExecutablePath { get; set; } = string.Empty;
    public bool IsBundled { get; set; }
    public string RawVersionInfo { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}