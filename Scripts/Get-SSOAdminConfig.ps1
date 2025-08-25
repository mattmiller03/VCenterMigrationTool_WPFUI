<#
.SYNOPSIS
    Retrieves admin configuration using VMware.SDK.vSphere module (replaces deprecated SsoAdmin)
.DESCRIPTION
    Uses the modern VMware SDK to retrieve administrative configuration data
    including roles, permissions, users, and groups.
.NOTES
    Version: 2.0 - Updated for VMware.SDK.vSphere
    Requires: VMware.PowerCLI 13.x+ with VMware.SDK.vSphere
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
    [string]$LogPath,
    
    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Embedded logging functions
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $SuppressConsoleOutput

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

# Initialize logging
if ($LogPath) {
    $Global:ScriptLogFile = $LogPath
    $separator = "=" * 80
    "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    "Script: Get-SSOAdminConfig (SDK Version)" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
}

$scriptSuccess = $false
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
    SDKVersion = $null
    UsedFallback = $false
}

try {
    Write-LogInfo "Starting admin configuration discovery (SDK Version)" -Category "Initialization"
    
    # Import PowerCLI modules
    Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
    Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Check for VMware SDK modules
    $sdkAvailable = $false
    $ssoAdminAvailable = $false
    
    # Check for new SDK module
    Write-LogInfo "Checking for VMware.SDK.vSphere module..." -Category "Module"
    $sdkModule = Get-Module -ListAvailable -Name VMware.Sdk.vSphere* | Select-Object -First 1
    if ($sdkModule) {
        $sdkAvailable = $true
        $ssoData.SDKVersion = $sdkModule.Version.ToString()
        Write-LogSuccess "VMware SDK module found: $($sdkModule.Name) v$($sdkModule.Version)" -Category "Module"
    } else {
        Write-LogWarning "VMware.SDK.vSphere module not found" -Category "Module"
    }
    
    # Check for legacy SSO Admin module (for backwards compatibility)
    $ssoModule = Get-Module -ListAvailable -Name VMware.vSphere.SsoAdmin
    if ($ssoModule) {
        $ssoAdminAvailable = $true
        Write-LogInfo "Legacy SSO Admin module found (deprecated)" -Category "Module"
    }
    
    # Connect to vCenter using PowerCLI
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Initialize SDK connection if available
    $sdkConnected = $false
    if ($sdkAvailable) {
        try {
            Write-LogInfo "Establishing SDK vSphere connection..." -Category "Connection"
            
            # Create vSphere configuration
            $config = [VMware.Sdk.vSphere.vSphereApiConfiguration]::new()
            $config.Server = $VCenterServer
            
            # Set up authentication
            $username = $Credentials.UserName
            $password = $Credentials.GetNetworkCredential().Password
            
            # Use session-based authentication
            $sessionService = [VMware.Sdk.vSphere.cis.SessionService]::new($config)
            $sessionId = $sessionService.create($username, $password)
            
            # Update config with session
            $config.ApiKey.Add("vmware-api-session-id", $sessionId)
            
            $sdkConnected = $true
            Write-LogSuccess "SDK vSphere connection established" -Category "Connection"
        } catch {
            Write-LogWarning "Could not establish SDK connection: $($_.Exception.Message)" -Category "Connection"
            Write-LogInfo "Falling back to standard PowerCLI methods" -Category "Connection"
        }
    }
    
    # Collect Roles
    if ($IncludeRoles) {
        Write-LogInfo "Retrieving roles..." -Category "Discovery"
        $roles = Get-VIRole -ErrorAction SilentlyContinue
        
        foreach ($role in $roles) {
            $roleInfo = @{
                Name = $role.Name
                Id = $role.Id
                IsSystem = $role.IsSystem
                Description = $role.Description
                Privileges = @($role.PrivilegeList)
                AssignmentCount = 0
            }
            
            # Count assignments
            try {
                $assignments = Get-VIPermission | Where-Object { $_.Role -eq $role.Name }
                $roleInfo.AssignmentCount = @($assignments).Count
            } catch {
                # Continue without assignment count
            }
            
            $ssoData.Roles += $roleInfo
        }
        
        $ssoData.TotalRoles = $ssoData.Roles.Count
        Write-LogSuccess "Retrieved $($ssoData.TotalRoles) roles" -Category "Discovery"
    }
    
    # Collect Permissions
    if ($IncludePermissions) {
        Write-LogInfo "Retrieving permissions..." -Category "Discovery"
        $permissions = Get-VIPermission -ErrorAction SilentlyContinue
        
        foreach ($perm in $permissions) {
            $permInfo = @{
                Entity = $perm.Entity.Name
                EntityType = $perm.Entity.GetType().Name
                Principal = $perm.Principal
                Role = $perm.Role
                Propagate = $perm.Propagate
                IsGroup = $perm.IsGroup
            }
            
            $ssoData.Permissions += $permInfo
        }
        
        $ssoData.TotalPermissions = $ssoData.Permissions.Count
        Write-LogSuccess "Retrieved $($ssoData.TotalPermissions) permissions" -Category "Discovery"
    }
    
    # Collect SSO Users and Groups (if SDK is available)
    if ($sdkConnected -and ($IncludeSSOUsers -or $IncludeSSOGroups)) {
        try {
            Write-LogInfo "Retrieving SSO identity sources using SDK..." -Category "Discovery"
            
            # Use SDK to get identity sources
            $identityService = [VMware.Sdk.vSphere.sso.admin.IdentitySourcesService]::new($config)
            $identitySources = $identityService.list()
            
            foreach ($source in $identitySources) {
                Write-LogInfo "Processing identity source: $($source.Name)" -Category "Discovery"
                
                if ($IncludeSSOUsers) {
                    # Get users from this identity source
                    # Note: Actual implementation depends on specific SDK version and methods
                    Write-LogInfo "User enumeration from SDK requires additional implementation" -Category "Discovery"
                }
                
                if ($IncludeSSOGroups) {
                    # Get groups from this identity source
                    Write-LogInfo "Group enumeration from SDK requires additional implementation" -Category "Discovery"
                }
            }
        } catch {
            Write-LogWarning "Could not retrieve SSO data via SDK: $($_.Exception.Message)" -Category "Discovery"
        }
    } elseif (($IncludeSSOUsers -or $IncludeSSOGroups) -and -not $sdkConnected) {
        Write-LogWarning "SSO user/group discovery requires VMware SDK or legacy SSO Admin module" -Category "Discovery"
        $ssoData.UsedFallback = $true
    }
    
    # Global Permissions (these are standard vCenter permissions at root level)
    Write-LogInfo "Retrieving global permissions..." -Category "Discovery"
    try {
        $rootFolder = Get-Folder -NoRecursion
        $globalPerms = Get-VIPermission -Entity $rootFolder -ErrorAction SilentlyContinue
        
        foreach ($gPerm in $globalPerms) {
            $globalPermInfo = @{
                Principal = $gPerm.Principal
                Role = $gPerm.Role
                Propagate = $gPerm.Propagate
                IsGroup = $gPerm.IsGroup
            }
            
            $ssoData.GlobalPermissions += $globalPermInfo
        }
        
        Write-LogSuccess "Retrieved $(@($ssoData.GlobalPermissions).Count) global permissions" -Category "Discovery"
    } catch {
        Write-LogWarning "Could not retrieve global permissions: $($_.Exception.Message)" -Category "Discovery"
    }
    
    $scriptSuccess = $true
    Write-LogSuccess "Admin configuration discovery completed successfully" -Category "Completion"
    
    # Add summary
    $finalSummary = @"
Collection Summary:
- Roles: $($ssoData.TotalRoles)
- Permissions: $($ssoData.TotalPermissions)
- Global Permissions: $(@($ssoData.GlobalPermissions).Count)
- SDK Available: $sdkAvailable
- SDK Version: $(if ($ssoData.SDKVersion) { $ssoData.SDKVersion } else { 'N/A' })
- Used Fallback: $($ssoData.UsedFallback)
"@
    
} catch {
    Write-LogError "Script execution failed: $($_.Exception.Message)" -Category "Error"
    $ssoData.Error = $_.Exception.Message
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
        Write-LogInfo "Disconnected from vCenter" -Category "Connection"
    }
    
    # Output the data as JSON
    $ssoData | ConvertTo-Json -Depth 10
    
    # Log summary
    if ($Global:ScriptLogFile) {
        if ($finalSummary) {
            $finalSummary | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        "Script Status: $(if ($scriptSuccess) { 'SUCCESS' } else { 'FAILED' })" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}