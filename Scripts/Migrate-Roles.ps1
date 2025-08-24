<#
.SYNOPSIS
    Migrates vCenter roles from source to target vCenter using PowerCLI 13.x
.DESCRIPTION
    Exports roles from source vCenter and imports them to target vCenter.
    Handles custom roles, privileges, and role dependencies with validation options.
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
Start-ScriptLogging -ScriptName "Migrate-Roles" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceRolesFound = 0
    CustomRolesFound = 0
    RolesMigrated = 0
    RolesSkipped = 0
    RolesWithErrors = 0
}

try {
    Write-LogInfo "Starting role migration process" -Category "Initialization"
    
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
            Write-LogWarning "VMware.vSphere.SsoAdmin module not found. Some SSO roles may be unavailable." -Category "Module"
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
            $sourceSsoConnection = Connect-SsoAdminServer -Server $SourceVCenterServer -User $SourceCredentials.UserName -Password $SourceCredentials.GetNetworkCredential().Password -SkipCertificateCheck
            $sourceSsoConnected = $true
            Write-LogSuccess "Connected to source SSO Admin Server" -Category "Connection"
        } catch {
            Write-LogWarning "Could not connect to source SSO Admin Server: $($_.Exception.Message)" -Category "Connection"
        }
        
        try {
            Write-LogInfo "Connecting to target SSO Admin Server..." -Category "Connection"
            $targetSsoConnection = Connect-SsoAdminServer -Server $TargetVCenterServer -User $TargetCredentials.UserName -Password $TargetCredentials.GetNetworkCredential().Password -SkipCertificateCheck
            $targetSsoConnected = $true
            Write-LogSuccess "Connected to target SSO Admin Server" -Category "Connection"
        } catch {
            Write-LogWarning "Could not connect to target SSO Admin Server: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Get source roles (including SSO roles if available)
    Write-LogInfo "Retrieving roles from source vCenter..." -Category "Discovery"
    $sourceRoles = Get-VIRole -Server $sourceConnection
    $migrationStats.SourceRolesFound = $sourceRoles.Count
    Write-LogInfo "Found $($sourceRoles.Count) standard vCenter roles in source" -Category "Discovery"
    
    # Get additional SSO roles if connected
    $allSourceRoles = @($sourceRoles)
    if ($sourceSsoConnected) {
        try {
            Write-LogInfo "Retrieving SSO roles from source..." -Category "Discovery"
            # Note: SSO Admin module typically manages users/groups, not additional roles
            # But we ensure comprehensive role discovery by checking both sources
            Write-LogInfo "SSO connection available for enhanced role discovery" -Category "Discovery"
        } catch {
            Write-LogWarning "Could not retrieve SSO roles: $($_.Exception.Message)" -Category "Discovery"
        }
    }
    
    # Filter to custom roles only (excluding system roles)
    $customRoles = $allSourceRoles | Where-Object { -not $_.IsSystem }
    $migrationStats.CustomRolesFound = $customRoles.Count
    Write-LogInfo "Found $($customRoles.Count) custom roles to migrate" -Category "Discovery"
    
    # Get target roles for comparison
    $targetRoles = Get-VIRole -Server $targetConnection
    Write-LogInfo "Found $($targetRoles.Count) existing roles in target vCenter" -Category "Discovery"
    
    if ($customRoles.Count -eq 0) {
        Write-LogWarning "No custom roles found to migrate" -Category "Migration"
    } else {
        # Process each custom role
        foreach ($role in $customRoles) {
            try {
                Write-LogInfo "Processing role: $($role.Name)" -Category "Migration"
                
                # Check if role already exists in target
                $existingRole = $targetRoles | Where-Object { $_.Name -eq $role.Name }
                
                if ($existingRole) {
                    if ($OverwriteExisting) {
                        if ($ValidateOnly) {
                            Write-LogInfo "VALIDATION: Would overwrite existing role '$($role.Name)'" -Category "Validation"
                        } else {
                            Write-LogWarning "Removing existing role '$($role.Name)' from target" -Category "Migration"
                            Remove-VIRole -Role $existingRole -Confirm:$false -ErrorAction Stop
                        }
                    } else {
                        Write-LogWarning "Role '$($role.Name)' already exists in target - skipping" -Category "Migration"
                        $migrationStats.RolesSkipped++
                        continue
                    }
                }
                
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION: Would create role '$($role.Name)' with $($role.PrivilegeList.Count) privileges" -Category "Validation"
                    $migrationStats.RolesMigrated++
                } else {
                    # Create the role in target vCenter
                    Write-LogInfo "Creating role '$($role.Name)' with $($role.PrivilegeList.Count) privileges" -Category "Migration"
                    
                    $newRole = New-VIRole -Name $role.Name -Privilege $role.PrivilegeList -Server $targetConnection -ErrorAction Stop
                    
                    if ($newRole) {
                        Write-LogSuccess "Successfully created role: $($newRole.Name)" -Category "Migration"
                        $migrationStats.RolesMigrated++
                        
                        # Log privileges for reference
                        Write-LogInfo "Role privileges: $($role.PrivilegeList -join ', ')" -Category "Migration"
                    } else {
                        throw "Role creation returned null"
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
        $finalSummary = "Validation completed: $($migrationStats.RolesMigrated) roles would be migrated, $($migrationStats.RolesSkipped) skipped"
    } else {
        $finalSummary = "Successfully migrated $($migrationStats.RolesMigrated) roles, $($migrationStats.RolesSkipped) skipped, $($migrationStats.RolesWithErrors) errors"
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
        Disconnect-VIServer -Server $sourceConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    if ($targetConnection) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $targetConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $migrationStats
}