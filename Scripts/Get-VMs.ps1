# In Scripts/Get-Vms.ps1

param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,

    [Parameter(Mandatory=$true)]
    [string]$User,

    [Parameter(Mandatory=$true)]
    [string]$Password
)

# Your connection logic would go here
# Connect-VIServer -Server $VCenterServer -User $User -Password $Password -Force | Out-Null
Write-Information "Getting list of VMs from $VCenterServer..."

# Get VMs, select the properties we want, and convert the collection to JSON
# This will be the script's final output, which C# will capture.
Get-VM | Select-Object @{N="Name";E={$_.Name}}, @{N="PowerState";E={$_.PowerState}}, @{N="EsxiHost";E={$_.VMHost.Name}} | ConvertTo-Json

# Your disconnection logic would go here
# Disconnect-VIServer -Server $VCenterServer -Confirm:$false