<#
.SYNOPSIS
    Post-migration VM placement cleanup script for VMware vCenter.
.DESCRIPTION
    This script reads a JSON backup file containing VM configurations, then verifies and corrects
    the placement of VMs (folder and resource pool) in the new vCenter.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$JsonBackupPath,
    [Parameter(Mandatory=$true)]
    [string]$NewVCenterServer,
    [Parameter(Mandatory=$true)]
    [pscredential]$Credential,
    [Parameter(Mandatory=$false)]
    [string]$LogPath,
    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false,
    [Parameter(Mandatory=$false)]
    [switch]$WhatIf
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "VMPostMigrationCleanup" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$stats = @{
    "VMsProcessed" = 0
    "VMsNotFound" = 0
    "VMsRenamed" = 0
    "FoldersCorrected" = 0
    "ResourcePoolsCorrected" = 0
    "Errors" = 0
}

# --- Functions ---

function Load-VMBackupJson {
    param([string]$JsonFilePath)
    try {
        Write-LogInfo "Loading backup JSON from $JsonFilePath" -Category "DataLoad"
        $allEntries = Get-Content -Path $JsonFilePath -Raw | ConvertFrom-Json -ErrorAction Stop
        $vmConfigs = $allEntries | Where-Object { $_ -is [PSCustomObject] -and $_.PSObject.Properties.Name -contains 'VMName' }
        Write-LogSuccess "Loaded $($vmConfigs.Count) VM configurations from JSON." -Category "DataLoad"
        return $vmConfigs
    } catch {
        Write-LogError "Failed to load or parse JSON file: $($_.Exception.Message)" -Category "DataLoad"
        throw $_
    }
}

function Resolve-FolderByPath {
    param([string]$FolderPath)
    $parts = $FolderPath.Trim('/').Split('/')
    if ($parts[0] -ne 'vm') {
        Write-LogError "Folder path '$FolderPath' must start with '/vm/'." -Category "Resolver"
        return $null
    }
    $currentFolder = Get-Folder -Name 'vm' -ErrorAction Stop
    foreach ($part in $parts[1..($parts.Length - 1)]) {
        $childFolders = Get-Folder -Name $part -Location $currentFolder -ErrorAction SilentlyContinue
        if (-not $childFolders) {
            Write-LogError "Folder '$part' not found under '$($currentFolder.Name)'" -Category "Resolver"
            return $null
        }
        $currentFolder = $childFolders[0]
    }
    return $currentFolder
}

function Resolve-ResourcePoolByPath {
    param([string]$ResourcePoolPath, [string]$ClusterName)
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction SilentlyContinue
    if (-not $cluster) {
        Write-LogError "Cluster '$ClusterName' not found" -Category "Resolver"
        return $null
    }
    $rootRP = Get-ResourcePool -Name 'Resources' -Location $cluster -ErrorAction SilentlyContinue
    $parts = $ResourcePoolPath.Split('/') | Where-Object { $_ -ne 'Resources' }
    $currentRP = $rootRP
    foreach ($part in $parts) {
        $childRP = Get-ResourcePool -Name $part -Location $currentRP -ErrorAction SilentlyContinue
        if (-not $childRP) {
            Write-LogError "Resource pool '$part' not found under '$($currentRP.Name)'" -Category "Resolver"
            return $null
        }
        $currentRP = $childRP
    }
    return $currentRP
}

try {
    # Cluster name mapping
    $clusterNameMap = @{ "DLA_Dayton_Dev" = "DLA_Dayton_Dev"; "ETC_Dev-Test" = "ETC_Dev-Compute" }
    
    Write-LogInfo "Importing PowerCLI module..." -Category "Initialization"
    Import-Module VMware.PowerCLI -ErrorAction Stop
    Write-LogSuccess "PowerCLI module imported." -Category "Initialization"

    Write-LogInfo "Connecting to vCenter server: $NewVCenterServer" -Category "Connection"
    Connect-VIServer -Server $NewVCenterServer -Credential $Credential -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter." -Category "Connection"

    $backupVMs = Load-VMBackupJson -JsonFilePath $JsonBackupPath

    foreach ($vmConfig in $backupVMs) {
        $stats.VMsProcessed++
        $vmName = $vmConfig.VMName
        if ([string]::IsNullOrWhiteSpace($vmName)) { continue }
        if ($vmName -like 'vCLS*') {
            Write-LogInfo "Skipping cluster resource VM: $vmName" -Category "Filter"
            continue
        }

        $vm = Get-VM -Name $vmName -ErrorAction SilentlyContinue
        if (-not $vm) {
            $importedName = "$vmName-imported"
            $vm = Get-VM -Name $importedName -ErrorAction SilentlyContinue
        }
        if (-not $vm) {
            Write-LogWarning "VM '$vmName' not found in vCenter (including '-imported' suffix)." -Category "Discovery"
            $stats.VMsNotFound++
            continue
        }

        Write-LogInfo "Processing VM: $($vm.Name)" -Category "Processing"

        # Rename if needed
        if ($vm.Name.EndsWith('-imported')) {
            $originalName = $vm.Name -replace '-imported$'
            if (Get-VM -Name $originalName -ErrorAction SilentlyContinue) {
                Write-LogError "Cannot rename '$($vm.Name)' to '$originalName' because a VM with that name already exists." -Category "Rename"
                $stats.Errors++
            } else {
                Write-LogInfo "Renaming VM '$($vm.Name)' to '$originalName'" -Category "Rename"
                $whatIfParams = if ($WhatIf) { @{ WhatIf = $true } } else { @{} }
                Set-VM -VM $vm -Name $originalName -Confirm:$false -ErrorAction Stop @whatIfParams
                $stats.VMsRenamed++
                if (-not $WhatIf) { $vm = Get-VM -Name $originalName -ErrorAction Stop }
            }
        }

        # Folder placement
        $targetFolder = Resolve-FolderByPath -FolderPath $vmConfig.FolderPath
        if ($targetFolder -and $vm.Folder.Id -ne $targetFolder.Id) {
            Write-LogInfo "Moving VM '$($vm.Name)' from folder '$($vm.Folder.Name)' to '$($targetFolder.Name)'" -Category "Placement"
            $whatIfParams = if ($WhatIf) { @{ WhatIf = $true } } else { @{} }
            Move-VM -VM $vm -InventoryLocation $targetFolder -ErrorAction Stop @whatIfParams
            $stats.FoldersCorrected++
        }

        # Resource Pool placement
        $oldClusterName = $vmConfig.Cluster
        $clusterName = $clusterNameMap[$oldClusterName] ?? $oldClusterName
        $targetRP = Resolve-ResourcePoolByPath -ResourcePoolPath $vmConfig.ResourcePool -ClusterName $clusterName
        if ($targetRP -and $vm.ResourcePool.Id -ne $targetRP.Id) {
            Write-LogInfo "Moving VM '$($vm.Name)' from resource pool '$($vm.ResourcePool.Name)' to '$($targetRP.Name)'" -Category "Placement"
            $whatIfParams = if ($WhatIf) { @{ WhatIf = $true } } else { @{} }
            Move-VM -VM $vm -Destination $targetRP -ErrorAction Stop @whatIfParams
            $stats.ResourcePoolsCorrected++
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "VM cleanup process completed. Processed $($stats.VMsProcessed) VMs from the backup file."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with a critical error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
    Disconnect-VIServer -Server $NewVCenterServer -Confirm:$false -ErrorAction SilentlyContinue
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}