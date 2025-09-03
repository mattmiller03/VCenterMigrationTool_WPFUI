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
    
    [Parameter()]
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false,
    
    [Parameter()]
    [bool]$BypassModuleCheck = $false
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
    # PowerCLI modules are managed by the service layer
    Write-LogInfo "PowerCLI modules are managed by the service layer" -Category "Initialization"

    # Use existing vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
    $viConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
    if (-not $viConnection -or -not $viConnection.IsConnected) {
        throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using vCenter connection: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"

    # Get all clusters and for each cluster, get its hosts
    Write-LogInfo "Retrieving cluster and host topology..." -Category "Discovery"
    $topology = Get-Cluster -Server $viConnection | ForEach-Object {
        $cluster = $_
        $clusterCount++
        $hostsInCluster = $cluster | Get-VMHost -Server $viConnection
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
        Write-LogInfo "Preserving vCenter connection for persistent session" -Category "Cleanup"
        # DO NOT DISCONNECT - Using persistent connections managed by the application
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