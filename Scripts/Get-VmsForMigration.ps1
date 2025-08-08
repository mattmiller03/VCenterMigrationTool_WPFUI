# In Scripts/Get-VmsForMigration.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)
# Connect-VIServer...
Get-VM | Select-Object Name, PowerState, @{N='EsxiHost';E={$_.VMHost.Name}}, @{N='Datastore';E={($_ | Get-Datastore).Name}}, @{N='Cluster';E={$_.VMHost.Parent.Name}} | ConvertTo-Json -Depth 3
# Disconnect-VIServer...