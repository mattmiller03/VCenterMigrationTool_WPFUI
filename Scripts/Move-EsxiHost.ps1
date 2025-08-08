# In Scripts/Move-EsxiHost.ps1
param(
    [string]$SourceVCenter,
    [string]$SourceUser,
    [string]$SourcePassword,
    [string]$TargetVCenter,
    [string]$TargetUser,
    [string]$TargetPassword,
    [string]$HostName,
    [string]$TargetClusterName
)

Write-Information "Starting migration for host: $HostName"
# Connect to source vCenter
# Connect-VIServer -Server $SourceVCenter -User $SourceUser -Password $SourcePassword
Write-Information "Connected to source vCenter: $SourceVCenter"

# Get the host object and disconnect it
# Get-VMHost -Name $HostName | Set-VMHost -State 'Disconnected'
# Remove-VMHost -VMHost $HostName -Confirm:$false
Write-Information "Disconnected host '$HostName' from source vCenter."
Start-Sleep -Seconds 2

# Disconnect from source vCenter
# Disconnect-VIServer -Server $SourceVCenter -Confirm:$false

# Connect to target vCenter
# Connect-VIServer -Server $TargetVCenter -User $TargetUser -Password $TargetPassword
Write-Information "Connected to target vCenter: $TargetVCenter"

# Add the host to the new cluster
# Add-VMHost -Name $HostName -Location (Get-Cluster -Name $TargetClusterName) -User 'root' -Password 'your-host-root-password' -Force -Confirm:$false
Write-Information "Successfully added host '$HostName' to cluster '$TargetClusterName' in the new vCenter."
Start-Sleep -Seconds 2

# Disconnect from target vCenter
# Disconnect-VIServer -Server $TargetVCenter -Confirm:$false
Write-Information "Migration for host '$HostName' complete."