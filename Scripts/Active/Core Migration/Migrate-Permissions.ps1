<#
.SYNOPSIS
    Migrates vCenter permissions from source to target vCenter using PowerCLI 13.x
.DESCRIPTION
    Exports permissions from source vCenter and recreates them on target vCenter.
    Handles user/group permissions, role assignments, and entity mappings with validation options.
.NOTES
    Version: 1.0 - PowerCLI 13.x optimized
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
    [bool]$SkipMissingEntities = $true,
    
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
Start-ScriptLogging -ScriptName "Migrate-Permissions" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourcePermissionsFound = 0
    PermissionsMigrated = 0
    PermissionsSkipped = 0
    PermissionsWithErrors = 0
    MissingEntities = 0
    MissingRoles = 0
}

try {
    Write-LogInfo "Starting permissions migration process" -Category "Initialization"
    
    # Import required modules
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
        
        # Check for and import SSO Admin module
        Write-LogInfo "Checking for VMware.vSphere.SsoAdmin module..." -Category "Module"
        $ssoModule = Get-Module -ListAvailable -Name VMware.vSphere.SsoAdmin
        if ($ssoModule) {
            Import-Module VMware.vSphere.SsoAdmin -Force -ErrorAction Stop
            Write-LogSuccess "SSO Admin module imported successfully" -Category "Module"
        } else {
            Write-LogWarning "VMware.vSphere.SsoAdmin module not found. Some SSO permissions may be unavailable." -Category "Module"
        }
        Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
    }
    
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
    
    # Connect to SSO Admin servers if module is available
    $sourceSsoConnected = $false
    $targetSsoConnected = $false
    if (Get-Command Connect-SsoAdminServer -ErrorAction SilentlyContinue) {
        try {
            Write-LogInfo "Connecting to source SSO Admin Server..." -Category "Connection"
            $sourceSsoConnection = Connect-SsoAdminServer -Server $SourceVCenterServer -Credential $SourceCredentials -SkipCertificateCheck
            $sourceSsoConnected = $true
            Write-LogSuccess "Connected to source SSO Admin Server" -Category "Connection"
        } catch {
            Write-LogWarning "Could not connect to source SSO Admin Server: $($_.Exception.Message)" -Category "Connection"
        }
        
        try {
            Write-LogInfo "Connecting to target SSO Admin Server..." -Category "Connection"
            $targetSsoConnection = Connect-SsoAdminServer -Server $TargetVCenterServer -Credential $TargetCredentials -SkipCertificateCheck
            $targetSsoConnected = $true
            Write-LogSuccess "Connected to target SSO Admin Server" -Category "Connection"
        } catch {
            Write-LogWarning "Could not connect to target SSO Admin Server: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Get source permissions (including global permissions and SSO-specific permissions)
    Write-LogInfo "Retrieving permissions from source vCenter..." -Category "Discovery"
    $sourcePermissions = Get-VIPermission -Server $sourceConnection
    
    # Get global permissions using root folder
    try {
        Write-LogInfo "Retrieving global permissions from source..." -Category "Discovery"
        $rootFolder = Get-Folder -NoRecursion -Server $sourceConnection
        $globalPermissions = Get-VIPermission -Entity $rootFolder -Server $sourceConnection -ErrorAction SilentlyContinue
        
        # Combine standard and global permissions
        $allSourcePermissions = @($sourcePermissions) + @($globalPermissions)
        # Remove duplicates based on Principal, Entity, and Role
        $allSourcePermissions = $allSourcePermissions | Sort-Object Principal, Entity, Role -Unique
        
        Write-LogInfo "Found $($globalPermissions.Count) global permissions" -Category "Discovery"
    } catch {
        Write-LogWarning "Could not retrieve global permissions: $($_.Exception.Message)" -Category "Discovery"
        $allSourcePermissions = $sourcePermissions
    }
    
    $migrationStats.SourcePermissionsFound = $allSourcePermissions.Count
    Write-LogInfo "Found $($allSourcePermissions.Count) total permissions in source vCenter" -Category "Discovery"
    
    # Log SSO connectivity status for enhanced permission discovery
    if ($sourceSsoConnected) {
        Write-LogInfo "SSO Admin connection available for enhanced permission discovery" -Category "Discovery"
    }
    
    # Get target entities and roles for mapping
    Write-LogInfo "Building entity mapping for target vCenter..." -Category "Discovery"
    $targetInventory = Get-Inventory -Server $targetConnection
    $targetRoles = Get-VIRole -Server $targetConnection
    Write-LogInfo "Found $($targetInventory.Count) entities and $($targetRoles.Count) roles in target" -Category "Discovery"
    
    # Get existing permissions in target to check for duplicates
    $targetPermissions = Get-VIPermission -Server $targetConnection
    Write-LogInfo "Found $($targetPermissions.Count) existing permissions in target vCenter" -Category "Discovery"
    
    if ($allSourcePermissions.Count -eq 0) {
        Write-LogWarning "No permissions found to migrate" -Category "Migration"
    } else {
        # Process each permission
        foreach ($permission in $allSourcePermissions) {
            try {
                Write-LogInfo "Processing permission for principal: $($permission.Principal)" -Category "Migration"
                
                # Find the target entity by name and type
                $targetEntity = $null
                if ($permission.Entity) {
                    $entityName = $permission.Entity.Name
                    $entityType = $permission.Entity.GetType().Name
                    
                    $targetEntity = $targetInventory | Where-Object { 
                        $_.Name -eq $entityName -and $_.GetType().Name -eq $entityType 
                    } | Select-Object -First 1
                    
                    if (-not $targetEntity) {
                        if ($SkipMissingEntities) {
                            Write-LogWarning "Entity '$entityName' ($entityType) not found in target - skipping permission" -Category "Migration"
                            $migrationStats.MissingEntities++
                            $migrationStats.PermissionsSkipped++
                            continue
                        } else {
                            throw "Required entity '$entityName' ($entityType) not found in target vCenter"
                        }
                    }
                }
                
                # Find the target role
                $targetRole = $targetRoles | Where-Object { $_.Name -eq $permission.Role }
                if (-not $targetRole) {
                    Write-LogError "Role '$($permission.Role)' not found in target vCenter" -Category "Migration"
                    $migrationStats.MissingRoles++
                    $migrationStats.PermissionsWithErrors++
                    continue
                }
                
                # Check if permission already exists
                $existingPermission = $targetPermissions | Where-Object {
                    $_.Principal -eq $permission.Principal -and
                    $_.Entity.Name -eq $targetEntity.Name -and
                    $_.Role -eq $permission.Role
                }
                
                if ($existingPermission) {
                    if ($OverwriteExisting) {
                        if ($ValidateOnly) {
                            Write-LogInfo "VALIDATION: Would overwrite existing permission for '$($permission.Principal)'" -Category "Validation"
                        } else {
                            Write-LogWarning "Removing existing permission for '$($permission.Principal)' on '$($targetEntity.Name)'" -Category "Migration"
                            Remove-VIPermission -Permission $existingPermission -Confirm:$false -ErrorAction Stop
                        }
                    } else {
                        Write-LogWarning "Permission already exists for '$($permission.Principal)' on '$($targetEntity.Name)' - skipping" -Category "Migration"
                        $migrationStats.PermissionsSkipped++
                        continue
                    }
                }
                
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION: Would create permission '$($permission.Principal)' -> '$($permission.Role)' on '$($targetEntity.Name)'" -Category "Validation"
                    $migrationStats.PermissionsMigrated++
                } else {
                    # Create the permission in target vCenter
                    Write-LogInfo "Creating permission: '$($permission.Principal)' -> '$($permission.Role)' on '$($targetEntity.Name)'" -Category "Migration"
                    
                    $newPermission = New-VIPermission -Entity $targetEntity -Principal $permission.Principal -Role $targetRole -Propagate:$permission.Propagate -Server $targetConnection -ErrorAction Stop
                    
                    if ($newPermission) {
                        Write-LogSuccess "Successfully created permission for: $($permission.Principal)" -Category "Migration"
                        $migrationStats.PermissionsMigrated++
                    } else {
                        throw "Permission creation returned null"
                    }
                }
                
            } catch {
                Write-LogError "Failed to migrate permission for '$($permission.Principal)': $($_.Exception.Message)" -Category "Error"
                $migrationStats.PermissionsWithErrors++
                continue
            }
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "Validation completed: $($migrationStats.PermissionsMigrated) permissions would be migrated, $($migrationStats.PermissionsSkipped) skipped"
    } else {
        $finalSummary = "Successfully migrated $($migrationStats.PermissionsMigrated) permissions, $($migrationStats.PermissionsSkipped) skipped, $($migrationStats.PermissionsWithErrors) errors"
    }
    
    Write-LogSuccess $finalSummary -Category "Migration"
    Write-Output "SUCCESS: $finalSummary"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Permissions migration failed: $($_.Exception.Message)"
    Write-LogError "Permissions migration failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from SSO Admin servers if connected
    if ($sourceSsoConnected) {
        try {
            Write-LogInfo "Disconnecting from source SSO Admin Server..." -Category "Cleanup"
            Disconnect-SsoAdminServer -Server $sourceSsoConnection -ErrorAction SilentlyContinue
        } catch {
            Write-LogWarning "Error disconnecting from source SSO Admin" -Category "Cleanup"
        }
    }
    
    if ($targetSsoConnected) {
        try {
            Write-LogInfo "Disconnecting from target SSO Admin Server..." -Category "Cleanup"
            Disconnect-SsoAdminServer -Server $targetSsoConnection -ErrorAction SilentlyContinue
        } catch {
            Write-LogWarning "Error disconnecting from target SSO Admin" -Category "Cleanup"
        }
    }
    
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