# vCenter Migration Tool v1.1

## 1. Introduction

This document outlines the structure and development guide for the vCenter Migration Tool, a WPF application designed to assist with migrating a VMware vCenter environment. The application provides a modern, intuitive graphical user interface (GUI) for executing complex PowerShell and PowerCLI operations. The project is now at version 1.1, with a solid architectural foundation and comprehensive ESXi host management capabilities implemented.

## 2. Current Status & Recent Updates (December 2024)

### 2.1 Major New Feature: ESXi Host Management ✅

**Complete Host Migration System Integration:**
- **VMHostConfigV2.ps1 Integration**: Successfully integrated the proven VMHostConfigV2.ps1 script into the GUI application
- **Three-Mode Operation**: Full support for Backup, Restore, and Migrate operations for ESXi hosts
- **Enhanced Host Migration Page**: Completely redesigned with modern UI supporting all VMHostConfigV2 features
- **Wrapper Script Architecture**: Created Invoke-VMHostConfig.ps1 wrapper for seamless C#/PowerShell integration
- **Advanced Configuration Options**: Support for uplink portgroups, operation timeouts, and migration-specific settings

**ESXi Host Management Features:**
- **Host Configuration Backup**: Create comprehensive JSON backups of ESXi host configurations including network, storage, services, and advanced settings
- **Host Configuration Restore**: Restore host configurations from backup files with rollback capability on failure
- **Host Migration Between vCenters**: Full migration of ESXi hosts between source and target vCenter servers with configuration preservation
- **Real-time Progress Tracking**: Live progress updates during multi-host operations with detailed logging
- **Secure Credential Handling**: ESXi root password prompting for direct host access during migrations

### 2.2 Previously Resolved Issues ✅

**PowerShell Integration & Security Improvements:**
- **Fixed PowerCLI Bypass Parameter Issue**: Resolved issue where `BypassModuleCheck` parameter wasn't being passed correctly to PowerShell scripts, causing unnecessary PowerCLI module imports and slow connection times
- **Implemented Persistent PowerCLI Detection**: PowerCLI installation status now persists across application restarts using automatic detection on startup
- **Enhanced Password Security**: Removed all plain text password logging while maintaining full functionality - passwords now show as `[REDACTED]` in logs
- **Dynamic Log Path Configuration**: Application logs now save to the user-configured log directory from settings instead of hardcoded paths
- **Dual Connection Path Fix**: Both Dashboard connection testing and Settings profile testing now correctly pass the BypassModuleCheck parameter

### 2.3 Current Working Features ✅

- **Modern Fluent UI**: Built using WPF-UI library with Light/Dark theme support
- **MVVM Architecture**: Clean separation of concerns with dependency injection
- **Persistent Connection Profiles**: Encrypted password storage with Windows credentials
- **Dashboard**: Source/Target vCenter connection testing with real-time status
- **ESXi Host Management**: Complete backup, restore, and migration functionality for ESXi hosts
- **Settings System**: 
  - PowerShell/PowerCLI prerequisites checking and installation
  - File path configuration for logs and exports
  - Connection profile management with testing capabilities
- **Security**: All password handling is secure with no plain text logging
- **Logging**: Comprehensive logging with Serilog to user-configured locations

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

- **/Models**: Contains C# classes representing data (e.g., VCenterConnection.cs, MigrationTask.cs, HostBackupInfo.cs)
- **/Views**:
  - **/Pages**: Individual pages (DashboardPage.xaml, HostMigrationPage.xaml, SettingsPage.xaml, etc.)
  - **/Windows**: Main application shell (MainWindow.xaml)
- **/ViewModels**: Logic and data for each View (e.g., DashboardViewModel.cs, HostMigrationViewModel.cs, SettingsViewModel.cs)
- **/Services**: Backend services:
  - **HybridPowerShellService.cs**: Executes PowerShell scripts with security and optimization
  - **ConnectionProfileService.cs**: Manages connection profiles with encryption
  - **ConfigurationService.cs**: Handles application configuration and settings
  - **ApplicationHostService.cs**: Manages application startup and navigation
- **/Helpers**: Value converters and utility classes
- **/Scripts**: PowerShell .ps1 files including VMHostConfigV2.ps1 and Invoke-VMHostConfig.ps1 (all set to "Copy if newer")

## 5. Key Development Patterns

### 5.1 PowerShell Integration Best Practices

**Security Requirements:**
- All password parameters must be marked as sensitive in logging
- Use `IsSensitiveParameter()` method to detect password-related parameters
- Parameters containing "password", "pwd", "secret", "token", or "key" are automatically redacted

**PowerCLI Optimization:**
- PowerCLI installation status is automatically detected on application startup
- Scripts requiring PowerCLI automatically receive `BypassModuleCheck=true` parameter when PowerCLI is confirmed installed
- Use `SavePowerCliStatus()` method when PowerCLI installation status changes

**Script Parameter Handling:**
```csharp
// Correct way to add parameters for PowerCLI scripts
var scriptParams = new Dictionary<string, object>
{
    { "VCenterServer", serverAddress },
    { "Username", username },
    { "Password", password }
};

// BypassModuleCheck is automatically added if PowerCLI is confirmed
if (HybridPowerShellService.PowerCliConfirmedInstalled)
{
    scriptParams["BypassModuleCheck"] = true;
}

await _powerShellService.RunScriptAsync("Script.ps1", scriptParams, logPath);
```

### 5.2 ESXi Host Management Integration Pattern

**VMHostConfigV2.ps1 Integration:**
The application leverages the proven VMHostConfigV2.ps1 script through a wrapper architecture:

```csharp
// Example of calling the wrapper script for host migration
var scriptParams = new Dictionary<string, object>
{
    { "Action", "Migrate" },
    { "VMHostName", "esx01.lab.local" },
    { "SourceVCenter", "source-vc.lab.local" },
    { "TargetVCenter", "target-vc.lab.local" },
    { "SourceCredential", "user1:password1" },
    { "TargetCredential", "user2:password2" },
    { "ESXiHostCredential", "root:hostpassword" },
    { "TargetClusterName", "Target-Cluster" },
    { "BackupPath", @"C:\Backups\HostConfigs" },
    { "OperationTimeout", 600 }
};

await _powerShellService.RunScriptOptimizedAsync(
    ".\\Scripts\\Invoke-VMHostConfig.ps1", 
    scriptParams);
```

**Credential Handling Pattern:**
- C# application collects credentials through secure dialogs
- Credentials are passed as colon-separated strings to PowerShell wrapper
- Wrapper script converts strings to PSCredential objects internally
- No plain text credentials are logged at any stage

### 5.3 Creating a New Page

1. **Create the Model(s)**: In /Models folder, create C# classes for data representation
2. **Create the PowerShell Script(s)**: In /Scripts folder, create .ps1 files that output JSON using `ConvertTo-Json`
3. **Create the ViewModel**: In /ViewModels folder, inherit from `ObservableObject`, inject required services
4. **Create the View**: In /Views/Pages folder, create XAML page
5. **Create Code-Behind**: Constructor accepts ViewModel via DI, sets DataContext
6. **Register in App.xaml.cs**: Register Page and ViewModel as singletons in ConfigureServices
7. **Add to Navigation**: Add NavigationViewItem to MainWindowViewModel

### 5.4 Data Binding Pattern

Established pattern for all pages:
- Code-behind constructor receives ViewModel via DI
- Page's DataContext is set to the page itself (`DataContext = this;`)
- ViewModel exposed via public property: `public MyViewModel ViewModel { get; }`
- All XAML bindings use ViewModel prefix: `Text="{Binding ViewModel.MyProperty}"`

## 6. Current Issues & Next Steps

### 6.1 Known Working Areas ✅
- Dashboard connection testing (both source and target)
- Settings page PowerShell prerequisites checking and PowerCLI installation
- Connection profile management with secure password storage
- Application configuration and logging
- UI navigation and theming
- **ESXi Host Management**: Complete backup, restore, and migration functionality
- **VMHostConfigV2.ps1 Integration**: Full PowerShell script integration with wrapper architecture

### 6.2 Areas Needing Development 🚧

**High Priority:**
1. **VM Migration Page**: Complete the DataGrid functionality with real PowerShell integration
2. **Network Migration Page**: Implement the network topology discovery and migration logic
3. **vCenter Objects Migration**: Implement roles, permissions, folders, and tags migration

**Medium Priority:**
1. **Enhanced Host Management Features**: 
   - Bulk backup operations for multiple hosts simultaneously
   - Backup file management (listing, validation, cleanup)
   - Migration validation and pre-checks
2. **Progress Reporting**: Enhanced real-time progress for long-running migration operations
3. **Error Handling**: Enhanced error recovery and user feedback for failed operations
4. **Export/Import Configuration**: Complete the vCenter configuration backup and restore functionality

**Low Priority:**
1. **Host Management Enhancements**:
   - Backup file browser with metadata display
   - Migration scheduling and queuing
   - Host configuration comparison tools
2. **UI Improvements**:
   - Drag-and-drop backup file selection
   - Visual migration progress indicators
   - Host topology visualization

### 6.3 Technical Debt to Address
- Consider adding persistent configuration storage for PowerCLI status
- Implement retry logic for failed PowerShell operations
- Add unit tests for critical services, especially the new host management features
- Consider adding PowerShell execution timeouts for specific operations
- Optimize the VMHostConfigV2.ps1 wrapper for better error handling and logging

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

### 7.3 Logging Best Practices
- Use structured logging with proper log levels (Debug, Info, Warning, Error)
- Include context information in log messages
- Never log sensitive data (passwords, tokens, etc.)
- Use the configured log path from ConfigurationService

## 8. Recent Code Changes Summary

**Files Modified in Latest Session:**
- `HostMigrationViewModel.cs`: Complete rewrite with VMHostConfigV2.ps1 integration
- `HostMigrationPage.xaml`: Enhanced UI with action selection, file browsers, and advanced options
- `Scripts/Invoke-VMHostConfig.ps1`: New PowerShell wrapper script for VMHostConfigV2.ps1 integration
- `Models/HostBackupInfo.cs`: New model for backup file metadata
- `App.xaml.cs`: Added IDialogService registration for host management features

**Key Improvements in v1.1:**
- Complete ESXi host management system with backup, restore, and migration capabilities
- VMHostConfigV2.ps1 script integration through wrapper architecture
- Enhanced UI with dynamic configuration panels and file browser integration
- Real-time progress tracking for multi-host operations
- Advanced configuration options (timeouts, uplink portgroups, target selection)
- Secure credential handling for ESXi host direct access
- Comprehensive error handling and logging for host operations

**Previous Improvements (v1.0):**
- PowerCLI bypass optimization now persists across application restarts
- All password logging removed for security compliance
- Enhanced debugging capabilities for troubleshooting PowerShell issues
- Automatic PowerCLI detection on application startup

## 9. ESXi Host Management Features (New in v1.1)

### 9.1 Host Configuration Backup
- **Comprehensive Backup**: Captures network configuration, storage settings, services, firewall rules, advanced settings, and more
- **JSON Format**: Structured backup files in JSON format for easy parsing and validation
- **Metadata**: Includes backup date, source vCenter, and host information
- **File Management**: Configurable backup directory with auto-generated filenames

### 9.2 Host Configuration Restore
- **File Browser Integration**: GUI file selection for backup files
- **Rollback Capability**: Automatic rollback on restore failure
- **Validation**: Pre-restore validation of backup file integrity
- **Progress Tracking**: Real-time progress updates during restore operations

### 9.3 Host Migration Between vCenters
- **Full Migration**: Complete host migration including configuration preservation
- **Multi-Credential Support**: Separate credentials for source vCenter, target vCenter, and ESXi host
- **Target Configuration**: Datacenter and cluster selection in target vCenter
- **Migration Options**: Preserve VM assignments, migrate host profiles, update DRS rules
- **Lockdown Mode Handling**: Automatic lockdown mode management during migration
- **Network Configuration**: Advanced uplink portgroup handling and VDS management

### 9.4 Advanced Features
- **Operation Timeout**: Configurable timeout for long-running operations
- **Batch Operations**: Support for multiple host processing
- **Detailed Logging**: Comprehensive operation logs with timestamps
- **Error Recovery**: Robust error handling with detailed error messages
- **WhatIf Support**: Preview operations before execution (inherits from VMHostConfigV2.ps1)

The application is now a comprehensive ESXi host management solution with production-ready backup, restore, and migration capabilities, ready for the next phase of development focusing on completing VM migration and network topology features.