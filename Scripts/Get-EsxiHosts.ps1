<#
.SYNOPSIS
    Retrieves a topology of clusters and their associated ESXi hosts.
.DESCRIPTION
    Connects to vCenter, retrieves all clusters, and lists the hosts within each cluster.
    Output is in JSON format.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
    [Parameter()]
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-EsxiHosts" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$viConnection = $null
$jsonOutput = "[]" # Default to empty JSON array
$clusterCount = 0
$hostCount = 0

try {
    # Import PowerCLI
    Write-LogInfo "Importing PowerCLI modules..." -Category "Initialization"
    Import-Module VMware.VimAutomation.Core -ErrorAction Stop
    Write-LogSuccess "PowerCLI modules imported successfully" -Category "Initialization"

    # Connect to vCenter using provided credentials
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"

    # Get all clusters and for each cluster, get its hosts
    Write-LogInfo "Retrieving cluster and host topology..." -Category "Discovery"
    $topology = Get-Cluster | ForEach-Object {
        $cluster = $_
        $clusterCount++
        $hostsInCluster = $cluster | Get-VMHost
        $hostCount += $hostsInCluster.Count
        Write-LogDebug "Processing cluster '$($cluster.Name)' with $($hostsInCluster.Count) hosts." -Category "Discovery"
        
        [PSCustomObject]@{
            Name  = $cluster.Name
            Hosts = $hostsInCluster | ForEach-Object {
                [PSCustomObject]@{
                    Name = $_.Name
                }
            }
        }
    }
    
    Write-LogSuccess "Found $hostCount hosts across $clusterCount clusters." -Category "Discovery"

    $jsonOutput = $topology | ConvertTo-Json -Depth 3
    
    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved topology for $clusterCount clusters and $hostCount hosts."

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
        "ClustersFound" = $clusterCount
        "HostsFound" = $hostCount
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}

# Final output
Write-Output $jsonOutput