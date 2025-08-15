<#
.SYNOPSIS
    Copies privileges from one vCenter role to another with version compatibility handling.

.DESCRIPTION
    This script retrieves all privileges from a source vCenter role and adds them to a target vCenter role.
    It includes special handling for privilege differences between vCenter versions (such as v7 to v8),
    skipping privileges that don't exist in the current vCenter version.
    Requires PowerCLI v13.x or later to be installed.

.PARAMETER vCenterServer
    The hostname or IP address of the vCenter server to connect to.

.PARAMETER SourceRoleName
    The name of the role to copy privileges from.

.PARAMETER TargetRoleName
    The name of the role to add privileges to.

.PARAMETER Credential
    Optional PSCredential object for authentication to vCenter.
    If not provided, the script will use the current user's credentials or prompt for credentials.

.PARAMETER ReplaceExisting
    If specified, completely replaces the target role's privileges with the source role's privileges.
    By default, the script adds privileges without removing existing ones.

.EXAMPLE
    .\Copy-RolePrivileges.ps1 -vCenterServer "vcenter.domain.com" -SourceRoleName "Admin" -TargetRoleName "CustomAdmin"
    
    Adds all privileges from the "Admin" role to the "CustomAdmin" role.

.EXAMPLE
    .\Copy-RolePrivileges.ps1 -vCenterServer "vcenter.domain.com" -SourceRoleName "ReadOnly" -TargetRoleName "CustomRole" -ReplaceExisting
    
    Replaces all privileges in the "CustomRole" with those from the "ReadOnly" role.

.EXAMPLE
    $cred = Get-Credential
    .\Copy-RolePrivileges.ps1 -vCenterServer "vcenter.domain.com" -SourceRoleName "Admin" -TargetRoleName "CustomAdmin" -Credential $cred
    
    Connects to vCenter using the specified credentials and adds privileges from the "Admin" role to the "CustomAdmin" role.

.NOTES
    Author: Script Generator
    Requirements: PowerCLI v13.x or later
    Version: 1.2
    Date: [Current Date]
    
    Change Log:
    1.2 - Fixed handling of invalid privileges by processing each privilege individually
    1.1 - Added compatibility handling for privilege differences between vCenter versions (v7 to v8)
    1.0 - Initial version
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0, HelpMessage = "vCenter server hostname or IP address")]
    [string]$vCenterServer,
    
    [Parameter(Mandatory = $true, Position = 1, HelpMessage = "Source role name to copy privileges from")]
    [string]$SourceRoleName,
    
    [Parameter(Mandatory = $true, Position = 2, HelpMessage = "Target role name to add privileges to")]
    [string]$TargetRoleName,
    
    [Parameter(Mandatory = $false, HelpMessage = "Credentials for vCenter authentication")]
    [System.Management.Automation.PSCredential]$Credential,
    
    [Parameter(Mandatory = $false, HelpMessage = "Replace existing privileges instead of adding to them")]
    [switch]$ReplaceExisting
)

# Import PowerCLI module (if not already loaded)
if (-not (Get-Module -Name VMware.VimAutomation.Core)) {
    try {
        Import-Module VMware.VimAutomation.Core -ErrorAction Stop
    } catch {
        Write-Error "Failed to import PowerCLI module. Please ensure PowerCLI v13.x or later is installed."
        exit 1
    }
}

# Connect to vCenter
try {
    $connectParams = @{
        Server = $vCenterServer
        ErrorAction = 'Stop'
    }
    
    if ($Credential) {
        $connectParams.Add('Credential', $Credential)
    }
    
    $connection = Connect-VIServer @connectParams
    write-verbose "Connected to vCenter server: $($connection.Name)"
    
    # Get vCenter version
    $vCenterVersion = $connection.Version
    write-verbose "vCenter version: $($vCenterVersion)" 
} catch {
    Write-Error "Failed to connect to vCenter server: $($_)"
    exit 1
}

# Get the source and target roles
try {
    $sourceRole = Get-VIRole -Name $SourceRoleName -ErrorAction Stop
    $targetRole = Get-VIRole -Name $TargetRoleName -ErrorAction Stop
    
    write-verbose "Source role: $($sourceRole.Name)"
    write-verbose "Target role: $($targetRole.Name)"
} catch {
    Write-Error "Failed to retrieve roles: $($_)"
    Disconnect-VIServer -Server $vCenterServer -Confirm:$false
    exit 1
}

# Get all available privileges in the current vCenter for validation
try {
    write-verbose "Retrieving all available privileges in this vCenter instance..." 
    $allAvailablePrivileges = Get-VIPrivilege | Select-Object -ExpandProperty Id
    write-verbose "Found $($allAvailablePrivileges.Count) available privileges." 
} catch {
    Write-Error "Failed to retrieve available privileges: $($_)"
    Disconnect-VIServer -Server $vCenterServer -Confirm:$false
    exit 1
}

# Get privileges from the source role
$sourcePrivileges = $sourceRole.PrivilegeList

if ($sourcePrivileges.Count -eq 0) {
    Write-Warning "Source role has no privileges."
    Disconnect-VIServer -Server $vCenterServer -Confirm:$false
    exit 0
}

write-verbose "Found $($sourcePrivileges.Count) privileges in source role."

# Filter out privileges that don't exist in the current vCenter version
$validSourcePrivileges = @()
$invalidPrivileges = @()

foreach ($privilege in $sourcePrivileges) {
    if ($privilege -in $allAvailablePrivileges) {
        $validSourcePrivileges += $privilege
    } else {
        $invalidPrivileges += $privilege
    }
}

if ($invalidPrivileges.Count -gt 0) {
    Write-Warning "Found $($invalidPrivileges.Count) privileges that don't exist in this vCenter version and will be skipped:"
    foreach ($invalidPriv in $invalidPrivileges) {
        Write-Warning "  - $invalidPriv"
    }
}

write-verbose "Proceeding with $($validSourcePrivileges.Count) valid privileges." 

# Handle privileges based on whether we're replacing or adding
if ($ReplaceExisting) {
    # Replace all privileges in the target role
    try {
        write-verbose "Replacing all privileges in target role with valid source role privileges..."
        Set-VIRole -Role $targetRole -Privilege $validSourcePrivileges -Confirm:$false
        write-verbose "Successfully replaced privileges in target role."
    } catch {
        Write-Error "Failed to update target role: $($_)"
    }
} else {
    # Add privileges from source to target (without duplicating)
    $targetPrivileges = $targetRole.PrivilegeList
    $privilegesToAdd = $validSourcePrivileges | Where-Object { $_ -notin $targetPrivileges }

    if ($privilegesToAdd.Count -eq 0) {
        write-verbose "All valid source privileges already exist in target role. No changes needed."
    } else {
        write-verbose "Adding $($privilegesToAdd.Count) privileges to target role..."
        
        $successCount = 0
        $failCount = 0
        
        # Process each privilege individually to avoid batch failures
        foreach ($privilege in $privilegesToAdd) {
            try {
                # Verify again that the privilege exists before trying to add it
                if ($privilege -in $allAvailablePrivileges) {
                    Set-VIRole -Role $targetRole -AddPrivilege $privilege -Confirm:$false -ErrorAction Stop
                    $successCount++
                } else {
                    Write-Warning "Skipping privilege '$($privilege)' as it doesn't exist in this vCenter."
                    $failCount++
                }
            } catch {
                Write-Warning "Failed to add privilege '$($privilege)': $($_)"
                $failCount++
            }
        }
        
        write-verbose "Successfully added $($successCount) privileges to target role."
        if ($failCount -gt 0) {
            Write-Warning "Failed to add $($failCount) privileges."
        }
    }
}

# Summary of what was done
write-verbose "`nOperation Summary:" 
write-verbose "--------------------" 
write-verbose "Source Role: $($SourceRoleName)"
write-verbose "Target Role: $($TargetRoleName)"
write-verbose "Total privileges in source role: $($sourcePrivileges.Count)"
write-verbose "Valid privileges processed: $($validSourcePrivileges.Count)"
write-verbose "Skipped privileges: $($invalidPrivileges.Count)"
if (-not $ReplaceExisting) {
    write-verbose "Privileges added to target role: $($successCount)"
    if ($failCount -gt 0) {
        write-verbose "Privileges failed to add: $($failCount)"
    }
}
write-verbose "--------------------" 

# Disconnect from vCenter
Disconnect-VIServer -Server $vCenterServer -Confirm:$false -ErrorAction SilentlyContinue
write-verbose "Disconnected from vCenter server."
