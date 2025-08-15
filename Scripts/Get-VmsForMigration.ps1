<#
.SYNOPSIS
    Retrieves VM information from vCenter for migration planning.

.DESCRIPTION
    This script connects to a vCenter server and retrieves detailed information about virtual machines
    that can be used for migration planning. Returns data in JSON format for consumption by the
    vCenter Migration Tool.

.PARAMETER VCenterServer
    The hostname or IP address of the vCenter Server.

.PARAMETER Username
    Username for vCenter authentication.

.PARAMETER Password
    Password for vCenter authentication.

.PARAMETER BypassModuleCheck
    Switch to bypass PowerCLI module verification for faster execution.

.PARAMETER LogPath
    Optional path for logging output.

.EXAMPLE
    .\Get-VmsForMigration.ps1 -VCenterServer "vcenter.lab.local" -Username "admin" -Password "password"
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
    [switch]$BypassModuleCheck,
    
    [Parameter()]
    [string]$LogPath
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
    
    # Get all VMs with relevant information
    Write-Host "Retrieving VM information..."
    $vms = Get-VM | Where-Object { 
        # Exclude templates and system VMs
        $_.ExtensionData.Config.Template -eq $false -and
        $_.Name -notlike "vCLS*" 
    } | Select-Object @{
        Name = "Name"
        Expression = { $_.Name }
    }, @{
        Name = "PowerState"
        Expression = { $_.PowerState.ToString() }
    }, @{
        Name = "EsxiHost"
        Expression = { $_.VMHost.Name }
    }, @{
        Name = "Datastore"
        Expression = { ($_.DatastoreIdList | ForEach-Object { (Get-Datastore -Id $_).Name }) -join ", " }
    }, @{
        Name = "Cluster"
        Expression = { 
            if ($_.VMHost) {
                $cluster = Get-Cluster -VMHost $_.VMHost -ErrorAction SilentlyContinue
                if ($cluster) { $cluster.Name } else { "Standalone" }
            } else { "Unknown" }
        }
    }, @{
        Name = "IsSelected"
        Expression = { $false }  # Default to not selected
    }
    
    Write-Host "Found $($vms.Count) VMs"
    
    # Convert to JSON and output
    $jsonOutput = $vms | ConvertTo-Json -Depth 3
    Write-Output $jsonOutput
    
}
catch {
    Write-Error "Error retrieving VMs: $_"
    # Return empty array as JSON for error handling
    Write-Output "[]"
}
finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Disconnect-VIServer -Server $viConnection -Confirm:$false -Force
        Write-Host "Disconnected from vCenter"
    }
}