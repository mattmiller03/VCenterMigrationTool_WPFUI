<#
.SYNOPSIS
    Exports VMware vSphere resource pools and their configurations to a JSON file with a timestamp.

.DESCRIPTION
    Connects to a source vCenter, retrieves either all custom resource pools on
    a specified cluster or a named subset, and exports for each:
      • Name
      • Parent cluster or resource‐pool name
      • CPU shares level & count
      • CPU reservation
      • Memory shares level & count
      • Memory reservation
      • Associated VM names
      • Assigned permissions (principal, role, propagate)
    Automatically excludes any pools named "Resources" or "vCLS".
    Logs each step to a specified logfile.
    The output JSON filename will include a timestamp in the format _yyyyMMdd_HHmmss.

.PARAMETER SourceVC
    The hostname or IP of the source vCenter.

.PARAMETER SourceCred
    PSCredential for connecting to the source vCenter.

.PARAMETER OutputJson
    Path to write the exported JSON file. A timestamp will be added before the extension.
    E.g., '.\pools.json' will become '.\pools_20231027_103000.json'.

.PARAMETER All
    Switch. If specified, exports all non-built-in resource pools on the specified cluster.
    Requires -ClusterName.

.PARAMETER PoolNames
    One or more resource-pool names to export. Mutually exclusive with -All.

.PARAMETER ClusterName
    The name of the cluster to export resource pools from. Required when using -All.

.PARAMETER LogPath
    Path to the log file. Defaults to '.\Export-ResourcePools.log'.

.EXAMPLE
    # Export all custom pools from a cluster with timestamped filename
    .\Export-ResourcePools.ps1 `
      -SourceVC   'vcsa-src.lab.local' `
      -SourceCred (Get-Credential) `
      -OutputJson '.\all-pools.json' `
      -All `
      -ClusterName 'Cluster-A'

    # Export specific named pools with timestamped filename
    .\Export-ResourcePools.ps1 `
      -SourceVC   'vcsa-src.lab.local' `
      -SourceCred (Get-Credential) `
      -OutputJson '.\some-pools.json' `
      -PoolNames 'WebTier','DBTier'
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

    [Parameter(Mandatory = $false)]
    [string]$LogPath = ".\Export-ResourcePools.log"
)

function Write-Log {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [ValidateSet('INFO','WARN','ERROR')]
        [string]$Level = 'INFO'
    )

    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $entry     = "$timestamp [$Level] $Message"
    Add-Content -Path $LogPath -Value $entry

    switch ($Level) {
        'INFO'  { Write-Host $entry }
        'WARN'  { Write-Host $entry -ForegroundColor Yellow }
        'ERROR' { Write-Host $entry -ForegroundColor Red }
    }
}

# --- Script Start ---

# Initialize logfile
if (Test-Path -Path $LogPath) {
    Remove-Item -Path $LogPath -Force
}
Write-Log -Message "Script started. SourceVC='$SourceVC'."

# Generate timestamped output filename
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outputDir = Split-Path -Path $OutputJson -Parent
$outputBase = [System.IO.Path]::GetFileNameWithoutExtension($OutputJson)
$outputExt = [System.IO.Path]::GetExtension($OutputJson)
$timestampedOutputJson = Join-Path -Path $outputDir -ChildPath "$($outputBase)_$($timestamp)$($outputExt)"

Write-Log -Message "Original OutputJson path: '$OutputJson'"
Write-Log -Message "Generated Timestamped OutputJson path: '$timestampedOutputJson'"

# Import PowerCLI
try {
    Import-Module VMware.PowerCLI -ErrorAction Stop
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
    Write-Log -Message "Loaded VMware.PowerCLI."
}
catch {
    $err = $_.Exception.Message
    Write-Log -Message "Failed to load PowerCLI: $err" -Level 'ERROR'
    throw
}

# Connect to vCenter
try {
    $null = Connect-VIServer -Server $SourceVC -Credential $SourceCred -ErrorAction Stop
    Write-Log -Message "Connected to vCenter '$SourceVC'."
}
catch {
    $err = $_.Exception.Message
    Write-Log -Message "Failed to connect to '$SourceVC': $err" -Level 'ERROR'
    throw
}

# Retrieve cluster (only required for -All parameter set, but getting it early)
$cluster = $null # Initialize to null
if ($PSCmdlet.ParameterSetName -eq 'All') {
    try {
        $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
        Write-Log -Message "Found cluster '$ClusterName'."
    }
    catch {
        $err = $_.Exception.Message
        Write-Log -Message "Cluster '$ClusterName' not found: $err" -Level 'ERROR'
        Disconnect-VIServer -Confirm:$false | Out-Null
        throw
    }
}


# Retrieve resource pools
try {
    $allPools = @() # Initialize as empty array

    if ($All) {
        # Retrieve from the specified cluster location
        $allPools = Get-ResourcePool -Location $cluster -ErrorAction Stop |
                    Where-Object { $_.Name -notin 'Resources','vCLS' }
        Write-Log -Message "Retrieved $($allPools.Count) custom pools from cluster '$ClusterName' (excluding 'Resources','vCLS')."
    } else {
        # Retrieve specific named pools
        $allPools = Get-ResourcePool -ErrorAction Stop |
                    Where-Object { $PoolNames -contains $_.Name }

        # Check if any of the specified names were found
        if ($null -eq $allPools -or $allPools.Count -eq 0) {
             Write-Log -Message "No matching pools found for names: $($PoolNames -join ', ')" -Level 'ERROR'
             Disconnect-VIServer -Confirm:$false | Out-Null
             throw "No matching resource pools found for the provided names."
        }
        Write-Log -Message "Filtering to $($allPools.Count) pools by name."
    }
}
catch {
    $err = $_.Exception.Message
    Write-Log -Message "Error retrieving resource pools: $err" -Level 'ERROR'
    Disconnect-VIServer -Confirm:$false | Out-Null
    throw
}

# Build export objects
$exportList = foreach ($rp in $allPools) {
    try {
        $parent = $rp.Parent
        [PSCustomObject]@{
            Name               = $rp.Name
            ParentType         = $parent.GetType().Name
            ParentName         = $parent.Name
            CpuSharesLevel     = $rp.CpuSharesLevel
            CpuShares          = $rp.CpuShares
            CpuReservationMHz  = $rp.CpuReservationMHz
            MemSharesLevel     = $rp.MemSharesLevel
            MemShares          = $rp.MemShares
            MemReservationMB   = $rp.MemReservationMB
            VMs                = (Get-VM -Location $rp -ErrorAction Stop).Name
            Permissions        = (Get-VIPermission -Entity $rp -ErrorAction Stop |
                                  Select-Object Principal,Role,Propagate)
        }
    }
    catch {
        $err = $_.Exception.Message
        Write-Log -Message "Warning: failed exporting '$($rp.Name)': $err" -Level 'WARN'
    }
}

# Write JSON file using the timestamped path
try {
    $exportList | ConvertTo-Json -Depth 4 |
        Set-Content -Path $timestampedOutputJson -Encoding UTF8 -ErrorAction Stop
    Write-Log -Message "Exported $($exportList.Count) pool definitions to '$timestampedOutputJson'."
}
catch {
    $err = $_.Exception.Message
    Write-Log -Message "Failed to write JSON to '$timestampedOutputJson': $err" -Level 'ERROR'
    Disconnect-VIServer -Confirm:$false | Out-Null
    throw
}

# Disconnect
try {
    Disconnect-VIServer -Confirm:$false | Out-Null
    Write-Log -Message "Disconnected from vCenter."
}
catch {
    $err = $_.Exception.Message
    Write-Log -Message "Error disconnecting: $err" -Level 'WARN'
}

# Summary
Write-Log -Message "=== EXPORT SUMMARY ==="
Write-Log -Message ("Pools exported: {0}" -f $exportList.Count)

Write-Host "`n=== EXPORT SUMMARY ===" -ForegroundColor Cyan
Write-Host "Pools exported: $($exportList.Count)" -ForegroundColor Green
