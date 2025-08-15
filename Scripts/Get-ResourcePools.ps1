<#
.SYNOPSIS
    Retrieves resource pool information from a vCenter cluster for the GUI application.

.DESCRIPTION
    This script connects to a vCenter server and retrieves information about custom resource pools
    in a specified cluster. Returns data in JSON format for consumption by the vCenter Migration Tool.
    Excludes built-in pools like "Resources" and "vCLS".

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
    
    # Get custom resource pools (excluding built-in ones)
    Write-Host "Retrieving custom resource pools from cluster: $ClusterName"
    $resourcePools = Get-ResourcePool -Location $cluster -ErrorAction Stop | 
        Where-Object { $_.Name -notin @('Resources', 'vCLS') }
    
    if ($resourcePools) {
        # Convert to the format expected by the GUI
        $poolInfo = $resourcePools | ForEach-Object {
            $pool = $_
            
            # Get VMs in this pool
            $vmsInPool = @()
            try {
                $vmsInPool = (Get-VM -Location $pool -ErrorAction SilentlyContinue).Name
            }
            catch {
                Write-Host "Warning: Could not retrieve VMs for pool $($pool.Name): $_"
            }
            
            # Get permissions
            $permissions = @()
            try {
                $permissions = Get-VIPermission -Entity $pool -ErrorAction SilentlyContinue | 
                    Select-Object @{N='Principal'; E={$_.Principal}}, 
                                  @{N='Role'; E={$_.Role}}, 
                                  @{N='Propagate'; E={$_.Propagate}}
            }
            catch {
                Write-Host "Warning: Could not retrieve permissions for pool $($pool.Name): $_"
            }
            
            # Create the output object
            [PSCustomObject]@{
                Name = $pool.Name
                ParentType = $pool.Parent.GetType().Name
                ParentName = $pool.Parent.Name
                CpuSharesLevel = $pool.CpuSharesLevel.ToString()
                CpuShares = $pool.CpuShares
                CpuReservationMHz = $pool.CpuReservationMHz
                MemSharesLevel = $pool.MemSharesLevel.ToString()
                MemShares = $pool.MemShares
                MemReservationMB = $pool.MemReservationMB
                VMs = @($vmsInPool)
                Permissions = @($permissions)
            }
        }
        
        Write-Host "Found $($poolInfo.Count) custom resource pools"
        
        # Convert to JSON and output
        $jsonOutput = $poolInfo | ConvertTo-Json -Depth 4
        Write-Output $jsonOutput
    }
    else {
        Write-Host "No custom resource pools found in cluster $ClusterName"
        # Return empty array as JSON
        Write-Output "[]"
    }
    
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