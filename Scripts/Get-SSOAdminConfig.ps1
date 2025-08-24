<#
.SYNOPSIS
    Retrieves SSO Admin configuration including roles, permissions, users, and groups using VMware.vSphere.SsoAdmin module
.DESCRIPTION
    Connects to vCenter SSO and retrieves comprehensive administrative configuration data
    including SSO domains, users, groups, roles, and permissions.
.NOTES
    Version: 1.0 - Requires VMware.vSphere.SsoAdmin module
    Requires: VMware.PowerCLI 13.x and VMware.vSphere.SsoAdmin module
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
    [Parameter()]
    [bool]$IncludeRoles = $true,
    
    [Parameter()]
    [bool]$IncludePermissions = $true,
    
    [Parameter()]
    [bool]$IncludeSSOUsers = $false,
    
    [Parameter()]
    [bool]$IncludeSSOGroups = $false,
    
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
Start-ScriptLogging -ScriptName "Get-SSOAdminConfig" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$ssoData = @{
    CollectionDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    VCenterServer = $VCenterServer
    Roles = @()
    Permissions = @()
    SSOUsers = @()
    SSOGroups = @()
    GlobalPermissions = @()
    TotalRoles = 0
    TotalPermissions = 0
}

try {
    Write-LogInfo "Starting SSO admin configuration discovery" -Category "Initialization"
    
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
            Write-LogWarning "VMware.vSphere.SsoAdmin module not found. Some SSO data may be unavailable." -Category "Module"
        }
    }
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Connect to SSO Admin if module is available
    $ssoConnected = $false
    if (Get-Command Connect-SsoAdminServer -ErrorAction SilentlyContinue) {
        try {
            Write-LogInfo "Connecting to SSO Admin Server..." -Category "Connection"
            $ssoConnection = Connect-SsoAdminServer -Server $VCenterServer -Credential $Credentials -SkipCertificateCheck
            $ssoConnected = $true
            Write-LogSuccess "Connected to SSO Admin Server" -Category "Connection"
        } catch {
            Write-LogWarning "Could not connect to SSO Admin Server: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Get Roles (including SSO roles if available)
    if ($IncludeRoles) {
        Write-LogInfo "Retrieving roles..." -Category "Discovery"
        
        # Get standard vCenter roles
        $viRoles = Get-VIRole -ErrorAction SilentlyContinue
        foreach ($role in $viRoles) {
            $roleInfo = @{
                Name = $role.Name
                Id = $role.Id
                IsSystem = $role.IsSystem
                Description = $role.Description
                Privileges = @($role.PrivilegeList)
                AssignmentCount = 0
                Type = "VIRole"
            }
            
            # Count assignments for this role
            try {
                $assignments = Get-VIPermission | Where-Object { $_.Role -eq $role.Name }
                $roleInfo.AssignmentCount = @($assignments).Count
            } catch {
                Write-LogWarning "Could not count assignments for role '$($role.Name)'" -Category "Discovery"
            }
            
            $ssoData.Roles += $roleInfo
        }
        
        $ssoData.TotalRoles = $ssoData.Roles.Count
        Write-LogInfo "Found $($ssoData.TotalRoles) roles" -Category "Discovery"
    }
    
    # Get Permissions (including global permissions)
    if ($IncludePermissions) {
        Write-LogInfo "Retrieving permissions..." -Category "Discovery"
        
        # Get standard vCenter permissions
        $viPermissions = Get-VIPermission -ErrorAction SilentlyContinue
        foreach ($perm in $viPermissions) {
            $permInfo = @{
                Id = [System.Guid]::NewGuid().ToString()
                Principal = $perm.Principal
                PrincipalType = if ($perm.IsGroup) { "Group" } else { "User" }
                Role = $perm.Role
                Entity = if ($perm.Entity) { $perm.Entity.Name } else { "Root" }
                EntityType = if ($perm.Entity) { $perm.Entity.GetType().Name } else { "Root" }
                EntityId = if ($perm.Entity) { $perm.Entity.Id } else { "Root" }
                Propagate = $perm.Propagate
                Type = "VIPermission"
            }
            
            $ssoData.Permissions += $permInfo
        }
        
        # Get Global Permissions using Get-VIPermission with special parameters
        try {
            Write-LogInfo "Retrieving global permissions..." -Category "Discovery"
            $rootFolder = Get-Folder -NoRecursion
            $globalPerms = Get-VIPermission -Entity $rootFolder -ErrorAction SilentlyContinue
            
            foreach ($globalPerm in $globalPerms) {
                $globalPermInfo = @{
                    Id = [System.Guid]::NewGuid().ToString()
                    Principal = $globalPerm.Principal
                    PrincipalType = if ($globalPerm.IsGroup) { "Group" } else { "User" }
                    Role = $globalPerm.Role
                    Propagate = $globalPerm.Propagate
                    Type = "GlobalPermission"
                }
                
                $ssoData.GlobalPermissions += $globalPermInfo
            }
            
            Write-LogInfo "Found $($ssoData.GlobalPermissions.Count) global permissions" -Category "Discovery"
        } catch {
            Write-LogWarning "Could not retrieve global permissions: $($_.Exception.Message)" -Category "Discovery"
        }
        
        $ssoData.TotalPermissions = $ssoData.Permissions.Count + $ssoData.GlobalPermissions.Count
        Write-LogInfo "Found $($ssoData.TotalPermissions) total permissions" -Category "Discovery"
    }
    
    # Get SSO Users if connected and requested
    if ($IncludeSSOUsers -and $ssoConnected) {
        Write-LogInfo "Retrieving SSO users..." -Category "Discovery"
        try {
            # Get SSO domains
            $ssoDomains = Get-SsoPersonUser -Domain * -ErrorAction SilentlyContinue
            foreach ($user in $ssoDomains) {
                $userInfo = @{
                    Name = $user.Name
                    Domain = $user.Domain
                    Email = $user.EmailAddress
                    FirstName = $user.FirstName
                    LastName = $user.LastName
                    Description = $user.Description
                    Disabled = $user.Disabled
                    Type = "SSOUser"
                }
                $ssoData.SSOUsers += $userInfo
            }
            Write-LogInfo "Found $($ssoData.SSOUsers.Count) SSO users" -Category "Discovery"
        } catch {
            Write-LogWarning "Could not retrieve SSO users: $($_.Exception.Message)" -Category "Discovery"
        }
    }
    
    # Get SSO Groups if connected and requested
    if ($IncludeSSOGroups -and $ssoConnected) {
        Write-LogInfo "Retrieving SSO groups..." -Category "Discovery"
        try {
            $ssoGroups = Get-SsoGroup -Domain * -ErrorAction SilentlyContinue
            foreach ($group in $ssoGroups) {
                $groupInfo = @{
                    Name = $group.Name
                    Domain = $group.Domain
                    Description = $group.Description
                    Type = "SSOGroup"
                }
                $ssoData.SSOGroups += $groupInfo
            }
            Write-LogInfo "Found $($ssoData.SSOGroups.Count) SSO groups" -Category "Discovery"
        } catch {
            Write-LogWarning "Could not retrieve SSO groups: $($_.Exception.Message)" -Category "Discovery"
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "Successfully discovered $($ssoData.TotalRoles) roles and $($ssoData.TotalPermissions) permissions"
    
    # Output discovery data as JSON for the application
    $jsonOutput = $ssoData | ConvertTo-Json -Depth 10
    Write-Output $jsonOutput
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "SSO admin config discovery failed: $($_.Exception.Message)"
    Write-LogError "Discovery failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from SSO Admin if connected
    if ($ssoConnected) {
        try {
            Write-LogInfo "Disconnecting from SSO Admin Server..." -Category "Cleanup"
            Disconnect-SsoAdminServer -Server $ssoConnection -ErrorAction SilentlyContinue
        } catch {
            Write-LogWarning "Error disconnecting from SSO Admin" -Category "Cleanup"
        }
    }
    
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    $discoveryStats = @{
        VCenterServer = $VCenterServer
        RolesFound = $ssoData.TotalRoles
        PermissionsFound = $ssoData.TotalPermissions
        SSOUsersFound = $ssoData.SSOUsers.Count
        SSOGroupsFound = $ssoData.SSOGroups.Count
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $discoveryStats
}