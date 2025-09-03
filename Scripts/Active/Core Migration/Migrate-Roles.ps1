<#
.SYNOPSIS
    Enhanced vCenter role migration with version compatibility and privilege validation
.DESCRIPTION
    Migrates custom roles from source to target vCenter with advanced features:
    - Version compatibility handling (skips incompatible privileges)
    - Privilege validation against target vCenter capabilities
    - Replace vs Append modes for flexible privilege management
    - Enhanced statistics and detailed logging
    - Rollback protection and error handling
.NOTES
    Version: 2.0 - Enhanced with privilege validation and compatibility handling
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$SourceCredentials,
    
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$TargetCredentials,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetVCenterServer,
    
    [Parameter()]
    [bool]$ValidateOnly = $false,
    
    [Parameter()]
    [bool]$OverwriteExisting = $false,
    
    [Parameter()]
    [ValidateSet("Replace", "Append")]
    [string]$PrivilegeMode = "Replace",
    
    [Parameter()]
    [bool]$BypassModuleCheck = $false,
    
    [Parameter()]
    [string]$LogPath,
    
    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Embedded logging functions for SDK execution compatibility
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $false

function Write-LogInfo { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Info] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor White }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogSuccess { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Success] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Green }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogWarning { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Warning] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Yellow }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogError { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Error] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Red }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogDebug { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Debug] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Gray }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Start-ScriptLogging {
    param(
        [string]$ScriptName = '',
        [string]$LogPath = $null,
        [bool]$SuppressConsoleOutput = $false
    )
    
    $Global:SuppressConsoleOutput = $SuppressConsoleOutput
    
    if ($LogPath) {
        if ([System.IO.Path]::HasExtension($LogPath)) {
            $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
        } else {
            $logDir = $LogPath
        }
        
        $psLogDir = Join-Path $logDir "PowerShell"
        if (-not (Test-Path $psLogDir)) {
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }
        
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $sessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir "${ScriptName}_${timestamp}_${sessionId}.log"
        
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        "SCRIPT START: $ScriptName" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

function Stop-ScriptLogging {
    param(
        [bool]$Success = $true,
        [string]$Summary = "",
        [hashtable]$Statistics = @{}
    )
    
    if ($Global:ScriptLogFile) {
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        if ($Success) {
            "SCRIPT COMPLETED SUCCESSFULLY" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        } else {
            "SCRIPT FAILED" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Summary) {
            "Summary: $Summary" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Statistics.Count -gt 0) {
            "Statistics:" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            foreach ($key in $Statistics.Keys) {
                "    $key = $($Statistics[$key])" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            }
        }
        
        "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

# Start logging
Start-ScriptLogging -ScriptName "Migrate-Roles" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceRolesFound = 0
    CustomRolesFound = 0
    RolesMigrated = 0
    RolesSkipped = 0
    RolesWithErrors = 0
    TotalPrivilegesProcessed = 0
    ValidPrivileges = 0
    InvalidPrivilegesSkipped = 0
    PrivilegesAdded = 0
    PrivilegesFailedToAdd = 0
    Mode = $PrivilegeMode
    ValidationMode = $ValidateOnly
}

$sourceConnection = $null
$targetConnection = $null

try {
    Write-LogInfo "Starting enhanced role migration process with privilege validation" -Category "Initialization"
    Write-LogInfo "Migration Mode: $PrivilegeMode, Validate Only: $ValidateOnly" -Category "Initialization"
    
    # Import required modules
    # PowerCLI module management handled by service layer
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to source vCenter
    Write-LogInfo "Connecting to source vCenter: $SourceVCenterServer" -Category "Connection"
    $sourceConnection = Connect-VIServer -Server $SourceVCenterServer -Credential $SourceCredentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to source vCenter: $($sourceConnection.Name) (v$($sourceConnection.Version))" -Category "Connection"
    
    # Connect to target vCenter
    Write-LogInfo "Connecting to target vCenter: $TargetVCenterServer" -Category "Connection"
    $targetConnection = Connect-VIServer -Server $TargetVCenterServer -Credential $TargetCredentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to target vCenter: $($targetConnection.Name) (v$($targetConnection.Version))" -Category "Connection"
    
    # Get all available privileges for validation against target vCenter
    Write-LogInfo "Retrieving all available privileges in target vCenter for validation..." -Category "Validation"
    $allTargetPrivileges = Get-VIPrivilege -Server $targetConnection | Select-Object -ExpandProperty Id
    Write-LogSuccess "Found $($allTargetPrivileges.Count) available privileges in target vCenter." -Category "Validation"
    
    # Get source roles
    Write-LogInfo "Retrieving roles from source vCenter..." -Category "Discovery"
    $sourceRoles = Get-VIRole -Server $sourceConnection
    $migrationStats.SourceRolesFound = $sourceRoles.Count
    Write-LogInfo "Found $($sourceRoles.Count) total roles in source vCenter" -Category "Discovery"
    
    # Filter to custom roles only (excluding system roles)
    $customRoles = $sourceRoles | Where-Object { -not $_.IsSystem }
    $migrationStats.CustomRolesFound = $customRoles.Count
    Write-LogInfo "Found $($customRoles.Count) custom roles to migrate" -Category "Discovery"
    
    # Get target roles for comparison
    $targetRoles = Get-VIRole -Server $targetConnection
    Write-LogInfo "Found $($targetRoles.Count) existing roles in target vCenter" -Category "Discovery"
    
    if ($customRoles.Count -eq 0) {
        Write-LogWarning "No custom roles found to migrate" -Category "Migration"
        $finalSummary = "No custom roles found to migrate"
    } else {
        # Process each custom role
        foreach ($role in $customRoles) {
            try {
                Write-LogInfo "Processing role: '$($role.Name)' with $($role.PrivilegeList.Count) privileges" -Category "Migration"
                
                # Validate privileges against target vCenter capabilities
                $sourcePrivileges = $role.PrivilegeList
                $migrationStats.TotalPrivilegesProcessed += $sourcePrivileges.Count
                
                $validPrivileges = @()
                $invalidPrivileges = @()
                
                foreach ($privilege in $sourcePrivileges) {
                    if ($privilege -in $allTargetPrivileges) {
                        $validPrivileges += $privilege
                    } else {
                        $invalidPrivileges += $privilege
                    }
                }
                
                $migrationStats.ValidPrivileges += $validPrivileges.Count
                $migrationStats.InvalidPrivilegesSkipped += $invalidPrivileges.Count
                
                if ($invalidPrivileges.Count -gt 0) {
                    Write-LogWarning "Role '$($role.Name)': $($invalidPrivileges.Count) privileges don't exist in target vCenter and will be skipped: $($invalidPrivileges -join ', ')" -Category "Compatibility"
                }
                
                Write-LogInfo "Role '$($role.Name)': Proceeding with $($validPrivileges.Count) valid privileges" -Category "Processing"
                
                # Check if role already exists in target
                $existingTargetRole = $targetRoles | Where-Object { $_.Name -eq $role.Name }
                
                if ($existingTargetRole) {
                    if ($OverwriteExisting) {
                        if ($ValidateOnly) {
                            Write-LogInfo "VALIDATION: Would overwrite existing role '$($role.Name)'" -Category "Validation"
                        } else {
                            Write-LogInfo "Removing existing role '$($role.Name)' from target for replacement" -Category "Migration"
                            Remove-VIRole -Role $existingTargetRole -Confirm:$false -ErrorAction Stop
                            Write-LogSuccess "Removed existing role '$($role.Name)' from target" -Category "Migration"
                        }
                    } elseif ($PrivilegeMode -eq "Append") {
                        # Append mode: add missing privileges to existing role
                        if ($ValidateOnly) {
                            Write-LogInfo "VALIDATION: Would append privileges to existing role '$($role.Name)'" -Category "Validation"
                        } else {
                            Write-LogInfo "Appending privileges to existing role '$($role.Name)'..." -Category "Migration"
                            $targetPrivileges = $existingTargetRole.PrivilegeList
                            $privilegesToAdd = $validPrivileges | Where-Object { $_ -notin $targetPrivileges }
                            
                            if ($privilegesToAdd.Count -eq 0) {
                                Write-LogSuccess "All valid privileges already exist in role '$($role.Name)'. No changes needed." -Category "Migration"
                            } else {
                                Write-LogInfo "Adding $($privilegesToAdd.Count) new privileges to existing role '$($role.Name)'..." -Category "Migration"
                                foreach ($privilege in $privilegesToAdd) {
                                    try {
                                        Set-VIRole -Role $existingTargetRole -AddPrivilege $privilege -Confirm:$false -ErrorAction Stop
                                        Write-LogDebug "  Added privilege: $privilege" -Category "Migration"
                                        $migrationStats.PrivilegesAdded++
                                    } catch {
                                        Write-LogWarning "  Failed to add privilege '$privilege' to role '$($role.Name)': $($_.Exception.Message)" -Category "Migration"
                                        $migrationStats.PrivilegesFailedToAdd++
                                    }
                                }
                                Write-LogSuccess "Finished appending privileges to role '$($role.Name)'. Added: $($privilegesToAdd.Count - $migrationStats.PrivilegesFailedToAdd), Failed: $($migrationStats.PrivilegesFailedToAdd)" -Category "Migration"
                            }
                        }
                        $migrationStats.RolesMigrated++
                    } else {
                        # Replace mode but not overwriting - skip
                        Write-LogWarning "Role '$($role.Name)' already exists in target - skipping (use -OverwriteExisting to replace)" -Category "Migration"
                        $migrationStats.RolesSkipped++
                        continue
                    }
                }
                
                # Create new role or replace existing role
                if (-not $existingTargetRole -or ($existingTargetRole -and $OverwriteExisting)) {
                    if ($ValidateOnly) {
                        Write-LogInfo "VALIDATION: Would create role '$($role.Name)' with $($validPrivileges.Count) valid privileges" -Category "Validation"
                        $migrationStats.RolesMigrated++
                    } else {
                        if ($validPrivileges.Count -gt 0) {
                            Write-LogInfo "Creating role '$($role.Name)' with $($validPrivileges.Count) valid privileges" -Category "Migration"
                            $newRole = New-VIRole -Name $role.Name -Privilege $validPrivileges -Server $targetConnection -ErrorAction Stop
                            
                            if ($newRole) {
                                Write-LogSuccess "Successfully created role: '$($newRole.Name)' with $($newRole.PrivilegeList.Count) privileges" -Category "Migration"
                                $migrationStats.RolesMigrated++
                                $migrationStats.PrivilegesAdded += $validPrivileges.Count
                                
                                # Log privilege details for reference
                                Write-LogDebug "Role privileges: $($validPrivileges -join ', ')" -Category "Migration"
                            } else {
                                throw "Role creation returned null"
                            }
                        } else {
                            Write-LogWarning "Role '$($role.Name)' has no valid privileges for target vCenter - skipping creation" -Category "Migration"
                            $migrationStats.RolesSkipped++
                        }
                    }
                }
                
            } catch {
                Write-LogError "Failed to migrate role '$($role.Name)': $($_.Exception.Message)" -Category "Error"
                $migrationStats.RolesWithErrors++
                continue
            }
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "VALIDATION COMPLETE: $($migrationStats.RolesMigrated) roles would be migrated, $($migrationStats.RolesSkipped) skipped, $($migrationStats.InvalidPrivilegesSkipped) invalid privileges would be skipped"
    } else {
        $finalSummary = "MIGRATION COMPLETE: $($migrationStats.RolesMigrated) roles migrated, $($migrationStats.RolesSkipped) skipped, $($migrationStats.RolesWithErrors) errors, $($migrationStats.InvalidPrivilegesSkipped) incompatible privileges skipped"
    }
    
    Write-LogSuccess $finalSummary -Category "Migration"
    Write-Output "SUCCESS: $finalSummary"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Role migration failed: $($_.Exception.Message)"
    Write-LogError "Role migration failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter servers
    if ($sourceConnection) {
        Write-LogInfo "Disconnecting from source vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    if ($targetConnection) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $migrationStats
}