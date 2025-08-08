# In Scripts/Get-EsxiHosts.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)

# Connect-VIServer...

# Get all ESXi hosts and their relevant properties
Get-VMHost | Select-Object @{N='Name';E={$_.Name}}, @{N='Cluster';E={$_.Parent.Name}}, @{N='Status';E={$_.ConnectionState}} | ConvertTo-Json

# Disconnect-VIServer...