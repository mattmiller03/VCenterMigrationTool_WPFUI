# vCenter Migration Tool v1.0

## 1. Introduction

This document outlines the structure and development guide for the vCenter Migration Tool, a WPF application designed to assist with migrating a VMware vCenter environment. The application provides a modern, intuitive graphical user interface (GUI) for executing complex PowerShell and PowerCLI operations. The project is currently at version 1.0, with a solid architectural foundation and several core features implemented.

## 2. Current Status & Recent Updates (August 2025)

### 2.1 Recently Resolved Issues ✅

**PowerShell Integration & Security Improvements:**
- **Fixed PowerCLI Bypass Parameter Issue**: Resolved issue where `BypassModuleCheck` parameter wasn't being passed correctly to PowerShell scripts, causing unnecessary PowerCLI module imports and slow connection times
- **Implemented Persistent PowerCLI Detection**: PowerCLI installation status now persists across application restarts using automatic detection on startup
- **Enhanced Password Security**: Removed all plain text password logging while maintaining full functionality - passwords now show as `[REDACTED]` in logs
- **Dynamic Log Path Configuration**: Application logs now save to the user-configured log directory from settings instead of hardcoded paths
- **Dual Connection Path Fix**: Both Dashboard connection testing and Settings profile testing now correctly pass the BypassModuleCheck parameter

### 2.2 Current Working Features ✅

- **Modern Fluent UI**: Built using WPF-UI library with Light/Dark theme support
- **MVVM Architecture**: Clean separation of concerns with dependency injection
- **Persistent Connection Profiles**: Encrypted password storage with Windows credentials
- **Dashboard**: Source/Target vCenter connection testing with real-time status
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

- **/Models**: Contains C# classes representing data (e.g., VCenterConnection.cs, MigrationTask.cs)
- **/Views**:
  - **/Pages**: Individual pages (DashboardPage.xaml, SettingsPage.xaml, etc.)
  - **/Windows**: Main application shell (MainWindow.xaml)
- **/ViewModels**: Logic and data for each View (e.g., DashboardViewModel.cs, SettingsViewModel.cs)
- **/Services**: Backend services:
  - **HybridPowerShellService.cs**: Executes PowerShell scripts with security and optimization
  - **ConnectionProfileService.cs**: Manages connection profiles with encryption
  - **ConfigurationService.cs**: Handles application configuration and settings
  - **ApplicationHostService.cs**: Manages application startup and navigation
- **/Helpers**: Value converters and utility classes
- **/Scripts**: PowerShell .ps1 files (all set to "Copy if newer")

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

### 5.2 Creating a New Page

1. **Create the Model(s)**: In /Models folder, create C# classes for data representation
2. **Create the PowerShell Script(s)**: In /Scripts folder, create .ps1 files that output JSON using `ConvertTo-Json`
3. **Create the ViewModel**: In /ViewModels folder, inherit from `ObservableObject`, inject required services
4. **Create the View**: In /Views/Pages folder, create XAML page
5. **Create Code-Behind**: Constructor accepts ViewModel via DI, sets DataContext
6. **Register in App.xaml.cs**: Register Page and ViewModel as singletons in ConfigureServices
7. **Add to Navigation**: Add NavigationViewItem to MainWindowViewModel

### 5.3 Data Binding Pattern

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

### 6.2 Areas Needing Development 🚧

**High Priority:**
1. **VM Migration Page**: Complete the DataGrid functionality with real PowerShell integration
2. **Network Migration Page**: Implement the network topology discovery and migration logic
3. **Host Migration Page**: Build out the TreeView for cluster/host selection and migration
4. **vCenter Objects Migration**: Implement roles, permissions, folders, and tags migration

**Medium Priority:**
1. **Shared Connection Service Enhancement**: Ensure proper connection state sharing between pages
2. **Progress Reporting**: Implement real-time progress for long-running migration operations
3. **Error Handling**: Enhanced error recovery and user feedback for failed operations
4. **Export/Import Configuration**: Complete the vCenter configuration backup and restore functionality

### 6.3 Technical Debt to Address
- Consider adding persistent configuration storage for PowerCLI status
- Implement retry logic for failed PowerShell operations
- Add unit tests for critical services
- Consider adding PowerShell execution timeouts for specific operations

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
- `HybridPowerShellService.cs`: Added persistent PowerCLI detection, secure parameter logging
- `DashboardViewModel.cs`: Enhanced debugging, secure parameter handling
- `ViewProfilesViewModel.cs`: Added BypassModuleCheck support for settings page testing
- `PowerShellSettingsViewModel.cs`: Added SavePowerCliStatus calls
- `App.xaml.cs`: Dynamic log path configuration

**Key Improvements:**
- PowerCLI bypass optimization now persists across application restarts
- All password logging removed for security compliance
- Enhanced debugging capabilities for troubleshooting PowerShell issues
- Automatic PowerCLI detection on application startup

The application is now stable, secure, and ready for the next phase of development focusing on completing the migration functionality for VMs, networks, and hosts.