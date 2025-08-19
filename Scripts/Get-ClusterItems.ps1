<#
.SYNOPSIS
    Retrieves key items (Resource Pools, VDS) from a vCenter cluster.
.DESCRIPTION
    Connects to vCenter, finds a specific cluster, and lists its Resource Pools and
    Virtual Distributed Switches. Returns data in JSON format.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
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
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-ClusterItems" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$viConnection = $null
$jsonOutput = "[]"
$itemCount = 0

try {
    # Import PowerCLI
    Write-LogInfo "Importing PowerCLI modules..." -Category "Initialization"
    Import-Module VMware.VimAutomation.Core -ErrorAction Stop
    Write-LogSuccess "PowerCLI modules imported successfully." -Category "Initialization"

    # Connect to vCenter
    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"

    # Get Cluster
    Write-LogInfo "Retrieving cluster '$ClusterName'..." -Category "Discovery"
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
    Write-LogSuccess "Found cluster '$($cluster.Name)'." -Category "Discovery"

    # Get items
    Write-LogInfo "Retrieving items from cluster..." -Category "Discovery"
    $items = @()
    
    $resourcePools = $cluster | Get-ResourcePool
    Write-LogInfo "Found $($resourcePools.Count) resource pools." -Category "Discovery"
    $items += $resourcePools | Select-Object @{N='Name';E={$_.Name}}, @{N='Type';E={"ResourcePool"}}
    
    $vds = $cluster | Get-VMHost | Get-VDSwitch | Select-Object -Unique
    Write-LogInfo "Found $($vds.Count) Virtual Distributed Switches." -Category "Discovery"
    $items += $vds | Select-Object @{N='Name';E={$_.Name}}, @{N='Type';E={"VDS"}}
    
    $itemCount = $items.Count
    $jsonOutput = $items | ConvertTo-Json

    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved $itemCount items from cluster '$ClusterName'."

}
catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    throw $_
}
finally {
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -Force
    }
    
    $finalStats = @{
        "VCenterServer" = $VCenterServer
        "ClusterName" = $ClusterName
        "ItemsFound" = $itemCount
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}

# Final output
Write-Output $jsonOutput