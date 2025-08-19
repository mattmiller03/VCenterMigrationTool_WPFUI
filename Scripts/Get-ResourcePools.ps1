<#
.SYNOPSIS
    Retrieves resource pool information from a vCenter cluster for GUI display.
.DESCRIPTION
    Connects to vCenter and retrieves detailed info about resource pools in a cluster.
    Returns data in JSON format.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
[CmdletBinding()]
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
    [switch]$BypassModuleCheck,
    
    [Parameter()]
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-ResourcePools" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$viConnection = $null
$jsonOutput = "[]"
$poolCount = 0

try {
    # Import PowerCLI
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Initialization"
        Import-Module VMware.VimAutomation.Core -ErrorAction Stop
        Write-LogSuccess "PowerCLI modules imported successfully." -Category "Initialization"
    } else {
        Write-LogInfo "Bypassing PowerCLI module check." -Category "Initialization"
    }

    # Connect to vCenter
    $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
    $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"
    
    # Get the specified cluster
    Write-LogInfo "Finding cluster: $ClusterName" -Category "Discovery"
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
    Write-LogSuccess "Found cluster '$($cluster.Name)'" -Category "Discovery"
    
    # Get resource pools from the cluster
    Write-LogInfo "Retrieving resource pools from cluster..." -Category "Discovery"
    $resourcePools = Get-ResourcePool -Location $cluster -ErrorAction Stop | 
        Where-Object { $_.Name -notin @('Resources', 'vCLS') }
    
    $poolCount = $resourcePools.Count
    Write-LogSuccess "Found $poolCount custom resource pools." -Category "Discovery"
    
    # Build detailed resource pool information
    $poolInfo = @()
    foreach ($pool in $resourcePools) {
        try {
            Write-LogDebug "Processing resource pool: $($pool.Name)" -Category "Processing"
            $vmsInPool = Get-VM -Location $pool -ErrorAction SilentlyContinue
            $vmNames = if ($vmsInPool) { $vmsInPool.Name } else { @() }
            
            $poolData = [PSCustomObject]@{
                Name = $pool.Name; ParentType = $pool.Parent.GetType().Name; ParentName = $pool.Parent.Name
                CpuSharesLevel = $pool.CpuSharesLevel.ToString(); CpuShares = $pool.CpuShares
                CpuReservationMHz = $pool.CpuReservationMHz; MemSharesLevel = $pool.MemSharesLevel.ToString()
                MemShares = $pool.MemShares; MemReservationMB = $pool.MemReservationMB
                VMs = $vmNames; VmCount = $vmNames.Count; IsSelected = $false
            }
            $poolInfo += $poolData
        }
        catch {
            Write-LogWarning "Failed to process details for resource pool '$($pool.Name)': $($_.Exception.Message)" -Category "Processing"
        }
    }
    
    $jsonOutput = $poolInfo | ConvertTo-Json -Depth 4
    
    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved $poolCount resource pools from cluster '$ClusterName'."

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
        "ResourcePoolsFound" = $poolCount
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}

# Final output
Write-Output $jsonOutput