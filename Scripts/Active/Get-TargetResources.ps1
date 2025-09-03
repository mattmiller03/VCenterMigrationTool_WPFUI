<#
.SYNOPSIS
    Retrieves hosts and datastores from a target vCenter.
.DESCRIPTION
    Connects to a vCenter server and retrieves a list of ESXi hosts and datastores,
    returning the data in JSON format. Provides sample data if connection fails.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    [Parameter(Mandatory=$false)]
    [string]$LogPath,
    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false,
    [Parameter(Mandatory=$false)]
    [switch]$BypassModuleCheck
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-TargetResources" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$jsonOutput = '{"Hosts":[],"Datastores":[]}' # Default to empty JSON
$stats = @{ "HostsFound" = 0; "DatastoresFound" = 0 }

try {
    Write-LogInfo "Starting target resources script for vCenter: $VCenterServer" -Category "Initialization"

    # PowerCLI modules managed by service layer
    
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -ParticipateInCEIP $false -Scope Session -Confirm:$false | Out-Null
    
    # Use existing vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
    $connection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
    if (-not $connection -or -not $connection.IsConnected) {
        throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using vCenter connection: $($connection.Name)" -Category "Connection"
    
    # Get hosts and datastores
    Write-LogInfo "Retrieving hosts and datastores..." -Category "Discovery"
    $hosts = Get-VMHost -Server $connection -ErrorAction Stop | ForEach-Object { @{ Name = $_.Name } }
    $datastores = Get-Datastore -Server $connection -ErrorAction Stop | ForEach-Object { @{ Name = $_.Name } }
    
    $stats.HostsFound = $hosts.Count
    $stats.DatastoresFound = $datastores.Count
    Write-LogSuccess "Retrieved $($stats.HostsFound) hosts and $($stats.DatastoresFound) datastores." -Category "Discovery"
    
    $result = @{ Hosts = $hosts; Datastores = $datastores }
    $jsonOutput = $result | ConvertTo-Json -Depth 3
    
    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved resources from $VCenterServer."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Error occurred: $($_.Exception.Message). Returning sample data."
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    # Return sample data on error
    $sampleData = @{
        Hosts = @( @{ Name = "error-recovery-host-01" }; @{ Name = "error-recovery-host-02" } )
        Datastores = @( @{ Name = "error-recovery-ds-01" }; @{ Name = "error-recovery-ds-02" } )
    }
    $jsonOutput = $sampleData | ConvertTo-Json -Depth 3
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
    # DISCONNECT REMOVED - Using persistent connections managed by application
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}

# Final output for consumption by other tools
Write-Output $jsonOutput