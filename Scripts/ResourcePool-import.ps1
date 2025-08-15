<#
.SYNOPSIS
    Imports resource pools into a vSphere cluster and optionally moves VMs into them.
    Generates a detailed HTML report of all actions, results, and failures.

.PARAMETER DestVC
    The destination vCenter server.

.PARAMETER DestCred
    PSCredential for the destination vCenter.

.PARAMETER InputJson
    Path to the JSON file describing resource pools and their associated VMs.

.PARAMETER TargetCluster
    Name of the cluster in which to create the resource pools (and locate VMs).

.PARAMETER RemoveAllPools
    Switch. If specified, removes all non-built-in resource pools before import.

.PARAMETER MoveVMs
    Switch. If specified, after creating/finding the pools the script will attempt
    to move VMs (listed in the JSON) into their corresponding resource pools, but
    only if those VMs exist on the destination cluster.

.PARAMETER LogPath
    Path to the log file (default: .\Import-ResourcePools.log).

.PARAMETER ReportPath
    Path to the HTML report (default: .\ResourcePoolMigration_yyyyMMdd_HHmmss.html).

.EXAMPLE
    .\Import-ResourcePools.ps1 `
      -DestVC vcsa.lab.local `
      -DestCred (Get-Credential) `
      -InputJson .\pools_20231027_103000.json `
      -TargetCluster "Cluster-A" `
      -RemoveAllPools `
      -MoveVMs `
      -ReportPath "C:\Reports\Migration_Report.html"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]   $DestVC,
    [Parameter(Mandatory=$true)] [PSCredential] $DestCred,
    [Parameter(Mandatory=$true)] [string]   $InputJson,
    [Parameter(Mandatory=$true)] [string]   $TargetCluster,
    [Parameter(Mandatory=$false)] [switch]   $RemoveAllPools,
    [Parameter(Mandatory=$false)] [switch]   $MoveVMs,
    [Parameter(Mandatory=$false)] [string]   $LogPath = ".\Import-ResourcePools.log",
    [Parameter(Mandatory=$false)] [string]   $ReportPath = ".\ResourcePoolMigration_$(Get-Date -Format 'yyyyMMdd_HHmmss').html"
)

function Write-Log {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)] [string] $Message,
        [ValidateSet('INFO','WARN','ERROR')] [string] $Level = 'INFO'
    )
    $ts   = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "$ts [$Level] $Message"
    Add-Content -Path $LogPath -Value $line
    switch ($Level) {
        'INFO'  { Write-Host $line }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
    }
}

function Generate-MigrationReport {
    [CmdletBinding()]
    param(
        [string]$ReportPath,
        [string]$SourceVC,
        [string]$DestVC,
        [string]$TargetCluster,
        [string]$InputJson,
        [string]$LogPath,
        [datetime]$StartTime,
        [datetime]$EndTime,
        [string[]]$CreatedPools,
        [string[]]$SkippedPools,
        [string[]]$FailedPools,
        [string[]]$MovedVMs,
        [string[]]$SkippedVMs,
        [string[]]$FailedVMs,
        [switch]$MoveVMsEnabled
    )

    $duration = $EndTime - $StartTime
    $durationText = "{0:D2}h:{1:D2}m:{2:D2}s" -f $duration.Hours, $duration.Minutes, $duration.Seconds

    function Create-TableRows {
        param([array]$Items, [string]$Status, [string]$StatusClass)
        $rows = ""
        foreach ($item in $Items) {
            $name = $item
            $reason = ""
            if ($item -match '(.+) \( (.+) \)$') {
                $name = $matches[1]
                $reason = $matches[2]
            }
            $rows += @"
            <tr>
                <td><span class="status-badge $StatusClass">$Status</span></td>
                <td>$name</td>
                <td>$reason</td>
            </tr>
"@
        }
        return $rows
    }

    $poolRows = ""
    $poolRows += Create-TableRows -Items $CreatedPools -Status "Created" -StatusClass "success"
    $poolRows += Create-TableRows -Items $SkippedPools -Status "Skipped" -StatusClass "warning"
    $poolRows += Create-TableRows -Items ($FailedPools | ForEach-Object { "$_ (Creation Failed)" }) -Status "Failed" -StatusClass "danger"

    $vmRows = ""
    if ($MoveVMsEnabled) {
        $vmRows += Create-TableRows -Items $MovedVMs -Status "Moved" -StatusClass "success"
        $vmRows += Create-TableRows -Items $SkippedVMs -Status "Skipped" -StatusClass "warning"
        $vmRows += Create-TableRows -Items $FailedVMs -Status "Failed" -StatusClass "danger"
    }

    $html = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>vSphere Resource Pool Migration Report</title>
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        :root {
            --success-color: #28a745;
            --warning-color: #ffc107;
            --danger-color: #dc3545;
            --primary-color: #007bff;
            --light-color: #f8f9fa;
            --dark-color: #343a40;
            --gray-color: #6c757d;
        }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            color: #333;
            background-color: #f5f5f5;
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
        }
        .report-header {
            background-color: var(--primary-color);
            color: white;
            padding: 15px 20px;
            border-radius: 5px;
            margin-bottom: 20px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }
        .report-header h1 { margin: 0; font-size: 24px; }
        .report-meta {
            background-color: white;
            border-radius: 5px;
            padding: 15px 20px;
            margin-bottom: 20px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }
        .report-meta dl {
            display: grid;
            grid-template-columns: 180px 1fr;
            gap: 10px;
            margin: 0;
        }
        .report-meta dt {
            font-weight: bold;
            color: var(--gray-color);
        }
        .report-meta dd { margin: 0; }
        .summary-cards {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 20px;
            margin-bottom: 20px;
        }
        .card {
            background-color: white;
            border-radius: 5px;
            padding: 20px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
            text-align: center;
        }
        .card-success { border-top: 4px solid var(--success-color); }
        .card-warning { border-top: 4px solid var(--warning-color); }
        .card-danger { border-top: 4px solid var(--danger-color); }
        .card h2 {
            margin-top: 0;
            font-size: 16px;
            color: var(--gray-color);
            text-transform: uppercase;
        }
        .card .count {
            font-size: 36px;
            font-weight: bold;
            margin: 10px 0;
        }
        .success { color: var(--success-color); }
        .warning { color: var(--warning-color); }
        .danger { color: var(--danger-color); }
        .details-section {
            background-color: white;
            border-radius: 5px;
            padding: 20px;
            margin-bottom: 20px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }
        .details-section h2 {
            margin-top: 0;
            color: var(--dark-color);
            border-bottom: 1px solid #eee;
            padding-bottom: 10px;
        }
        table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 15px;
        }
        th, td {
            text-align: left;
            padding: 12px 15px;
            border-bottom: 1px solid #eee;
        }
        th {
            background-color: var(--light-color);
            font-weight: bold;
        }
        tr:hover { background-color: #f8f9fa; }
        .status-badge {
            display: inline-block;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 12px;
            font-weight: bold;
            text-transform: uppercase;
        }
        .status-badge.success { background-color: #e6f7ee; color: var(--success-color);}
        .status-badge.warning { background-color: #fff8e6; color: #b38600;}
        .status-badge.danger  { background-color: #f8e6e6; color: var(--danger-color);}
        .footer {
            text-align: center;
            margin-top: 30px;
            color: var(--gray-color);
            font-size: 14px;
        }
        @media (max-width: 768px) {
            .summary-cards { grid-template-columns: 1fr; }
            .report-meta dl { grid-template-columns: 1fr; }
            .report-meta dt { margin-top: 10px; }
        }
    </style>
</head>
<body>
    <div class="report-header">
        <h1>vSphere Resource Pool Migration Report</h1>
    </div>
    <div class="report-meta">
        <dl>
            <dt>Report Generated:</dt>
            <dd>$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')</dd>
            <dt>Source vCenter:</dt>
            <dd>$SourceVC</dd>
            <dt>Destination vCenter:</dt>
            <dd>$DestVC</dd>
            <dt>Target Cluster:</dt>
            <dd>$TargetCluster</dd>
            <dt>Input JSON:</dt>
            <dd>$InputJson</dd>
            <dt>Log File:</dt>
            <dd>$LogPath</dd>
            <dt>Execution Time:</dt>
            <dd>$durationText</dd>
        </dl>
    </div>
    <div class="summary-cards">
        <div class="card card-success">
            <h2>Pools Created</h2>
            <div class="count success">$($CreatedPools.Count)</div>
        </div>
        <div class="card card-warning">
            <h2>Pools Skipped</h2>
            <div class="count warning">$($SkippedPools.Count)</div>
        </div>
        <div class="card card-danger">
            <h2>Pools Failed</h2>
            <div class="count danger">$($FailedPools.Count)</div>
        </div>
    </div>
    $(if ($MoveVMsEnabled) {
@"
    <div class="summary-cards">
        <div class="card card-success">
            <h2>VMs Moved</h2>
            <div class="count success">$($MovedVMs.Count)</div>
        </div>
        <div class="card card-warning">
            <h2>VMs Skipped</h2>
            <div class="count warning">$($SkippedVMs.Count)</div>
        </div>
        <div class="card card-danger">
            <h2>VMs Failed</h2>
            <div class="count danger">$($FailedVMs.Count)</div>
        </div>
    </div>
"@
    })
    <div class="details-section">
        <h2>Resource Pool Details</h2>
        <table>
            <thead>
                <tr>
                    <th width="100">Status</th>
                    <th>Name</th>
                    <th>Details</th>
                </tr>
            </thead>
            <tbody>
                $poolRows
            </tbody>
        </table>
    </div>
    $(if ($MoveVMsEnabled -and ($MovedVMs.Count -gt 0 -or $SkippedVMs.Count -gt 0 -or $FailedVMs.Count -gt 0)) {
@"
    <div class="details-section">
        <h2>Virtual Machine Details</h2>
        <table>
            <thead>
                <tr>
                    <th width="100">Status</th>
                    <th>Name</th>
                    <th>Details</th>
                </tr>
            </thead>
            <tbody>
                $vmRows
            </tbody>
        </table>
    </div>
"@
    })
    <div class="footer">
        Report generated by PowerCLI Resource Pool Migration Script
    </div>
</body>
</html>
"@
    try {
        $html | Out-File -FilePath $ReportPath -Encoding UTF8 -Force
        Write-Log "HTML report generated at '$ReportPath'."
        return $true
    }
    catch {
        Write-Log "Failed to generate HTML report: $($_.Exception.Message)" -Level 'ERROR'
        return $false
    }
}

# --- Start ---
if (Test-Path $LogPath) { Remove-Item $LogPath -Force }
$startTime = Get-Date
Write-Log "Script started. DestVC='$DestVC', TargetCluster='$TargetCluster'."
if ($RemoveAllPools) { Write-Log "Option: Remove existing pools enabled." }
if ($MoveVMs)        { Write-Log "Option: Move VMs into pools enabled." }

# --- Load PowerCLI ---
try {
    Import-Module VMware.PowerCLI -ErrorAction Stop
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
    Write-Log "Loaded VMware.PowerCLI."
}
catch {
    Write-Log "Failed to load PowerCLI: $($_.Exception.Message)" -Level 'ERROR'
    throw
}

# --- Connect to vCenter ---
try {
    $null = Connect-VIServer -Server $DestVC -Credential $DestCred -ErrorAction Stop
    Write-Log "Connected to vCenter '$DestVC'."
}
catch {
    Write-Log "Failed to connect to vCenter '$DestVC': $($_.Exception.Message)" -Level 'ERROR'
    throw
}

# --- Locate target cluster ---
try {
    $cluster = Get-Cluster -Name $TargetCluster -ErrorAction Stop
    Write-Log "Found target cluster '$TargetCluster'."
}
catch {
    Write-Log "Cluster '$TargetCluster' not found: $($_.Exception.Message)" -Level 'ERROR'
    Disconnect-VIServer -Confirm:$false | Out-Null
    throw
}

# --- Optionally remove existing pools ---
if ($RemoveAllPools) {
    try {
        $toRemove = Get-ResourcePool -Location $cluster -ErrorAction Stop |
                    Where-Object { $_.Name -notin 'Resources','vCLS' }
        if ($toRemove) {
            Write-Log "Removing $($toRemove.Count) existing resource pools..."
            $toRemove | Remove-ResourcePool -Confirm:$false -ErrorAction Stop
            Write-Log "Removed existing resource pools."
        } else {
            Write-Log "No existing resource pools to remove."
        }
    }
    catch {
        Write-Log "Failed to remove existing pools: $($_.Exception.Message)" -Level 'ERROR'
        Disconnect-VIServer -Confirm:$false | Out-Null
        throw
    }
}

# --- Validate and parse JSON ---
if (-not (Test-Path $InputJson)) {
    Write-Log "Input JSON not found: '$InputJson'" -Level 'ERROR'
    Disconnect-VIServer -Confirm:$false | Out-Null
    throw "Input JSON file not found."
}
try {
    $configs = Get-Content $InputJson -Raw | ConvertFrom-Json
    if ($null -eq $configs) { throw "Empty or invalid JSON." }
    Write-Log "Loaded $($configs.Count) pool definitions from JSON."
}
catch {
    Write-Log "Failed to parse JSON: $($_.Exception.Message)" -Level 'ERROR'
    Disconnect-VIServer -Confirm:$false | Out-Null
    throw
}

# --- Resource Pool Creation ---
$createdPools = @()
$failedPools  = @()
$skippedPools = @()
$poolMap      = @{}   # Name → new/existing ResourcePool object

Write-Log "--- Starting Resource Pool Creation Phase ---"
foreach ($c in $configs) {
    $poolName = $c.Name
    $existingPool = Get-ResourcePool -Name $poolName -Location $cluster -ErrorAction SilentlyContinue

    if ($null -ne $existingPool) {
        Write-Log "Resource Pool '$poolName' already exists in cluster '$TargetCluster'. Skipping creation." -Level 'INFO'
        $skippedPools += $poolName
        $poolMap[$poolName] = $existingPool
    } else {
        try {
            Write-Log "Attempting to create pool '$poolName'..."
            $newPool = New-ResourcePool `
                       -Name                     $poolName `
                       -Location                 $cluster `
                       -CpuSharesLevel           Normal `
                       -MemSharesLevel           Normal `
                       -CpuReservationMHz        0 `
                       -MemReservationMB         0 `
                       -CpuLimitMHz             -1 `
                       -MemLimitMB              -1 `
                       -CpuExpandableReservation $true `
                       -MemExpandableReservation $true `
                       -ErrorAction              Stop

            $createdPools += $poolName
            $poolMap[$poolName] = $newPool
            Write-Log "Successfully created pool '$poolName'."
        }
        catch {
            Write-Log "FAILED to create pool '$poolName': $($_.Exception.Message)" -Level 'ERROR'
            $failedPools += $poolName
        }
    }
}
Write-Log "--- Resource Pool Creation Phase Finished ---"

# --- VM Movement Phase (Optional) ---
$movedVMs  = @()
$failedVMs = @()
$skippedVMs = @()

if ($MoveVMs) {
    Write-Log "--- Starting VM Movement Phase ---"
    try {
        $allDestVMs = Get-VM -Location $cluster -ErrorAction Stop
        Write-Log "Found $($allDestVMs.Count) VMs in cluster '$TargetCluster'."
    }
    catch {
        Write-Log "Failed to retrieve VMs: $($_.Exception.Message)" -Level 'ERROR'
        throw
    }
    $vmLookup = @{}
    foreach ($vm in $allDestVMs) {
        $vmLookup[$vm.Name.ToLower()] = $vm
    }
    foreach ($c in $configs) {
        $poolName = $c.Name
        $vmNames  = $c.VMs
        if ($failedPools -contains $poolName) {
            Write-Log "Skipping VM movement for pool '$poolName' (pool creation failed)." -Level 'WARN'
            if ($vmNames) { $skippedVMs += $vmNames | ForEach-Object {"$_ (Pool Failed)"} }
            continue
        }
        if (-not $vmNames -or $vmNames.Count -eq 0) { continue }
        if (-not $poolMap.ContainsKey($poolName)) {
             Write-Log "Could not find destination pool object for '$poolName' in map. Cannot move VMs." -Level 'ERROR'
             if ($vmNames) { $failedVMs += $vmNames | ForEach-Object {"$_ (Pool Object Missing)"} }
             continue
        }
        $destPool = $poolMap[$poolName]
        Write-Log "Processing VMs for pool '$poolName' ($($vmNames.Count) listed)..."
        foreach ($vmName in $vmNames) {
            $key = $vmName.ToLower()
            if (-not $vmLookup.ContainsKey($key)) {
                Write-Log "VM '$vmName' not found on target vCenter. Skipping." -Level 'WARN'
                $failedVMs += "$vmName (Not Found)"
                continue
            }
            $vm = $vmLookup[$key]
            if ($vm.ResourcePool.Name -eq $poolName) {
                Write-Log "VM '$vmName' already in pool '$poolName'. No action needed." -Level 'INFO'
                $skippedVMs += "$vmName (Already In Pool)"
                continue
            }
            try {
                Write-Log "Moving VM '$vmName' to pool '$poolName'..."
                Move-VM -VM $vm -Destination $destPool -ErrorAction Stop
                Write-Log "Successfully moved '$vmName' to '$poolName'."
                $movedVMs += $vmName
            }
            catch {
                Write-Log "FAILED to move '$vmName': $($_.Exception.Message)" -Level 'ERROR'
                $failedVMs += "$vmName (Error: $($_.Exception.Message))"
            }
        }
    }
    Write-Log "--- VM Movement Phase Finished ---"
}

# --- Disconnect ---
try {
    Disconnect-VIServer -Confirm:$false | Out-Null
    Write-Log "Disconnected from vCenter."
}
catch {
    Write-Log "Disconnect warning: $($_.Exception.Message)" -Level 'WARN'
}

# --- Summary ---
Write-Log "=== IMPORT SUMMARY ==="
Write-Log ("Pools Created: {0}" -f $createdPools.Count)
Write-Log ("Pools Skipped (Already Exist): {0}" -f $skippedPools.Count)
if ($failedPools.Count -gt 0) {
    Write-Log ("Pools Failed Creation: {0} -> {1}" -f $failedPools.Count, $failedPools -join ', ') -Level 'ERROR'
} else {
    Write-Log "Pool Creation Failures: 0"
}

if ($MoveVMs) {
    Write-Log "=== VM MOVEMENT SUMMARY ==="
    Write-Log ("VMs Attempted Move: {0}" -f ($movedVMs.Count + $failedVMs.Count + $skippedVMs.Count))
    Write-Log ("VMs Moved Successfully: {0}" -f $movedVMs.Count)
    Write-Log ("VMs Skipped (Not Found / Already In Pool / Pool Issue): {0}" -f $skippedVMs.Count)
    if ($failedVMs.Count -gt 0) {
        Write-Log ("VMs Failed Move: {0} -> {1}" -f $failedVMs.Count, $failedVMs -join '; ') -Level 'ERROR'
    } else {
        Write-Log "VM Movement Failures: 0"
    }
}

Write-Host "`n=== IMPORT SUMMARY ===" -ForegroundColor Cyan
Write-Host "Pools Created: $($createdPools.Count)" -ForegroundColor Green
Write-Host "Pools Skipped (Already Exist): $($skippedPools.Count)" -ForegroundColor Yellow
if ($failedPools.Count -gt 0) {
    Write-Host "Pools Failed Creation: $($failedPools.Count) -> $($failedPools -join ', ')" -ForegroundColor Red
} else {
    Write-Host "Pool Creation Failures: 0" -ForegroundColor Green
}
if ($MoveVMs) {
    Write-Host "`n=== VM MOVEMENT SUMMARY ===" -ForegroundColor Cyan
    Write-Host "VMs Moved Successfully: $($movedVMs.Count)" -ForegroundColor Green
    Write-Host "VMs Skipped (Not Found / Already In Pool / Pool Issue): $($skippedVMs.Count)" -ForegroundColor Yellow
    if ($failedVMs.Count -gt 0) {
        Write-Host "VMs Failed Move: $($failedVMs.Count) -> $($failedVMs -join '; ')" -ForegroundColor Red
    } else {
        Write-Host "VM Movement Failures: 0" -ForegroundColor Green
    }
}

# --- Report Generation ---
$endTime = Get-Date

# Attempt to read SourceVC from your JSON (if present),
# but always coerce it into a string—even if it’s $null or an array.
$sourceVC = ""
if (Test-Path $InputJson) {
    try {
        $jsonContent = Get-Content $InputJson -Raw | ConvertFrom-Json
        if ($jsonContent.PSObject.Properties['SourceVC']) {
            # cast whatever it is into a string
            $sourceVC = [string]$jsonContent.SourceVC
        }
    }
    catch {
        Write-Log "Warning: failed to extract SourceVC from JSON: $($_.Exception.Message)" -Level 'WARN'
    }
}

# wrap in double quotes to force string interpolation
$reportSuccess = Generate-MigrationReport `
    -ReportPath     $ReportPath `
    -SourceVC       "$sourceVC" `
    -DestVC         $DestVC `
    -TargetCluster  $TargetCluster `
    -InputJson      $InputJson `
    -LogPath        $LogPath `
    -StartTime      $startTime `
    -EndTime        $endTime `
    -CreatedPools   $createdPools `
    -SkippedPools   $skippedPools `
    -FailedPools    $failedPools `
    -MovedVMs       $movedVMs `
    -SkippedVMs     $skippedVMs `
    -FailedVMs      $failedVMs `
    -MoveVMsEnabled:$MoveVMs

if ($reportSuccess) {
    Write-Host "`nHTML report generated at: $ReportPath" -ForegroundColor Cyan
}

Write-Log "Script finished."
