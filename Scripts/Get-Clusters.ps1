# In Scripts/Get-Clusters.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)
# Connect-VIServer...
# Get all clusters and select just their names
Get-Cluster | Select-Object Name | ConvertTo-Json
# Disconnect-VIServer...