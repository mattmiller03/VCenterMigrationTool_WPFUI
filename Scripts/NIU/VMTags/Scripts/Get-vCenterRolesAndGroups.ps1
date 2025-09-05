<#
.SYNOPSIS
    Connects to vCenter Server and exports roles, privileges, and local groups information
.DESCRIPTION
    This script connects to a vCenter Server and retrieves:
    - All roles and their assigned privileges
    - All local groups and their members
    Supports both VMware.PowerCLI and VCF.PowerCLI modules
.PARAMETER vCenterServer
    The vCenter Server FQDN or IP address
.PARAMETER Credential
    PSCredential object for authentication (optional - will prompt if not provided)
.PARAMETER OutputPath
    Path where the output files will be saved (default: current directory)
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$vCenterServer,
    
    [Parameter(Mandatory=$false)]
    [PSCredential]$Credential,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "."
)

# Function to check and import PowerCLI modules
function Import-PowerCLIModule {
    $moduleLoaded = $false
    $loadedModule = ""
    
    # Try to import VMware.PowerCLI first
    try {
        Write-Host "Attempting to load VMware.PowerCLI module..." -ForegroundColor Yellow
        Import-Module VMware.PowerCLI -ErrorAction Inquire
        $moduleLoaded = $true
        $loadedModule = "VMware.PowerCLI"
        Write-Host "VMware.PowerCLI module loaded successfully" -ForegroundColor Green
    }
    catch {
        Write-Host "VMware.PowerCLI not found or failed to load. Trying VCF.PowerCLI..." -ForegroundColor Yellow
        
        # Try to import VCF.PowerCLI as fallback
        try {
            Import-Module VCF.PowerCLI -ErrorAction Inquire
            $moduleLoaded = $true
            $loadedModule = "VCF.PowerCLI"
            Write-Host "VCF.PowerCLI module loaded successfully" -ForegroundColor Green
        }
        catch {
            Write-Error "Failed to import either VMware.PowerCLI or VCF.PowerCLI modules."
            Write-Host "Please install one of the following PowerCLI modules:" -ForegroundColor Yellow
            Write-Host "  Option 1: Install-Module -Name VMware.PowerCLI -Scope CurrentUser" -ForegroundColor Cyan
            Write-Host "  Option 2: Install-Module -Name VCF.PowerCLI -Scope CurrentUser" -ForegroundColor Cyan
            return $null
        }
    }
    
    return @{
        Loaded = $moduleLoaded
        ModuleName = $loadedModule
    }
}

# Function to check available modules
function Get-AvailablePowerCLIModules {
    $availableModules = @()
    
    # Check for VMware.PowerCLI
    $vmwareModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    if ($vmwareModule) {
        $availableModules += [PSCustomObject]@{
            Name = "VMware.PowerCLI"
            Version = $vmwareModule.Version | Select-Object -First 1
            Status = "Available"
        }
    }
    
    # Check for VCF.PowerCLI
    $vcfModule = Get-Module -ListAvailable -Name "VCF.PowerCLI" -ErrorAction SilentlyContinue
    if ($vcfModule) {
        $availableModules += [PSCustomObject]@{
            Name = "VCF.PowerCLI"
            Version = $vcfModule.Version | Select-Object -First 1
            Status = "Available"
        }
    }
    
    return $availableModules
}

# Check what PowerCLI modules are available
Write-Host "Checking for available PowerCLI modules..." -ForegroundColor Yellow
$availableModules = Get-AvailablePowerCLIModules

if ($availableModules.Count -gt 0) {
    Write-Host "Found the following PowerCLI modules:" -ForegroundColor Green
    $availableModules | Format-Table -AutoSize
} else {
    Write-Host "No PowerCLI modules found installed." -ForegroundColor Red
    Write-Host "Please install one of the following:" -ForegroundColor Yellow
    Write-Host "  VMware.PowerCLI: Install-Module -Name VMware.PowerCLI -Scope CurrentUser" -ForegroundColor Cyan
    Write-Host "  VCF.PowerCLI: Install-Module -Name VCF.PowerCLI -Scope CurrentUser" -ForegroundColor Cyan
    exit 1
}

# Import PowerCLI module
$moduleStatus = Import-PowerCLIModule
if (-not $moduleStatus) {
    exit 1
}

Write-Host "Using module: $($moduleStatus.ModuleName)" -ForegroundColor Green

# Disable certificate warnings (remove this line if you want certificate validation)
try {
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
    Write-Host "Certificate validation disabled" -ForegroundColor Yellow
}
catch {
    Write-Warning "Could not set PowerCLI configuration: $($_.Exception.Message)"
}

# Get credentials if not provided
if (-not $Credential) {
    $Credential = Get-Credential -Message "Enter vCenter Server credentials"
}

try {
    # Connect to vCenter Server
    Write-Host "Connecting to vCenter Server: $vCenterServer" -ForegroundColor Yellow
    $connection = Connect-VIServer -Server $vCenterServer -Credential $Credential -ErrorAction Stop
    Write-Host "Successfully connected to $vCenterServer" -ForegroundColor Green
    
    # Create output arrays
    $rolesOutput = @()
    $groupsOutput = @()
    $permissionsOutput = @()
    
    Write-Host "`nRetrieving roles and privileges..." -ForegroundColor Yellow
    
    # Get all roles
    $roles = Get-VIRole
    
    foreach ($role in $roles) {
        Write-Host "Processing role: $($role.Name)" -ForegroundColor Cyan
        
        # Get privileges for this role
        $privileges = $role.PrivilegeList
        
        $roleInfo = [PSCustomObject]@{
            RoleName = $role.Name
            RoleId = $role.Id
            IsSystem = $role.IsSystem
            PrivilegeCount = $privileges.Count
            Privileges = ($privileges -join "; ")
        }
        
        $rolesOutput += $roleInfo
        
        # Also create detailed privilege breakdown
        foreach ($privilege in $privileges) {
            $privDetail = [PSCustomObject]@{
                RoleName = $role.Name
                RoleId = $role.Id
                Privilege = $privilege
                IsSystemRole = $role.IsSystem
            }
            $permissionsOutput += $privDetail
        }
    }
    
    Write-Host "`nRetrieving local groups and members..." -ForegroundColor Yellow
    
    # Get Service Instance to access User Directory
    $si = Get-View ServiceInstance
    $userDirectory = Get-View $si.Content.UserDirectory
    
    # Search for all groups
    try {
        $searchSpec = New-Object VMware.Vim.UserSearchSpec
        $searchSpec.SearchStr = ""
        $searchSpec.ExactMatch = $false
        $searchSpec.FindUsers = $false
        $searchSpec.FindGroups = $true
        
        $groups = $userDirectory.RetrieveUserGroups($null, $searchSpec)
        
        foreach ($group in $groups) {
            Write-Host "Processing group: $($group.Principal)" -ForegroundColor Cyan
            
            # Get group members
            try {
                $members = $userDirectory.RetrieveUserGroupsMembers(@($group.Principal))
                $memberNames = $members | ForEach-Object { $_.Principal }
            }
            catch {
                $memberNames = @("Unable to retrieve members: $($_.Exception.Message)")
            }
            
            $groupInfo = [PSCustomObject]@{
                GroupName = $group.Principal
                FullName = $group.FullName
                Domain = if ($group.Principal -like "*\*") { ($group.Principal -split "\\")[0] } else { "Local" }
                MemberCount = if ($memberNames -and $memberNames[0] -notlike "Unable to retrieve*") { $memberNames.Count } else { 0 }
                Members = ($memberNames -join "; ")
            }
            
            $groupsOutput += $groupInfo
        }
    }
    catch {
        Write-Warning "Unable to retrieve groups: $($_.Exception.Message)"
        Write-Host "This might be due to insufficient permissions or domain connectivity issues." -ForegroundColor Yellow
    }
    
    # Export results to files
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    
    # Ensure output directory exists
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        Write-Host "Created output directory: $OutputPath" -ForegroundColor Green
    }
    
    # Export roles summary
    $rolesFile = Join-Path $OutputPath "vCenter_Roles_$timestamp.csv"
    $rolesOutput | Export-Csv -Path $rolesFile -NoTypeInformation
    Write-Host "`nRoles exported to: $rolesFile" -ForegroundColor Green
    
    # Export detailed privileges
    $privFile = Join-Path $OutputPath "vCenter_RolePrivileges_$timestamp.csv"
    $permissionsOutput | Export-Csv -Path $privFile -NoTypeInformation
    Write-Host "Role privileges exported to: $privFile" -ForegroundColor Green
    
    # Export groups
    if ($groupsOutput.Count -gt 0) {
        $groupsFile = Join-Path $OutputPath "vCenter_Groups_$timestamp.csv"
        $groupsOutput | Export-Csv -Path $groupsFile -NoTypeInformation
        Write-Host "Groups exported to: $groupsFile" -ForegroundColor Green
    }
    
    # Display summary
    Write-Host "`n=== SUMMARY ===" -ForegroundColor Magenta
    Write-Host "PowerCLI Module Used: $($moduleStatus.ModuleName)" -ForegroundColor White
    Write-Host "vCenter Server: $vCenterServer" -ForegroundColor White
    Write-Host "Total Roles: $($rolesOutput.Count)" -ForegroundColor White
    Write-Host "Total Groups: $($groupsOutput.Count)" -ForegroundColor White
    Write-Host "System Roles: $(($rolesOutput | Where-Object {$_.IsSystem -eq $true}).Count)" -ForegroundColor White
    Write-Host "Custom Roles: $(($rolesOutput | Where-Object {$_.IsSystem -eq $false}).Count)" -ForegroundColor White
    
    # Display roles in console
    Write-Host "`n=== ROLES SUMMARY ===" -ForegroundColor Magenta
    $rolesOutput | Format-Table -Property RoleName, IsSystem, PrivilegeCount -AutoSize
    
    # Display groups in console (first 10 if many)
    if ($groupsOutput.Count -gt 0) {
        Write-Host "`n=== GROUPS SUMMARY ===" -ForegroundColor Magenta
        if ($groupsOutput.Count -gt 10) {
            $groupsOutput | Select-Object -First 10 | Format-Table -Property GroupName, Domain, MemberCount -AutoSize
            Write-Host "... and $($groupsOutput.Count - 10) more groups (see CSV file for complete list)" -ForegroundColor Yellow
        } else {
            $groupsOutput | Format-Table -Property GroupName, Domain, MemberCount -AutoSize
        }
    }
}
catch {
    Write-Error "An error occurred: $($_.Exception.Message)"
    Write-Host "Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor Red
}
finally {
    # Disconnect from vCenter
    if ($connection) {
        Write-Host "`nDisconnecting from vCenter Server..." -ForegroundColor Yellow
        try {
            Disconnect-VIServer -Server $vCenterServer -Confirm:$false -ErrorAction SilentlyContinue
            Write-Host "Disconnected successfully" -ForegroundColor Green
        }
        catch {
            Write-Warning "Error during disconnect: $($_.Exception.Message)"
        }
    }
}

Write-Host "`nScript completed!" -ForegroundColor Green