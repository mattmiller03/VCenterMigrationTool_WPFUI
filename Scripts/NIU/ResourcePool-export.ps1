<#
.SYNOPSIS
    Exports VMware vSphere resource pools and their configurations to a timestamped JSON file.
.DESCRIPTION
    Connects to a source vCenter and retrieves specified resource pools, exporting their configuration
    and associated VM names to a JSON file.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceVC,
    [Parameter(Mandatory = $true)]
    [PSCredential]$SourceCred,
    [Parameter(Mandatory = $true)]
    [string]$OutputJson,
    [Parameter(ParameterSetName = 'All', Mandatory = $true)]
    [switch]$All,
    [Parameter(ParameterSetName = 'ByName', Mandatory = $true)]
    [string[]]$PoolNames,
    [Parameter(ParameterSetName = 'All', Mandatory = $true)]
    [string]$ClusterName,
    [Parameter(Mandatory=$false)]
    [string]$LogPath,
    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "ResourcePool-export" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$poolsExportedCount = 0

try {
    # Generate timestamped output filename
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $outputDir = Split-Path -Path $OutputJson -Parent
    $outputBase = [System.IO.Path]::GetFileNameWithoutExtension($OutputJson)
    $outputExt = [System.IO.Path]::GetExtension($OutputJson)
    $timestampedOutputJson = Join-Path -Path $outputDir -ChildPath "$($outputBase)_$($timestamp)$($outputExt)"
    Write-LogInfo "Export file will be saved to: $timestampedOutputJson" -Category "Setup"

    # Import PowerCLI
    Write-LogInfo "Importing PowerCLI module..." -Category "Initialization"
    Import-Module VMware.PowerCLI -ErrorAction Stop
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Write-LogSuccess "PowerCLI module imported." -Category "Initialization"
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter '$SourceVC'..." -Category "Connection"
    Connect-VIServer -Server $SourceVC -Credential $SourceCred -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter." -Category "Connection"
    
    # Retrieve resource pools
    $allPools = @()
    if ($All) {
        Write-LogInfo "Retrieving all custom pools from cluster '$ClusterName'." -Category "Discovery"
        $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
        $allPools = Get-ResourcePool -Location $cluster -ErrorAction Stop | Where-Object { $_.Name -notin 'Resources','vCLS' }
    } else {
        Write-LogInfo "Retrieving pools by name: $($PoolNames -join ', ')" -Category "Discovery"
        $allPools = Get-ResourcePool -ErrorAction Stop | Where-Object { $PoolNames -contains $_.Name }
        if ($allPools.Count -eq 0) { throw "No matching resource pools found for the provided names." }
    }
    Write-LogSuccess "Found $($allPools.Count) resource pools to export." -Category "Discovery"

    # Build export objects
    $exportList = foreach ($rp in $allPools) {
        Write-LogDebug "Processing pool: $($rp.Name)" -Category "Export"
        [PSCustomObject]@{
            Name = $rp.Name; ParentType = $rp.Parent.GetType().Name; ParentName = $rp.Parent.Name
            CpuSharesLevel = $rp.CpuSharesLevel; CpuShares = $rp.CpuShares; CpuReservationMHz = $rp.CpuReservationMHz
            MemSharesLevel = $rp.MemSharesLevel; MemShares = $rp.MemShares; MemReservationMB = $rp.MemReservationMB
            VMs = (Get-VM -Location $rp -ErrorAction SilentlyContinue).Name
            Permissions = (Get-VIPermission -Entity $rp -ErrorAction SilentlyContinue | Select-Object Principal,Role,Propagate)
        }
    }
    $poolsExportedCount = $exportList.Count

    # Write JSON file
    Write-LogInfo "Writing $poolsExportedCount pool definitions to JSON file..." -Category "Export"
    $exportList | ConvertTo-Json -Depth 4 | Set-Content -Path $timestampedOutputJson -Encoding UTF8 -ErrorAction Stop
    Write-LogSuccess "Export completed successfully." -Category "Export"
    
    $scriptSuccess = $true
    $finalSummary = "Successfully exported $poolsExportedCount resource pool definitions to '$timestampedOutputJson'."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with a critical error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
    # DISCONNECT REMOVED - Using persistent connections managed by application
    
    $finalStats = @{
        "SourceVCenter" = $SourceVC
        "PoolsExported" = $poolsExportedCount
        "OutputFile" = $timestampedOutputJson
    }
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}