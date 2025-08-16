# vCenter Migration Tool v1.2

## 1. Introduction

This document outlines the structure and development guide for the vCenter Migration Tool, a WPF application designed to assist with migrating a VMware vCenter environment. The application provides a modern, intuitive graphical user interface (GUI) for executing complex PowerShell and PowerCLI operations. The project is now at version 1.2, featuring comprehensive ESXi host management, VM configuration backup capabilities, and network migration functionality.

## 2. Current Status & Recent Updates (January 2025)

### 2.1 Major New Features in v1.2 ✅

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

### 2.2 Previously Working Features ✅

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
  - **HybridPowerShellService.cs**: PowerShell execution with security and optimization
  - **SharedConnectionService.cs**: Manages vCenter connections
  - **CredentialService.cs**: Secure credential management
  - **ConfigurationService.cs**: Application configuration
- **/Helpers**: Utility classes including WindowScrollBehavior.cs for scroll fix
- **/Scripts**: PowerShell files including VM backup scripts and network migration scripts

## 5. Key Development Patterns

### 5.1 VM Backup Integration Pattern

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

### 5.2 Network Migration Integration Pattern

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

### 5.3 UI Scroll Behavior Fix

**Global Scroll Solution:**
```csharp
// WindowScrollBehavior.cs helper applied to NavigationView
<ui:NavigationView helpers:WindowScrollBehavior.EnableGlobalScroll="True">
```

This resolves mouse wheel scrolling issues across all pages by handling scroll events at the navigation container level.

### 5.4 PowerShell Script Organization

**Script Categories:**
- **VM Operations**: BackupVMConfigurations.ps1, RestoreVMConfigurations.ps1, ValidateVMBackups.ps1
- **Network Operations**: Migrate-NetworkConfiguration.ps1, Get-NetworkTopology.ps1
- **Host Operations**: VMHostConfigV2.ps1, Invoke-VMHostConfig.ps1
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
- **Settings**: PowerShell/PowerCLI prerequisites and configuration management
- **UI Navigation**: Smooth scrolling and modern interface across all pages

### 6.2 Areas Needing Development 🚧

**High Priority:**
1. **Resource Pool Migration**: Complete the ResourcePoolMigrationPage with actual PowerShell integration
2. **Folder Structure Migration**: Implement VM folder structure migration capabilities
3. **Migration Validation**: Enhanced pre-migration validation across all migration types
4. **Bulk Operations**: Multi-selection and batch processing improvements

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

## 8. Recent Code Changes Summary (v1.2)

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

## 9. VM Configuration Backup Features (New in v1.2)

### 9.1 Backup Capabilities
- **Flexible Scopes**: Selected VMs, cluster-based, or full vCenter backup
- **Comprehensive Data**: VM settings, network adapters, disks, snapshots, annotations, custom attributes
- **PowerCLI Integration**: Advanced VM configuration extraction using PowerCLI cmdlets
- **Compression**: Optional ZIP compression for backup files
- **Metadata**: Rich backup metadata including timestamp, source vCenter, and backup options

### 9.2 Backup Management
- **File Validation**: Verify backup file integrity and content
- **Restore Preview**: Display backup contents without making changes
- **Backup Browser**: Validate and explore existing backup files
- **Configuration Options**: Granular control over what data to include in backups

## 10. Network Migration Features (New in v1.2)

### 10.1 Network Discovery
- **Topology Visualization**: Hierarchical display of hosts, vSwitches, and port groups
- **Component Selection**: Checkbox-based selection of individual network components
- **Standard and Distributed vSwitches**: Support for both switch types
- **VMkernel Ports**: Discovery and migration planning for management networks

### 10.2 Migration Capabilities
- **Network Mapping**: Manual and automatic mapping between source and target networks
- **VLAN Preservation**: Maintain or modify VLAN configurations during migration
- **Conflict Resolution**: Handle existing network configurations on target
- **Validation Mode**: Test migration configurations before execution
- **Export/Import**: Save network configurations for documentation and backup

### 10.3 Advanced Features
- **Auto-Mapping**: Intelligent network mapping based on name matching
- **Selective Migration**: Choose specific network components to migrate
- **Configuration Export**: JSON/CSV export for documentation and backup
- **Real-time Progress**: Live migration progress with detailed logging

## 11. Next Development Session Priorities

**Immediate Next Steps:**
1. **Resource Pool Migration**: Complete the ResourcePoolMigrationPage functionality
2. **PowerShell Script Creation**: Create missing scripts for resource pool operations
3. **Migration Validation**: Implement pre-migration validation across all types
4. **Error Handling Enhancement**: Improve error recovery and user feedback

**Recommended Development Order:**
1. Complete Resource Pool Migration page
2. Implement Folder Structure Migration
3. Add comprehensive migration validation
4. Create migration templates and saved configurations
5. Enhance reporting and analytics

The application now provides a comprehensive migration suite with professional-grade VM backup, network migration, and host management capabilities. The next phase should focus on completing the remaining migration types and enhancing the overall user experience with advanced features and validation.