# In Scripts/Get-ClusterItems.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password,
    [string]$ClusterName
)
# Connect-VIServer...
$cluster = Get-Cluster -Name $ClusterName
$items = @()
# Get Resource Pools
$items += $cluster | Get-ResourcePool | Select-Object @{N='Name';E={$_.Name}}, @{N='Type';E={"ResourcePool"}}
# Get vDS
$items += $cluster | Get-VMHost | Get-VDSwitch | Select-Object -Unique | Select-Object @{N='Name';E={$_.Name}}, @{N='Type';E={"VDS"}}
$items | ConvertTo-Json
# Disconnect-VIServer...