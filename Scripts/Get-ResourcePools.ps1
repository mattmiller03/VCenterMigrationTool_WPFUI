<#
.SYNOPSIS
    Retrieves resource pool information from a vCenter cluster for GUI display.

.DESCRIPTION
    This script connects to a vCenter server and retrieves information about resource pools
    in a specified cluster. Returns data in JSON format for consumption by the
    vCenter Migration Tool GUI.

.PARAMETER VCenterServer
    The hostname or IP address of the vCenter Server.

.PARAMETER Username
    Username for vCenter authentication.

.PARAMETER Password
    Password for vCenter authentication.

.PARAMETER ClusterName
    Name of the cluster to query for resource pools.

.PARAMETER BypassModuleCheck
    Switch to bypass PowerCLI module verification for faster execution.

.PARAMETER LogPath
    Optional path for logging output.

.EXAMPLE
    .\Get-ResourcePools.ps1 -VCenterServer "vcenter.lab.local" -Username "admin" -Password "password" -ClusterName "Cluster-A"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password,
    
    [Parameter(Mandatory=$true)]
    [string]$ClusterName,
    
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
    
    # Get the specified cluster
    Write-Host "Finding cluster: $ClusterName"
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
    
    # Get resource pools from the cluster (excluding built-in ones)
    Write-Host "Retrieving resource pools from cluster..."
    $resourcePools = Get-ResourcePool -Location $cluster -ErrorAction Stop | 
        Where-Object { $_.Name -notin @('Resources', 'vCLS') }
    
    Write-Host "Found $($resourcePools.Count) custom resource pools"
    
    # Build detailed resource pool information
    $poolInfo = @()
    foreach ($pool in $resourcePools) {
        try {
            # Get VMs in this resource pool
            $vmsInPool = Get-VM -Location $pool -ErrorAction SilentlyContinue
            $vmNames = if ($vmsInPool) { $vmsInPool.Name } else { @() }
            
            # Get permissions (if any)
            $permissions = Get-VIPermission -Entity $pool -ErrorAction SilentlyContinue
            
            $poolData = [PSCustomObject]@{
                Name = $pool.Name
                ParentType = $pool.Parent.GetType().Name
                ParentName = $pool.Parent.Name
                CpuSharesLevel = $pool.CpuSharesLevel.ToString()
                CpuShares = $pool.CpuShares
                CpuReservationMHz = $pool.CpuReservationMHz
                MemSharesLevel = $pool.MemSharesLevel.ToString()
                MemShares = $pool.MemShares
                MemReservationMB = $pool.MemReservationMB
                VMs = $vmNames
                VmCount = $vmNames.Count
                IsSelected = $false  # Default to not selected for GUI
                Permissions = if ($permissions) { 
                    $permissions | Select-Object Principal, Role, Propagate 
                } else { 
                    @() 
                }
            }
            
            $poolInfo += $poolData
            Write-Host "Processed resource pool: $($pool.Name) ($($vmNames.Count) VMs)"
        }
        catch {
            Write-Warning "Failed to process resource pool '$($pool.Name)': $_"
        }
    }
    
    # Convert to JSON and output
    $jsonOutput = $poolInfo | ConvertTo-Json -Depth 4
    Write-Output $jsonOutput
    
}
catch {
    Write-Error "Error retrieving resource pools: $_"
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