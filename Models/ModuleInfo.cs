using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace VCenterMigrationTool.Models;

/// <summary>
/// Represents information about a PowerShell module required by the application
/// </summary>
public partial class ModuleInfo : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty] 
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isRequired = true;

    [ObservableProperty]
    private string _installedVersion = "Not Installed";

    [ObservableProperty]
    private string _minimumVersion = string.Empty;

    [ObservableProperty]
    private string _status = "Checking...";

    [ObservableProperty]
    private string _installCommand = string.Empty;

    /// <summary>
    /// Collection of module sub-components (for VMware.PowerCLI which is actually a meta-module)
    /// </summary>
    public List<string> SubModules { get; set; } = new();

    /// <summary>
    /// Creates module info for VMware PowerCLI
    /// </summary>
    public static ModuleInfo CreatePowerCLI()
    {
        return new ModuleInfo
        {
            Name = "VMware.PowerCLI",
            Description = "VMware vSphere PowerCLI for vCenter automation",
            IsRequired = true,
            MinimumVersion = "13.0.0",
            InstallCommand = "Install-Module VMware.PowerCLI -Force -Scope CurrentUser",
            SubModules = new List<string>
            {
                "VMware.VimAutomation.Core",
                "VMware.VimAutomation.Common", 
                "VMware.VimAutomation.Sdk",
                "VMware.Vim"
            }
        };
    }

    /// <summary>
    /// Creates module info for PowerShell execution policy management
    /// </summary>
    public static ModuleInfo CreateExecutionPolicy()
    {
        return new ModuleInfo
        {
            Name = "ExecutionPolicy",
            Description = "PowerShell script execution permissions",
            IsRequired = true,
            MinimumVersion = "N/A",
            InstallCommand = "Set-ExecutionPolicy RemoteSigned -Scope CurrentUser"
        };
    }

    /// <summary>
    /// Creates module info for .NET Framework compatibility
    /// </summary>
    public static ModuleInfo CreateDotNetFramework()
    {
        return new ModuleInfo
        {
            Name = ".NET Framework",
            Description = "Required for PowerShell module compatibility",
            IsRequired = true,
            MinimumVersion = "4.7.2"
        };
    }
}