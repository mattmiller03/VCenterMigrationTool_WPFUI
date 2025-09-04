# vCenter Migration Tool v1.4

## 1. Introduction

This document outlines the structure and development guide for the vCenter Migration Tool, a WPF application designed to assist with migrating a VMware vCenter environment. The application provides a modern, intuitive graphical user interface (GUI) for executing complex PowerShell and PowerCLI operations. The project is now at version 1.4, featuring comprehensive ESXi host management, VM configuration backup capabilities, network migration functionality, enhanced PowerShell logging with dashboard integration, and robust dual connection architecture for maximum vCenter compatibility.

## 2. Current Status & Recent Updates (January 2025)

### 2.1 Latest Updates in v1.4 ✅

**Unified Connection and Script Execution Architecture (January 2025):**
- **Process Isolation Fix**: Resolved critical architecture issue where vCenter connections established in main UI weren't accessible to scripts running in different PowerShell processes
- **PersistantVcenterConnectionService Enhancement**: Added `ExecuteScriptAsync`, `ExecuteDualVCenterScriptAsync`, and `ExecuteCommandAsync` methods to run scripts in same process as persistent connections
- **HybridPowerShellService Elimination**: Removed HybridPowerShellService dependency from migration ViewModels to prevent process separation conflicts
- **Unified Script Execution**: All PowerShell operations now execute within persistent connection service ensuring connection accessibility
- **Direct Module Checking**: Updated PowerShell settings to use direct PowerShell commands instead of script-based prerequisite checking
- **ModuleInfo Collection**: Transformed single PowerCLI status to collection of required modules (PowerCLI, ExecutionPolicy, .NET Framework) with individual tracking
- **Credential Management Simplification**: Eliminated redundant credential passing between services by using established connections

### 2.2 Previous Updates in v1.4 ✅

**Dual Connection Architecture Implementation:**
- **Simultaneous API + PowerCLI Connections**: Dashboard now establishes both API and PowerCLI connections simultaneously for maximum compatibility
- **Enhanced Connection Status**: Connection details now show status of both API and PowerCLI connections with clear capability indicators
- **Admin Configuration Fix**: Resolved admin configuration loading issues by ensuring PowerCLI availability regardless of API SSL problems
- **Improved Error Handling**: Better connection failure messages with dual connection status reporting
- **SharedConnectionService Enhancement**: Updated to track both API (`SourceApiConnected`, `TargetApiConnected`) and PowerCLI connection states independently

**SSO Admin Module Issue Resolution:**
- **VMware.vSphere.SsoAdmin Module Handling**: Gracefully handles missing SSO Admin module (deprecated in PowerCLI 13.x)
- **Fallback SSO Discovery**: Automatic fallback to standard vCenter role/permission discovery using `Get-VIRole` and `Get-VIPermission`
- **User-Friendly Error Messages**: Clear status messages explaining SSO module limitations with informative guidance
- **Enhanced Admin Config Loading**: Improved error handling in `AdminConfigMigrationViewModel` with context-aware error detection

**Settings Page DataTrigger Binding Fix:**
- **Accent Color Selection**: Fixed DataTrigger binding error in `AppearanceSettingsView.xaml`
- **Custom Converter Implementation**: Created `AccentColorSelectionConverter` with `MultiBinding` support for proper accent color highlighting
- **XAML Binding Compliance**: Resolved invalid binding expressions in DataTrigger values using proper WPF patterns

**Enhanced Folder Migration System:**
- **Complete Export/Import Functionality**: Fully functional folder export to JSON and import with actual folder creation in target vCenter
- **Copy-VMFolderStructure.ps1 Integration**: Updated MigrateFoldersAsync to use the proven Copy-VMFolderStructure.ps1 script for reliable folder replication
- **Smart Folder Import**: Import functionality now creates actual VM folders with hierarchical structure preservation, duplicate detection, and parent folder auto-creation
- **Enhanced Admin Config Migration**: Folder migration checkbox now performs complete folder structure replication between datacenters
- **Real-time Progress Reporting**: Detailed logging with creation statistics (Created, Existed, Failed) and comprehensive error handling
- **PowerShell Integration**: Uses embedded PowerShell scripts for reliable vCenter folder operations with SecureString password handling

### 2.2 Major Updates from v1.3 ✅

**Enhanced PowerShell Logging System:**
- **Comprehensive Script Logging**: All PowerShell scripts now use the unified Write-ScriptLog.ps1 logging system
- **Individual Log Files**: Each script execution creates its own log file in PowerShell subdirectory  
- **Dashboard Activity Integration**: ESXi host backup operations now appear in the dashboard activity logs
- **PowerShellLoggingService**: Real-time logging service with event-driven architecture for live monitoring
- **Activity Logs Page**: Complete activity monitoring with filtering, export, and time-range selection
- **Session Tracking**: Unique session IDs for each script execution with start/end tracking

**Application Cleanup Improvements:**
- **Enhanced Process Cleanup**: Improved PowerShell process termination on application shutdown
- **ViewModel Disposal**: Proper cleanup of timers and resources in ViewModels (PowerShellSettingsViewModel, ActivityLogsViewModel)
- **Shutdown Logging**: Detailed logging of cleanup operations during application exit
- **Process Count Tracking**: Log exactly how many PowerShell processes are cleaned up on shutdown

**ESXi Host Backup Fixes:**
- **JSON Parsing Improvements**: Enhanced JSON extraction from PowerShell script output
- **LogPath Parameter**: Fixed missing LogPath parameter in Backup-ESXiHostConfig.ps1
- **Error Handling**: Better error handling for multi-host backup operations
- **Dashboard Job Tracking**: Backup jobs now show properly in the dashboard with progress updates

### 2.3 Major Features from v1.2 ✅

**VM Configuration Backup System:**
- **Comprehensive VM Backup**: Complete VM configuration backup functionality integrated into VM Migration page
- **Flexible Backup Scopes**: Support for selected VMs, cluster-based backup, or full vCenter backup
- **Rich Configuration Data**: Captures VM settings, network adapters, disk configuration, snapshots, annotations, custom attributes, and permissions
- **Export Options**: JSON format with optional compression for space efficiency
- **Backup Validation**: Built-in backup file validation and restore preview functionality
- **PowerShell Script Integration**: External PowerShell scripts (BackupVMConfigurations.ps1, RestoreVMConfigurations.ps1, ValidateVMBackups.ps1)

**Network Migration System:**
- **Complete Network Migration Page**: Full network topology discovery and migration capabilities
- **Network Topology Visualization**: Hierarchical TreeView display of hosts → vSwitches → port groups with selection checkboxes
- **Migration Support**: Standard vSwitches, Distributed vSwitches, Port Groups, and VMkernel ports
- **Network Mapping**: Manual and automatic network mapping between source and target environments
- **Configuration Export/Import**: Save network configurations as JSON/CSV for documentation and backup
- **Advanced Options**: VLAN preservation, conflict resolution, validation mode, and selective component migration
- **PowerShell Integration**: Migrate-NetworkConfiguration.ps1 and Get-NetworkTopology.ps1 scripts

**Enhanced VM Migration Page:**
- **Complete UI Redesign**: Professional multi-section layout with VM selection, backup, migration, and logging
- **VM Selection Management**: Select All/Unselect All functionality with DataGrid checkbox integration
- **Migration Configuration**: Comprehensive migration options including disk format, MAC preservation, network handling
- **Network Mapping**: Dynamic network mapping with add/remove functionality
- **Post-Migration Cleanup**: Integrated cleanup operations using VMPostMigrationCleanup.ps1
- **Real-time Logging**: Comprehensive activity logging with timestamps

**Global UI Improvements:**
- **Scroll Behavior Fix**: Resolved mouse wheel scrolling issues across the application using WindowScrollBehavior helper
- **Enhanced Navigation**: Improved page navigation with proper scroll handling
- **Professional UI**: Consistent modern design across all pages with progress indicators and status feedback

### 2.4 Previously Working Features ✅

**ESXi Host Management (v1.1):**
- **VMHostConfigV2.ps1 Integration**: Complete host backup, restore, and migration functionality
- **Three-Mode Operation**: Full support for Backup, Restore, and Migrate operations
- **Advanced Configuration Options**: Uplink portgroups, operation timeouts, migration-specific settings
- **Real-time Progress Tracking**: Live progress updates with detailed logging

**Core Application Features:**
- **Modern Fluent UI**: WPF-UI library with Light/Dark theme support
- **MVVM Architecture**: Clean separation with dependency injection
- **Persistent Connection Profiles**: Encrypted password storage
- **Dashboard**: Source/Target vCenter connection testing
- **Settings System**: PowerShell/PowerCLI prerequisites and configuration management
- **Security**: Secure password handling with no plain text logging

## 3. Technology Stack

- **Framework**: .NET 8
- **UI**: Windows Presentation Foundation (WPF) 
- **UI Library**: WPF-UI (Fluent) v4.0.3
- **Architecture**: Model-View-ViewModel (MVVM)
- **MVVM Toolkit**: CommunityToolkit.Mvvm for source-generated observable properties and commands
- **Dependency Injection**: Microsoft.Extensions.Hosting
- **Logging**: Serilog with dynamic log paths
- **Backend Logic**: PowerShell 7+ with external process execution (HybridPowerShellService)

## 4. Project Structure

The project follows a standard MVVM structure:

- **/Models**: Data classes (VCenterConnection.cs, VirtualMachine.cs, NetworkHostNode.cs, ResourcePoolInfo.cs, etc.)
- **/Views**:
  - **/Pages**: Individual pages (DashboardPage.xaml, HostMigrationPage.xaml, VmMigrationPage.xaml, NetworkMigrationPage.xaml, SettingsPage.xaml)
  - **/Windows**: Main application shell (MainWindow.xaml)
- **/ViewModels**: Page logic (DashboardViewModel.cs, VmMigrationViewModel.cs, NetworkMigrationViewModel.cs, etc.)
- **/Services**: Backend services:
  - **HybridPowerShellService.cs**: PowerShell execution with security, optimization, and process cleanup
  - **PersistentExternalConnectionService.cs**: Manages persistent PowerShell connections to vCenters
  - **PowerShellLoggingService.cs**: Centralized logging for all PowerShell script executions
  - **SharedConnectionService.cs**: Manages vCenter connections
  - **CredentialService.cs**: Secure credential management
  - **ConfigurationService.cs**: Application configuration
- **/Helpers**: Utility classes including WindowScrollBehavior.cs for scroll fix
- **/Scripts**: PowerShell files including VM backup scripts and network migration scripts

## 5. Key Development Patterns

### 5.1 Dual Connection Architecture Pattern

**Simultaneous API + PowerCLI Connection Setup:**
```csharp
// DashboardViewModel.cs - Always attempt both connections
var (apiSuccess, sessionToken) = await _vSphereApiService.AuthenticateAsync(connectionInfo, finalPassword);
bool powerCLISuccess = false;
string powerCLIResult = await _powerShellService.RunCommandAsync(powerCLIScript);

if (powerCLIResult.Contains("POWERCLI_SUCCESS")) {
    powerCLISuccess = true;
}

// Overall success if either connection works
bool overallSuccess = apiSuccess || powerCLISuccess;

// Track both connection states independently
_sharedConnectionService.SourceApiConnected = apiSuccess;
_sharedConnectionService.SourceUsingPowerCLI = powerCLISuccess;
```

**Connection Status Detection:**
```csharp
// SharedConnectionService.cs - Check both connection types
if (hasPowerCLI && !string.IsNullOrEmpty(sessionId)) {
    return (true, serverAddress, "PowerCLI Session");
}
if (hasAPI) {
    return (true, serverAddress, "API Session");
}
```

**Admin Configuration Fallback Pattern:**
```csharp
// VCenterInventoryService.cs - Handle missing SSO module gracefully
if (result.StartsWith("ERROR:")) {
    _logger.LogInformation("Note: VMware.vSphere.SsoAdmin module may be missing - this is normal for PowerCLI 13.x");
    _logger.LogInformation("Falling back to basic role/permission discovery");
    await LoadBasicRolesAndPermissionsAsync(vCenterName, inventory, connectionType);
}
```

### 5.2 VM Backup Integration Pattern

**External PowerShell Scripts Approach:**
The application uses external PowerShell scripts for maintainability:

```csharp
// VM Backup execution example
var parameters = new Dictionary<string, object>
{
    ["BackupFilePath"] = backupFilePath,
    ["VMNames"] = selectedVMs.Select(vm => vm.Name).ToArray(),
    ["IncludeSettings"] = true,
    ["IncludeSnapshots"] = false,
    ["CompressOutput"] = true
};

var scriptPath = Path.Combine("Scripts", "BackupVMConfigurations.ps1");
var result = await _powerShellService.RunScriptAsync(scriptPath, parameters);
```

**VM Selection Pattern:**
- VirtualMachine model includes IsSelected property for checkbox binding
- ViewModel provides SelectAll/UnselectAll commands
- Backup scope determined by user selection (selected VMs, cluster, or all VMs)

### 5.2 XAML DataTrigger Binding Pattern

**MultiBinding with Custom Converter:**
```xml
<!-- AppearanceSettingsView.xaml - Fixed DataTrigger binding -->
<DataTrigger Value="True">
    <DataTrigger.Binding>
        <MultiBinding Converter="{StaticResource AccentColorSelectionConverter}">
            <Binding Path="Name"/>
            <Binding Path="DataContext.CurrentAccentColor" RelativeSource="{RelativeSource AncestorType=UserControl}"/>
        </MultiBinding>
    </DataTrigger.Binding>
    <Setter Property="BorderBrush" Value="{Binding HexValue}"/>
    <Setter Property="BorderThickness" Value="2"/>
</DataTrigger>
```

**Custom IMultiValueConverter:**
```csharp
// AccentColorSelectionConverter.cs
public class AccentColorSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && 
            values[0] is string colorName && 
            values[1] is string currentAccentColor)
        {
            return string.Equals(colorName, currentAccentColor, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
```

### 5.3 Network Migration Integration Pattern

**Network Topology Discovery:**
```csharp
// Network topology loading
var networkScript = Path.Combine("Scripts", "Get-NetworkTopology.ps1");
var networkTopology = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<NetworkHostNode>(networkScript, parameters);

// Hierarchical data structure: NetworkHostNode → VSwitchInfo → PortGroupInfo
```

**Network Mapping System:**
- Manual network mapping with source→target relationships
- Auto-mapping based on matching network names
- NetworkMappingItem model for source/target network pairs

### 5.4 UI Scroll Behavior Fix

**Global Scroll Solution:**
```csharp
// WindowScrollBehavior.cs helper applied to NavigationView
<ui:NavigationView helpers:WindowScrollBehavior.EnableGlobalScroll="True">
```

This resolves mouse wheel scrolling issues across all pages by handling scroll events at the navigation container level.

### 5.5 PowerShell Script Organization

**Script Categories:**
- **VM Operations**: BackupVMConfigurations.ps1, RestoreVMConfigurations.ps1, ValidateVMBackups.ps1
- **Network Operations**: Migrate-NetworkConfiguration.ps1, Get-NetworkTopology.ps1
- **Host Operations**: VMHostConfigV2.ps1, Invoke-VMHostConfig.ps1
- **Folder Operations**: Copy-VMFolderStructure.ps1 (v2.0 with enhanced datacenter support)
- **Migration Operations**: CrossVcenterVMmigration_list.ps1, VMPostMigrationCleanup.ps1

**PowerCLI Script Detection:**
Scripts are automatically detected for PowerCLI optimization:
```csharp
private bool IsPowerCliScript(string scriptPath)
{
    var powerCliScripts = new[]
    {
        // VM backup scripts
        "BackupVMConfigurations.ps1",
        "RestoreVMConfigurations.ps1",
        "ValidateVMBackups.ps1",
        
        // Network migration scripts
        "Migrate-NetworkConfiguration.ps1",
        "Get-NetworkTopology.ps1",
        
        // Folder migration scripts
        "Copy-VMFolderStructure.ps1",
        
        // Existing scripts...
    };
    // ...
}
```

## 6. Current Issues & Next Steps

### 6.1 Completed Features ✅

- **Dashboard**: Connection testing for source and target vCenters
- **ESXi Host Management**: Complete backup, restore, and migration functionality
- **VM Migration**: Complete UI with backup functionality, migration configuration, and post-migration cleanup
- **Network Migration**: Full network topology discovery and migration system
- **Folder Migration**: Complete VM folder structure export/import and replication functionality
- **Settings**: PowerShell/PowerCLI prerequisites and configuration management
- **UI Navigation**: Smooth scrolling and modern interface across all pages

### 6.2 Areas Needing Development 🚧

**High Priority:**
1. **Resource Pool Migration**: Complete the ResourcePoolMigrationPage with actual PowerShell integration
2. **Migration Validation**: Enhanced pre-migration validation across all migration types
3. **Bulk Operations**: Multi-selection and batch processing improvements

**Medium Priority:**
1. **Migration Scheduling**: Queue and schedule migration operations
2. **Enhanced Reporting**: Detailed migration reports with success/failure statistics
3. **Configuration Templates**: Save and reuse migration configuration templates
4. **Advanced Network Features**: VDS migration enhancements and network policy migration

**Low Priority:**
1. **Migration Analytics**: Success rate tracking and performance metrics
2. **Integration APIs**: REST API for automation integration
3. **Advanced UI Features**: Drag-and-drop operations and visual topology mapping
4. **Multi-vCenter Support**: Manage multiple source/target vCenter pairs

### 6.3 Technical Debt to Address
- **Unit Testing**: Add comprehensive test coverage for ViewModels and Services
- **PowerShell Error Handling**: Enhanced error recovery and user feedback
- **Performance Optimization**: Optimize large-scale data loading and processing
- **Configuration Persistence**: Save user preferences and migration templates
- **Documentation**: Inline code documentation and user guide creation

## 7. Development Guidelines

### 7.1 Security Requirements
- **NEVER log passwords in plain text**
- Always use `IsSensitiveParameter()` checks before logging
- Use SecureString where possible, convert only when necessary for external processes
- Validate all user inputs before passing to PowerShell scripts

### 7.2 PowerShell Script Guidelines
- All scripts should accept a `-LogPath` parameter for consistent logging
- Scripts requiring PowerCLI should accept a `-BypassModuleCheck` switch parameter
- Always output structured data using `ConvertTo-Json` for C# consumption
- Include proper error handling and logging within scripts
- **Use external script files** instead of embedded PowerShell for maintainability

### 7.3 UI Development Best Practices
- **Apply WindowScrollBehavior.EnableGlobalScroll="True"** to NavigationView for proper scrolling
- Use consistent card-based layout with ui:CardControl
- Implement progress indicators for long-running operations
- Provide real-time status updates and activity logging
- Use hierarchical data display (TreeView) for complex data structures

## 8. Recent Code Changes Summary (v1.3)

**Latest Changes in v1.3:**
- `EsxiHostsViewModel.cs`: Added PowerShellLoggingService integration for dashboard job tracking
- `PowerShellLoggingService.cs`: Enhanced with event-driven architecture for real-time monitoring
- `ActivityLogsViewModel.cs`: Complete activity log management with filtering and export
- `App.xaml.cs`: Enhanced shutdown cleanup with ViewModel disposal and process count logging
- `PowerShellSettingsViewModel.cs`: Added IDisposable implementation for timer cleanup
- `Write-ScriptLog.ps1`: Updated all PowerShell scripts to use unified logging system
- `Backup-ESXiHostConfig.ps1`: Fixed missing LogPath parameter and improved JSON output

## 9. Previous Code Changes (v1.2)

**Major Files Added/Modified:**
- `VmMigrationViewModel.cs`: Complete rewrite with backup functionality and enhanced migration features
- `VmMigrationPage.xaml`: Professional multi-section UI with VM backup integration
- `NetworkMigrationViewModel.cs`: New comprehensive network migration ViewModel
- `NetworkMigrationPage.xaml`: New network topology visualization and migration page
- `WindowScrollBehavior.cs`: New helper for global scroll behavior fix
- `Scripts/BackupVMConfigurations.ps1`: Comprehensive VM backup script
- `Scripts/RestoreVMConfigurations.ps1`: VM backup validation and restore script
- `Scripts/ValidateVMBackups.ps1`: Backup file validation script
- `Scripts/Migrate-NetworkConfiguration.ps1`: Network migration engine
- `Scripts/Get-NetworkTopology.ps1`: Network topology discovery script

**Key Improvements in v1.2:**
- **VM Backup System**: Complete VM configuration backup with flexible scopes and validation
- **Network Migration**: Full network topology migration with mapping and validation
- **Enhanced VM Migration**: Professional UI with integrated backup and cleanup operations
- **Global Scroll Fix**: Resolved mouse wheel scrolling issues application-wide
- **Script Architecture**: Moved to external PowerShell scripts for better maintainability
- **Professional UI**: Consistent modern design with progress tracking and status feedback

## 10. VM Configuration Backup Features (New in v1.2)

### 10.1 Backup Capabilities
- **Flexible Scopes**: Selected VMs, cluster-based, or full vCenter backup
- **Comprehensive Data**: VM settings, network adapters, disks, snapshots, annotations, custom attributes
- **PowerCLI Integration**: Advanced VM configuration extraction using PowerCLI cmdlets
- **Compression**: Optional ZIP compression for backup files
- **Metadata**: Rich backup metadata including timestamp, source vCenter, and backup options

### 10.2 Backup Management
- **File Validation**: Verify backup file integrity and content
- **Restore Preview**: Display backup contents without making changes
- **Backup Browser**: Validate and explore existing backup files
- **Configuration Options**: Granular control over what data to include in backups

## 11. Network Migration Features (New in v1.2)

### 11.1 Network Discovery
- **Topology Visualization**: Hierarchical display of hosts, vSwitches, and port groups
- **Component Selection**: Checkbox-based selection of individual network components
- **Standard and Distributed vSwitches**: Support for both switch types
- **VMkernel Ports**: Discovery and migration planning for management networks

### 11.2 Migration Capabilities
- **Network Mapping**: Manual and automatic mapping between source and target networks
- **VLAN Preservation**: Maintain or modify VLAN configurations during migration
- **Conflict Resolution**: Handle existing network configurations on target
- **Validation Mode**: Test migration configurations before execution
- **Export/Import**: Save network configurations for documentation and backup

### 11.3 Advanced Features
- **Auto-Mapping**: Intelligent network mapping based on name matching
- **Selective Migration**: Choose specific network components to migrate
- **Configuration Export**: JSON/CSV export for documentation and backup
- **Real-time Progress**: Live migration progress with detailed logging

## 12. PowerShell Script Logging System (v1.3)

### 12.1 Write-ScriptLog.ps1 Integration
All PowerShell scripts now use the centralized logging function:
- **Individual Log Files**: Each script execution creates a unique log file
- **Session Tracking**: Unique session IDs for tracking script executions
- **Log Location**: Logs stored in `LogPath/PowerShell/[scriptname]_[sessionid]_[timestamp].log`
- **Console Control**: `-SuppressConsoleOutput` parameter for cleaner execution

### 12.2 Scripts Updated with Logging
**Fully Integrated (16 scripts):**
- Get-Clusters.ps1, Get-Datacenters.ps1, Get-Datastores.ps1
- Connect-vCenterPersistent.ps1, Export-vCenterConfig.ps1
- Backup-ESXiHostConfig.ps1, VMHostConfigV2.ps1
- BackupVMConfigurations.ps1, RestoreVMConfigurations.ps1
- CrossVcenterVMmigration_list.ps1, VMPostMigrationCleanup.ps1
- Get-NetworkTopology.ps1, Migrate-NetworkConfiguration.ps1
- And more...

### 12.3 Process Cleanup Architecture
**Robust Process Management:**
- **HybridPowerShellService**: Tracks all spawned processes with 5-minute cleanup timer
- **PersistentExternalConnectionService**: Manages persistent connections with graceful shutdown
- **App.OnExit**: Explicit cleanup calls with process count logging
- **ViewModel Disposal**: Proper cleanup of timers and event subscriptions

## 13. Next Development Session Priorities

**Immediate Next Steps:**
1. **Resource Pool Migration**: Complete the ResourcePoolMigrationPage functionality
2. **PowerShell Script Creation**: Create missing scripts for resource pool operations
3. **Migration Validation**: Implement pre-migration validation across all types
4. **Error Handling Enhancement**: Improve error recovery and user feedback

**Recommended Development Order:**
1. Complete Resource Pool Migration page
2. Add comprehensive migration validation
3. Create migration templates and saved configurations
4. Enhance reporting and analytics

The application now provides a comprehensive migration suite with professional-grade VM backup, network migration, folder structure migration, host management capabilities, and robust PowerShell logging with dashboard integration. The next phase should focus on completing the remaining migration types (Resource Pools) and enhancing the overall user experience with advanced features and validation.

## 14. Recent Code Changes Summary (v1.4)

**Latest Changes in v1.4:**
- `PersistantVcenterConnectionService.cs`: Added script execution methods (`ExecuteScriptAsync`, `ExecuteDualVCenterScriptAsync`, `ExecuteCommandAsync`) to unify connection and execution in single process
- `AdminConfigMigrationViewModel.cs`: Removed HybridPowerShellService dependency, updated all script calls to use persistent connection service
- `PowerShellSettingsViewModel.cs`: Implemented direct module checking with `ModuleInfo` collection instead of script-based prerequisite validation
- `ModuleInfo.cs`: New observable model for tracking individual PowerShell module requirements (PowerCLI, ExecutionPolicy, .NET Framework)
- `DashboardViewModel.cs`: Complete dual connection rewrite with simultaneous API + PowerCLI connection establishment
- `SharedConnectionService.cs`: Enhanced with `SourceApiConnected`/`TargetApiConnected` properties and improved `GetConnectionStatusAsync()` logic
- `VCenterInventoryService.cs`: Enhanced SSO Admin module error handling with informative fallback messages
- `AppearanceSettingsView.xaml`: Fixed DataTrigger binding error using MultiBinding approach
- `AccentColorSelectionConverter.cs`: New custom converter for proper accent color selection in settings

**Key Technical Improvements:**
- **Unified Architecture**: Single service handles both vCenter connections and script execution, eliminating process isolation issues
- **Simplified Credential Management**: Connections established once through UI are used by all scripts without re-authentication
- **Enhanced Module Tracking**: Individual status tracking for PowerCLI, ExecutionPolicy, and .NET Framework requirements
- **Direct PowerShell Integration**: Module checking uses direct commands instead of external scripts for better reliability
- **Process Consolidation**: All PowerShell operations run in persistent connection processes ensuring connection accessibility
- **Dual Connection Architecture**: Always attempts both API and PowerCLI connections for maximum compatibility
- **Complete Folder Migration**: Functional export/import system with actual folder creation using PowerShell integration
- **Enhanced Error Communication**: Clear, user-friendly messages explaining SSO module limitations
- **Proper XAML Binding**: Resolved DataTrigger binding issues using WPF-compliant patterns

## 15. Known Issues & Solutions

### 15.1 ESXi Host Backup JSON Parsing (v1.3)
**Issue**: "T is invalid after a single JSON value" error during multi-host backups
**Solution**: Enhanced JSON extraction with multiple parsing methods and better error handling in EsxiHostsViewModel.cs

### 15.2 PowerShell Process Cleanup (v1.3)
**Issue**: Potential hanging PowerShell processes on application exit
**Solution**: Comprehensive cleanup in App.OnExit with process count logging and ViewModel disposal

### 15.3 Dashboard Activity Tracking (v1.3)
**Issue**: Backup jobs not appearing in dashboard activity logs
**Solution**: Integrated PowerShellLoggingService with EsxiHostsViewModel for real-time job tracking

### 15.4 SSO Admin Module Missing (v1.4) ✅ RESOLVED
**Issue**: Admin configuration loading failed due to missing `VMware.vSphere.SsoAdmin` module (deprecated in PowerCLI 13.x)
**Solution**: Implemented graceful fallback to standard `Get-VIRole` and `Get-VIPermission` with user-friendly error messages

### 15.5 Settings Page DataTrigger Binding (v1.4) ✅ RESOLVED
**Issue**: DataTrigger binding error in accent color selection due to invalid binding expressions in `Value` attribute
**Solution**: Implemented `AccentColorSelectionConverter` with `MultiBinding` for proper WPF-compliant DataTrigger functionality

### 15.6 Admin Configuration Connection Issues (v1.4) ✅ RESOLVED
**Issue**: Admin configuration failed to load when API connections worked but PowerCLI was unavailable
**Solution**: Implemented dual connection architecture ensuring PowerCLI availability for admin operations regardless of API SSL issues

### 15.7 Inappropriate Disconnect Commands (v1.4) ✅ RESOLVED
**Issue**: Disconnect-VIServer errors appearing in logs during PowerCLI cleanup when modules weren't loaded
**Solution**: Added conditional checks to only attempt VIServer disconnection when PowerCLI is loaded and has active connections

### 15.8 Process Isolation Architecture (v1.4) ✅ RESOLVED
**Issue**: vCenter connections established through main UI Connect buttons weren't accessible to scripts running in different PowerShell processes via HybridPowerShellService
**Solution**: Unified all PowerShell operations in PersistantVcenterConnectionService, ensuring connections and script execution occur in the same process. Eliminated HybridPowerShellService dependency from migration ViewModels.