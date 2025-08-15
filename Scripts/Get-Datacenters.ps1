<#
.SYNOPSIS
    Retrieves datacenter names from vCenter for folder structure migration.

.DESCRIPTION
    This script connects to a vCenter server and retrieves the names of all datacenters
    for use in folder structure migration operations.

.PARAMETER VCenterServer
    The hostname or IP address of the vCenter Server.

.PARAMETER Username
    Username for vCenter authentication.

.PARAMETER Password
    Password for vCenter authentication.

.PARAMETER BypassModuleCheck
    Switch to bypass PowerCLI module verification for faster execution.

.EXAMPLE
    .\Get-Datacenters.ps1 -VCenterServer "vcenter.lab.local" -Username "admin" -Password "password"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password,
    
    [Parameter()]
    [switch]$BypassModuleCheck
)

# Import PowerCLI modules if not bypassed
if (-not $BypassModuleCheck) {
    try {
        Import-Module VMware.VimAutomation.Core -ErrorAction Stop
        Write-Host "PowerCLI modules imported successfully"
    }
    catch {
        Write-Error "Failed to import PowerCLI modules: $_"
        exit 1
    }
}

try {
    # Create credential object
    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
    
    # Connect to vCenter
    Write-Host "Connecting to vCenter: $VCenterServer"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
    
    # Get all datacenters
    Write-Host "Retrieving datacenter information..."
    $datacenters = Get-Datacenter | Select-Object -ExpandProperty Name
    
    if ($datacenters) {
        # Output as comma-separated list
        $datacenterList = $datacenters -join ", "
        Write-Output $datacenterList
        Write-Host "Found datacenters: $datacenterList"
    } else {
        Write-Output "No datacenters found"
        Write-Host "No datacenters found in vCenter"
    }
    
}
catch {
    Write-Error "Error retrieving datacenters: $_"
    Write-Output "Error: Could not retrieve datacenters"
}
finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Disconnect-VIServer -Server $viConnection -Confirm:$false -Force
        Write-Host "Disconnected from vCenter"
    }
}