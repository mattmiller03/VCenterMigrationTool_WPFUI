# In Scripts/Get-TargetResources.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)
# Connect-VIServer...
$result = @{
    Hosts = Get-VMHost | Select-Object Name
    Datastores = Get-Datastore | Select-Object Name
}
$result | ConvertTo-Json -Depth 3
# Disconnect-VIServer...