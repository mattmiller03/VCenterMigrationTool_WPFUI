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
    [System.Management.Automation.PSCredential]$Credentials,
    
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
    # PowerCLI modules are managed by the service layer
    Write-LogInfo "PowerCLI modules are managed by the service layer" -Category "Initialization"

    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    # Force connection and ignore SSL certificate issues
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"
    
    # Get the specified cluster
    Write-LogInfo "Finding cluster: $ClusterName" -Category "Discovery"
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
    Write-LogSuccess "Found cluster '$($cluster.Name)'" -Category "Discovery"
    
    # Get resource pools from the cluster
    # OPTIMIZED: Use vSphere API for much faster resource pool data collection
    Write-LogInfo "Retrieving resource pools from cluster using vSphere API..." -Category "Discovery"
    
    # Get all resource pools in cluster using API (much faster)
    $resourcePools = Get-View -ViewType ResourcePool -SearchRoot $cluster.MoRef | 
        Where-Object { $_.Name -notin @('Resources', 'vCLS') }
    
    $poolCount = $resourcePools.Count
    Write-LogSuccess "Found $poolCount custom resource pools." -Category "Discovery"
    
    # Build detailed resource pool information using API
    $poolInfo = @()
    foreach ($pool in $resourcePools) {
        try {
            Write-LogDebug "Processing resource pool: $($pool.Name)" -Category "Processing"
            
            # Get VM names efficiently using API
            $vmNames = @()
            if ($pool.Vm -and $pool.Vm.Count -gt 0) {
                $vms = Get-View -Id $pool.Vm -ErrorAction SilentlyContinue
                $vmNames = $vms | ForEach-Object { $_.Name }
            }
            
            # Get parent info
            $parentName = ""
            $parentType = ""
            try {
                if ($pool.Parent) {
                    $parent = Get-View $pool.Parent -ErrorAction SilentlyContinue
                    if ($parent) {
                        $parentName = $parent.Name
                        $parentType = $parent.GetType().Name
                    }
                }
            } catch {
                # Continue without parent info
            }
            
            # Get resource allocation settings from API
            $cpuShares = 0
            $cpuReservation = 0
            $cpuSharesLevel = "Normal"
            $memShares = 0
            $memReservation = 0
            $memSharesLevel = "Normal"
            
            if ($pool.Config) {
                if ($pool.Config.CpuAllocation) {
                    $cpuShares = if ($pool.Config.CpuAllocation.Shares) { $pool.Config.CpuAllocation.Shares.Shares } else { 0 }
                    $cpuReservation = if ($pool.Config.CpuAllocation.Reservation) { $pool.Config.CpuAllocation.Reservation } else { 0 }
                    $cpuSharesLevel = if ($pool.Config.CpuAllocation.Shares) { $pool.Config.CpuAllocation.Shares.Level.ToString() } else { "Normal" }
                }
                if ($pool.Config.MemoryAllocation) {
                    $memShares = if ($pool.Config.MemoryAllocation.Shares) { $pool.Config.MemoryAllocation.Shares.Shares } else { 0 }
                    $memReservation = if ($pool.Config.MemoryAllocation.Reservation) { [int]($pool.Config.MemoryAllocation.Reservation / 1024 / 1024) } else { 0 }
                    $memSharesLevel = if ($pool.Config.MemoryAllocation.Shares) { $pool.Config.MemoryAllocation.Shares.Level.ToString() } else { "Normal" }
                }
            }
            
            $poolData = [PSCustomObject]@{
                Name = $pool.Name
                ParentType = $parentType
                ParentName = $parentName
                CpuSharesLevel = $cpuSharesLevel
                CpuShares = [int]$cpuShares
                CpuReservationMHz = [int]$cpuReservation
                MemSharesLevel = $memSharesLevel
                MemShares = [int]$memShares
                MemReservationMB = [int]$memReservation
                VMs = $vmNames
                VmCount = [int]$vmNames.Count
                IsSelected = $false
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
        # DISCONNECT REMOVED - Using persistent connections managed by application
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