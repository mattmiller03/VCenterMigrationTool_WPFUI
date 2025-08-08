# In Scripts/Get-NetworkTopology.ps1
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)
# Connect-VIServer...

$topology = Get-VMHost | ForEach-Object {
    $host = $_
    [PSCustomObject]@{
        Name = $host.Name
        VSwitches = $host | Get-VirtualSwitch | ForEach-Object {
            [PSCustomObject]@{
                Name = $_.Name
                Type = if ($_.GetType().Name -like "*Distributed*") { "Distributed" } else { "Standard" }
                PortGroups = $_ | Get-VirtualPortGroup | ForEach-Object {
                    [PSCustomObject]@{
                        Name = $_.Name
                        VlanId = $_.VlanId
                    }
                }
            }
        }
        VmKernelPorts = $host | Get-VMHostNetworkAdapter | Where-Object { $_.VMKernelGateway } | ForEach-Object {
            [PSCustomObject]@{
                Name = $_.DeviceName
                IpAddress = $_.IP
                VSwitchName = $_.VirtualSwitch.Name
            }
        }
    }
}

$topology | ConvertTo-Json -Depth 5

# Disconnect-VIServer...