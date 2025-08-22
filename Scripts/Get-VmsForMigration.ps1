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
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
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
    # Import PowerCLI modules if not bypassed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Initialization"
        Import-Module VMware.VimAutomation.Core -ErrorAction Stop
        Write-LogSuccess "PowerCLI modules imported successfully" -Category "Initialization"
    } else {
        Write-LogInfo "Bypassing PowerCLI module check." -Category "Initialization"
    }

    # Connect to vCenter using provided credentials
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"
    
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
        Disconnect-VIServer -Server $viConnection -Confirm:$false -Force
    }
    
    $finalStats = @{
        "VCenterServer" = $VCenterServer
        "VMsFound" = $vmCount
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}

# Final output for consumption by other tools
Write-Output $jsonOutput