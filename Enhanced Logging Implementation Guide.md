# Enhanced Logging Implementation Guide for vCenter Migration Tool Scripts

## Overview

This guide provides a comprehensive approach to implement consistent, enhanced logging across all PowerShell scripts in the vCenter Migration Tool. The enhanced logging framework provides:

- **Structured logging** with categories and data
- **Performance measurement** and timing
- **Log rotation** and file management
- **Multiple output targets** (console, file)
- **Session tracking** and correlation
- **Error handling** with stack traces
- **Statistics collection**

## 1. Framework Components

### Core Files
- `Write-ScriptLog.ps1` - Enhanced logging framework (replace existing)
- Updated script templates with integrated logging

### Key Features
- **Session IDs** for correlation across operations
- **Performance timing** with `Measure-ScriptBlock`
- **Category-based logging** for better organization
- **Automatic log rotation** when files get large
- **Configurable log levels** (Debug, Verbose, Info, Warning, Error, Critical)
- **Statistics tracking** for operational metrics

## 2. Implementation Steps for Each Script

### Step 1: Add Enhanced Parameters
Add these parameters to your script's param block:

```powershell
# Enhanced logging parameters
[ValidateSet('Debug', 'Verbose', 'Info', 'Warning', 'Error', 'Critical')]
[string]$LogLevel = 'Info',

[switch]$DisableConsoleOutput,
[switch]$IncludeStackTrace,
[string]$CustomLogPath
```

### Step 2: Initialize Logging
Replace existing logging initialization:

```powershell
# Import the enhanced logging framework
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Initialize logging
$loggingParams = @{
    ScriptName = 'YourScriptName'
    LogLevel = $LogLevel
    IncludeStackTrace = $IncludeStackTrace
}

if ($CustomLogPath) { $loggingParams.LogFile = $CustomLogPath }
if ($DisableConsoleOutput) { $loggingParams.DisableConsole = $true }

Start-ScriptLogging @loggingParams
```

### Step 3: Replace Existing Log Functions
Replace your existing `Write-Log` functions with:

```powershell
# Use appropriate helper functions
Write-LogInfo "Information message" -Category "CategoryName"
Write-LogWarning "Warning message" -Category "CategoryName"
Write-LogError "Error message" -Category "CategoryName" -ErrorRecord $_
Write-LogSuccess "Success message" -Category "CategoryName"
Write-LogDebug "Debug message" -Category "CategoryName"
```

### Step 4: Add Performance Measurement
Wrap time-critical operations:

```powershell
$result = Measure-ScriptBlock -Name "Operation Name" -Category "Performance" -ScriptBlock {
    # Your operation here
    Connect-VIServer -Server $vCenter -Credential $cred
}
```

### Step 5: Add Statistics Tracking
Initialize and track operational metrics:

```powershell
$script:Statistics = @{
    'Total Operations' = 0
    'Successful Operations' = 0
    'Failed Operations' = 0
    'Processing Time' = 0
}

# Update throughout script
$script:Statistics['Total Operations']++
```

### Step 6: Enhanced Error Handling
Use detailed error logging:

```powershell
try {
    # Your operation
}
catch {
    Write-LogError "Operation failed" -Category "OperationType" -ErrorRecord $_ -Data @{
        VM = $vmName
        Host = $hostName
        Operation = "Migration"
    }
    throw
}
```

### Step 7: Finalize Logging
Replace script cleanup with:

```powershell
finally {
    # Your cleanup code
    
    # Finalize logging
    $success = $script:Statistics['Successful Operations'] -eq $script:Statistics['Total Operations']
    $summary = "Processed $($script:Statistics['Successful Operations'])/$($script:Statistics['Total Operations']) items"
    
    Stop-ScriptLogging -Success $success -Summary $summary -Statistics $script:Statistics
}
```

## 3. Script-Specific Implementation Priority

### High Priority (Core Operations)
1. **CrossVcenterVMmigration_list.ps1** - Main migration script
2. **Test-vCenterConnection.ps1** - Connection testing
3. **VMHostConfigV2.ps1** - Host configuration management
4. **Backup-ESXiHostConfig.ps1** - Host backup operations
5. **BackupVMConfigurations.ps1** - VM backup operations

### Medium Priority (Supporting Operations)
1. **Migrate-NetworkConfiguration.ps1** - Network migration
2. **Get-NetworkTopology.ps1** - Network discovery
3. **ResourcePool-export.ps1** / **ResourcePool-import.ps1** - Resource pool operations
4. **Move-EsxiHost.ps1** - Host migration

### Lower Priority (Utility Scripts)
1. **Get-Prerequisites.ps1** - Environment checking
2. **Install-PowerCli.ps1** - PowerCLI installation
3. **Get-TargetResources.ps1** - Resource discovery
4. **ValidateVMBackup.ps1** - Backup validation

## 4. Category Standards

Use consistent categories across scripts:

- **Initialization** - Script startup and configuration
- **Authentication** - Credential processing and vCenter connection
- **PowerCLI** - Module loading and PowerCLI operations
- **Network** - Network connectivity and configuration
- **VM-Migration** - Virtual machine migration operations
- **Host-Migration** - ESXi host migration operations
- **Backup** - Backup and restore operations
- **Validation** - Validation and verification operations
- **Performance** - Timing and performance metrics
- **Cleanup** - Script cleanup and disconnection
- **Configuration** - Configuration changes and settings
- **Error** - Error handling and recovery

## 5. Data Standards

Include relevant data with log entries:

```powershell
# For VM operations
-Data @{
    VM = $vmName
    Host = $hostName
    Datastore = $datastoreName
    PowerState = $powerState
}

# For connection operations
-Data @{
    Server = $serverName
    Version = $version
    User = $username
    SessionId = $sessionId
}

# For performance operations
-Data @{
    Duration = $duration
    ItemCount = $count
    ThroughputMBps = $throughput
}
```

## 6. Log Level Guidelines

- **Critical**: System failures, data loss scenarios
- **Error**: Operation failures that prevent completion
- **Warning**: Issues that don't prevent completion but need attention
- **Info**: General operational information (default level)
- **Success**: Successful completion of major operations
- **Verbose**: Detailed operational flow information
- **Debug**: Detailed technical information for troubleshooting
- **Performance**: Timing and performance measurements

## 7. Testing the Implementation

### Validation Checklist
- [ ] Script starts with proper logging initialization
- [ ] All major operations are logged with appropriate levels
- [ ] Errors include ErrorRecord for detailed troubleshooting
- [ ] Performance-critical operations use Measure-ScriptBlock
- [ ] Statistics are collected and reported
- [ ] Script ends with Stop-ScriptLogging
- [ ] Log files are created in expected location
- [ ] Console output is properly color-coded
- [ ] Log rotation works for large files

### Test Commands
```powershell
# Test with different log levels
.\YourScript.ps1 -LogLevel Debug -IncludeStackTrace

# Test with file-only logging
.\YourScript.ps1 -DisableConsoleOutput -CustomLogPath "C:\TestLogs\test.log"

# Verify log content
Get-Content "C:\Logs\PowerShell\YourScript_2024-01-15.log" | Select-Object -Last 20
```

## 8. Benefits of Enhanced Logging

### For Developers
- Consistent logging patterns across all scripts
- Rich debugging information with stack traces
- Performance metrics for optimization
- Automatic log management

### For Operations
- Centralized log location with rotation
- Session correlation across multiple scripts
- Clear error messaging with context
- Statistics for reporting and monitoring

### For Troubleshooting
- Detailed error information with stack traces
- Performance timing to identify bottlenecks
- Category-based filtering for specific issues
- Data context for understanding failures

## 9. Migration Timeline

### Phase 1 (Week 1): Core Scripts
- Update Write-ScriptLog.ps1 framework
- Implement in Test-vCenterConnection.ps1
- Implement in CrossVcenterVMmigration_list.ps1

### Phase 2 (Week 2): Supporting Scripts  
- Update VMHostConfigV2.ps1
- Update backup/restore scripts
- Update network configuration scripts

### Phase 3 (Week 3): Utility Scripts
- Update remaining utility scripts
- Test integration across all scripts
- Documentation and training

### Phase 4 (Week 4): Validation and Optimization
- Full system testing
- Performance optimization
- Final documentation
- Deployment preparation

This phased approach ensures critical functionality is enhanced first while maintaining system stability throughout the migration process.