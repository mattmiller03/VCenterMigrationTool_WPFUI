<#
.SYNOPSIS
    Retrieves VM information from vCenter for migration planning.
.DESCRIPTION
    This script connects to a vCenter server and retrieves detailed information about virtual machines
    that can be used for migration planning. Returns data in JSON format.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter()]
    [switch]$BypassModuleCheck,
    
    [Parameter()]
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-VmsForMigration" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$viConnection = $null
$jsonOutput = "[]" # Default to empty JSON array on failure
$vmCount = 0

try {
    # PowerCLI modules are managed by the service layer
    Write-LogInfo "PowerCLI modules are managed by the service layer" -Category "Initialization"

    # Use existing vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
    $viConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
    if (-not $viConnection -or -not $viConnection.IsConnected) {
        throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using vCenter connection: $($viConnection.Name)" -Category "Connection"
    
    # Get all VMs with relevant information
    Write-LogInfo "Retrieving VM information..." -Category "Discovery"
    $vms = Get-VM | Where-Object { 
        $_.ExtensionData.Config.Template -eq $false -and $_.Name -notlike "vCLS*" 
    } | Select-Object @{
        Name = "Name"; Expression = { $_.Name }
    }, @{
        Name = "PowerState"; Expression = { $_.PowerState.ToString() }
    }, @{
        Name = "EsxiHost"; Expression = { $_.VMHost.Name }
    }, @{
        Name = "Datastore"; Expression = { ($_.DatastoreIdList | ForEach-Object { (Get-Datastore -Id $_).Name }) -join ", " }
    }, @{
        Name = "Cluster"; Expression = { if ($_.VMHost) { (Get-Cluster -VMHost $_.VMHost -ErrorAction SilentlyContinue).Name } else { "Unknown" } }
    }, @{
        Name = "IsSelected"; Expression = { $false }
    }
    
    $vmCount = $vms.Count
    Write-LogSuccess "Found $($vmCount) VMs" -Category "Discovery"
    
    # Convert to JSON
    $jsonOutput = $vms | ConvertTo-Json -Depth 3
    
    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved $vmCount VMs from $VCenterServer."

}
catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    # Error is re-thrown for external tools to catch
    throw $_
}
finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    $finalStats = @{
        "VCenterServer" = $VCenterServer
        "VMsFound" = $vmCount
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}

# Final output for consumption by other tools
Write-Output $jsonOutput