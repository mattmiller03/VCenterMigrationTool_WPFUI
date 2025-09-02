<#
.SYNOPSIS
    Imports resource pools into a vSphere cluster and optionally moves VMs into them,
    generating a detailed HTML report and a structured log file.
.DESCRIPTION
    Reads a JSON file describing resource pools and replicates the structure on a target cluster.
    Optionally moves VMs into the newly created pools.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string] $DestVC,
    [Parameter(Mandatory=$true)] [PSCredential] $DestCred,
    [Parameter(Mandatory=$true)] [string] $InputJson,
    [Parameter(Mandatory=$true)] [string] $TargetCluster,
    [Parameter(Mandatory=$false)] [switch] $RemoveAllPools,
    [Parameter(Mandatory=$false)] [switch] $MoveVMs,
    [Parameter(Mandatory=$false)] [string] $LogPath,
    [Parameter(Mandatory=$false)] [bool] $SuppressConsoleOutput = $false,
    [Parameter(Mandatory=$false)] [string] $ReportPath = ".\ResourcePoolMigration_$(Get-Date -Format 'yyyyMMdd_HHmmss').html"
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "ResourcePool-import" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$stats = @{
    "PoolsCreated" = 0; "PoolsSkipped" = 0; "PoolsFailed" = 0
    "VMsMoved" = 0; "VMsSkipped" = 0; "VMsFailed" = 0
}
# Arrays to hold detailed failure reasons for the report
$failedPoolsReport = @(); $failedVMsReport = @(); $skippedVMsReport = @()
$createdPoolsReport = @(); $skippedPoolsReport = @(); $movedVMsReport = @()

# --- Functions ---
function Generate-MigrationReport { param(...) # HTML Report function remains unchanged }

try {
    Write-LogInfo "Script started. DestVC='$DestVC', TargetCluster='$TargetCluster'." -Category "Setup"
    if ($RemoveAllPools) { Write-LogInfo "Option: Remove existing pools enabled." -Category "Setup" }
    if ($MoveVMs) { Write-LogInfo "Option: Move VMs into pools enabled." -Category "Setup" }

    # PowerCLI and Connection
    Write-LogInfo "Importing PowerCLI and connecting to vCenter '$DestVC'..." -Category "Connection"
    Import-Module VMware.PowerCLI -ErrorAction Stop
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Connect-VIServer -Server $DestVC -Credential $DestCred -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter." -Category "Connection"
    
    $cluster = Get-Cluster -Name $TargetCluster -ErrorAction Stop
    Write-LogSuccess "Found target cluster '$TargetCluster'." -Category "Discovery"

    # Remove existing pools if requested
    if ($RemoveAllPools) {
        Write-LogInfo "Removing existing custom resource pools from '$TargetCluster'..." -Category "Cleanup"
        $toRemove = Get-ResourcePool -Location $cluster -ErrorAction Stop | Where-Object { $_.Name -notin 'Resources','vCLS' }
        if ($toRemove) {
            $toRemove | Remove-ResourcePool -Confirm:$false -ErrorAction Stop
            Write-LogSuccess "Removed $($toRemove.Count) existing resource pools." -Category "Cleanup"
        } else { Write-LogInfo "No existing resource pools to remove." -Category "Cleanup" }
    }

    # Parse JSON
    if (-not (Test-Path $InputJson)) { throw "Input JSON not found: '$InputJson'" }
    $configs = Get-Content $InputJson -Raw | ConvertFrom-Json
    Write-LogSuccess "Loaded $($configs.Count) pool definitions from JSON." -Category "DataLoad"

    # Resource Pool Creation
    Write-LogInfo "--- Starting Resource Pool Creation Phase ---" -Category "PoolCreation"
    $poolMap = @{}
    foreach ($c in $configs) {
        $poolName = $c.Name
        if (Get-ResourcePool -Name $poolName -Location $cluster -ErrorAction SilentlyContinue) {
            Write-LogInfo "Resource Pool '$poolName' already exists. Skipping creation." -Category "PoolCreation"
            $stats.PoolsSkipped++; $skippedPoolsReport += $poolName
            $poolMap[$poolName] = Get-ResourcePool -Name $poolName -Location $cluster
        } else {
            try {
                Write-LogDebug "Creating pool '$poolName'..." -Category "PoolCreation"
                $newPool = New-ResourcePool -Name $poolName -Location $cluster -ErrorAction Stop
                $stats.PoolsCreated++; $createdPoolsReport += $poolName
                $poolMap[$poolName] = $newPool
                Write-LogSuccess "Successfully created pool '$poolName'." -Category "PoolCreation"
            } catch {
                Write-LogError "FAILED to create pool '$poolName': $($_.Exception.Message)" -Category "PoolCreation"
                $stats.PoolsFailed++; $failedPoolsReport += $poolName
            }
        }
    }

    # VM Movement
    if ($MoveVMs) {
        Write-LogInfo "--- Starting VM Movement Phase ---" -Category "VMMovement"
        $allDestVMs = Get-VM -Location $cluster -ErrorAction Stop
        $vmLookup = @{}; $allDestVMs.ForEach({ $vmLookup[$_.Name.ToLower()] = $_ })
        
        foreach ($c in $configs) {
            $poolName = $c.Name; $vmNames = $c.VMs
            if ($failedPoolsReport -contains $poolName) {
                Write-LogWarning "Skipping VM movement for failed pool '$poolName'." -Category "VMMovement"
                if($vmNames) { $stats.VMsSkipped += $vmNames.Count; $skippedVMsReport += $vmNames | ForEach-Object { "$_ (Pool Failed)" } }
                continue
            }
            if (-not $vmNames) { continue }
            $destPool = $poolMap[$poolName]

            foreach ($vmName in $vmNames) {
                $vm = $vmLookup[$vmName.ToLower()]
                if (-not $vm) {
                    Write-LogWarning "VM '$vmName' not found in cluster. Skipping." -Category "VMMovement"
                    $stats.VMsFailed++; $failedVMsReport += "$vmName (Not Found)"
                    continue
                }
                if ($vm.ResourcePool.Id -eq $destPool.Id) {
                    Write-LogDebug "VM '$vmName' already in correct pool. Skipping." -Category "VMMovement"
                    $stats.VMsSkipped++; $skippedVMsReport += "$vmName (Already In Pool)"
                    continue
                }
                try {
                    Write-LogInfo "Moving VM '$vmName' to pool '$poolName'..." -Category "VMMovement"
                    Move-VM -VM $vm -Destination $destPool -ErrorAction Stop
                    $stats.VMsMoved++
                    $movedVMsReport += $vmName
                } catch {
                    Write-LogError "FAILED to move '$vmName': $($_.Exception.Message)" -Category "VMMovement"
                    $stats.VMsFailed++; $failedVMsReport += "$vmName (Move Error: $($_.Exception.Message))"
                }
            }
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "Resource pool import process finished."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with a critical error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
    # DISCONNECT REMOVED - Using persistent connections managed by application

    # Generate Report
    $endTime = Get-Date
    # ... Report generation logic remains here, using the stat variables ...

    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}