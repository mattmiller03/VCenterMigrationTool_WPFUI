# In Scripts/Get-EsxiHosts.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)

# Connect-VIServer...

# Get all clusters and for each cluster, get its hosts
$topology = Get-Cluster | ForEach-Object {
    $cluster = $_
    [PSCustomObject]@{
        Name  = $cluster.Name
        Hosts = $cluster | Get-VMHost | ForEach-Object {
            [PSCustomObject]@{
                Name = $_.Name
            }
        }
    }
}

$topology | ConvertTo-Json -Depth 3

# Disconnect-VIServer...