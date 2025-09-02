<#
.SYNOPSIS
    Moves an ESXi host from one vCenter to another.
.DESCRIPTION
    This script safely disconnects and removes an ESXi host from a source vCenter
    and adds it to a target cluster in a destination vCenter.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenter,
    [Parameter(Mandatory=$true)]
    [string]$SourceUser,
    [Parameter(Mandatory=$true)]
    [string]$SourcePassword,
    [Parameter(Mandatory=$true)]
    [string]$TargetVCenter,
    [Parameter(Mandatory=$true)]
    [string]$TargetUser,
    [Parameter(Mandatory=$true)]
    [string]$TargetPassword,
    [Parameter(Mandatory=$true)]
    [string]$HostName,
    [Parameter(Mandatory=$true)]
    [string]$TargetClusterName,
    [Parameter(Mandatory=$true)]
    [string]$HostRootPassword,
    [Parameter(Mandatory=$false)]
    [string]$LogPath,
    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Move-EsxiHost" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$sourceVIServer = $null
$targetVIServer = $null

try {
    # Prepare credentials
    $sourceSecurePass = ConvertTo-SecureString -String $SourcePassword -AsPlainText -Force
    $sourceCred = New-Object System.Management.Automation.PSCredential($SourceUser, $sourceSecurePass)
    $targetSecurePass = ConvertTo-SecureString -String $TargetPassword -AsPlainText -Force
    $targetCred = New-Object System.Management.Automation.PSCredential($TargetUser, $targetSecurePass)

    # --- Source vCenter Operations ---
    Write-LogInfo "Connecting to source vCenter: $SourceVCenter" -Category "SourceVC"
    $sourceVIServer = Connect-VIServer -Server $SourceVCenter -Credential $sourceCred -ErrorAction Stop
    Write-LogSuccess "Connected to source vCenter: $SourceVCenter" -Category "SourceVC"

    Write-LogInfo "Retrieving host '$HostName' from source vCenter." -Category "SourceVC"
    $vmHost = Get-VMHost -Name $HostName -Server $sourceVIServer -ErrorAction Stop
    
    Write-LogInfo "Placing host '$HostName' into maintenance mode." -Category "SourceVC"
    Set-VMHost -VMHost $vmHost -State Maintenance -Evacuate -Confirm:$false -ErrorAction Stop
    Write-LogSuccess "Host is now in maintenance mode." -Category "SourceVC"

    Write-LogInfo "Disconnecting host '$HostName' from vCenter inventory." -Category "SourceVC"
    Set-VMHost -VMHost $vmHost -State Disconnected -Confirm:$false -ErrorAction Stop
    Write-LogSuccess "Host disconnected successfully." -Category "SourceVC"

    Write-LogInfo "Removing host '$HostName' from vCenter inventory." -Category "SourceVC"
    Remove-VMHost -VMHost $vmHost -Confirm:$false -ErrorAction Stop
    Write-LogSuccess "Host removed successfully." -Category "SourceVC"

    # --- Target vCenter Operations ---
    Write-LogInfo "Connecting to target vCenter: $TargetVCenter" -Category "TargetVC"
    $targetVIServer = Connect-VIServer -Server $TargetVCenter -Credential $targetCred -ErrorAction Stop
    Write-LogSuccess "Connected to target vCenter: $TargetVCenter" -Category "TargetVC"
    
    Write-LogInfo "Retrieving target cluster '$TargetClusterName'." -Category "TargetVC"
    $targetCluster = Get-Cluster -Name $TargetClusterName -Server $targetVIServer -ErrorAction Stop

    Write-LogInfo "Adding host '$HostName' to cluster '$($targetCluster.Name)' in the new vCenter." -Category "TargetVC"
    Add-VMHost -Name $HostName -Location $targetCluster -User 'root' -Password $HostRootPassword -Force -Confirm:$false -ErrorAction Stop
    Write-LogSuccess "Successfully added host '$HostName' to the new vCenter." -Category "TargetVC"

    $scriptSuccess = $true
    $finalSummary = "Migration for host '$HostName' completed successfully."

}
catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    throw $_
}
finally {
    if ($sourceVIServer) {
        Write-LogInfo "Disconnecting from source vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    if ($targetVIServer) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    $finalStats = @{
        "HostMoved" = $HostName
        "SourceVCenter" = $SourceVCenter
        "TargetVCenter" = $TargetVCenter
        "TargetCluster" = $TargetClusterName
    }
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}