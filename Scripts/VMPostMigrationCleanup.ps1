<#
.SYNOPSIS
    Post-migration VM placement cleanup script for VMware vCenter.

.DESCRIPTION
    This script reads a JSON backup file containing VM configurations from the old datacenter,
    then verifies and corrects the placement of VMs in the new vCenter. It ensures each VM is
    placed in the correct folder and resource pool as specified in the backup.

    VMs with names starting with 'vCLS' (cluster resource VMs) are excluded from processing.

    Supports cluster name mapping for situations where cluster names have changed.

    Detects and renames VMs ending with '-imported' back to their original names before processing.

.PARAMETER JsonBackupPath
    Path to the JSON backup file containing VM configuration data.

.PARAMETER NewVCenterServer
    The hostname or IP address of the new vCenter server.

.PARAMETER Credential
    PSCredential object used to authenticate to the new vCenter server.

.PARAMETER LogPath
    Optional path to a log file. Default is 'C:\Logs\VMPlacementCleanup.log'.

.PARAMETER WhatIf
    Switch parameter. If specified, runs the script in test mode without making changes.
    Move-VM and Set-VM cmdlets will use the -WhatIf flag.

.EXAMPLE
    $cred = Get-Credential
    .\VMPostMigrationCleanup.ps1 -JsonBackupPath "C:\backups\vms.json" -NewVCenterServer "vcenter.example.com" -Credential $cred -WhatIf

.NOTES
    Requires VMware PowerCLI module version 13.3 or later.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$JsonBackupPath,

    [Parameter(Mandatory=$true)]
    [string]$NewVCenterServer,

    [Parameter(Mandatory=$true)]
    [pscredential]$Credential,

    [string]$LogPath = "C:\Logs\VMPlacementCleanup.log",

    [switch]$WhatIf
)

# Cluster name mapping: old cluster names (keys) to new cluster names (values)
$clusterNameMap = @{
    # Replace with your actual cluster mappings
    "DLA_Dayton_Dev" = "DLA_Dayton_Dev"
    "ETC_Dev-Test"   = "ETC_Dev-Compute"
}

Import-Module VMware.PowerCLI -ErrorAction Stop

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp][$Level] $Message"
    Add-Content -Path $LogPath -Value $logEntry
    Write-Host $logEntry
}

function Load-VMBackupJson {
    param(
        [Parameter(Mandatory=$true)]
        [string]$JsonFilePath
    )

    try {
        Write-Log "Loading backup JSON from $JsonFilePath"
        $allEntries = Get-Content -Path $JsonFilePath -Raw | ConvertFrom-Json -ErrorAction Stop

        # Filter only VM objects (exclude strings or other types)
        $vmConfigs = $allEntries | Where-Object {
            $_ -is [PSCustomObject] -and $_.PSObject.Properties.Name -contains 'VMName'
        }

        return $vmConfigs
    }
    catch {
        Write-Log "Failed to load or parse JSON file: $_" "ERROR"
        throw $_
    }
}

function Resolve-FolderByPath {
    param(
        [string]$FolderPath
    )
    $parts = $FolderPath.Trim('/').Split('/')

    if ($parts[0] -ne 'vm') {
        Write-Log "Folder path '$FolderPath' does not start with '/vm/'" "ERROR"
        return $null
    }

    $currentFolder = Get-Folder -Name 'vm' -ErrorAction Stop

    foreach ($part in $parts[1..($parts.Length - 1)]) {
        $childFolders = Get-Folder -Name $part -Location $currentFolder -ErrorAction SilentlyContinue

        if (-not $childFolders) {
            Write-Log "Folder '$part' not found under '$($currentFolder.Name)'" "ERROR"
            return $null
        }
        elseif ($childFolders.Count -gt 1) {
            Write-Log "Multiple folders named '$part' found under '$($currentFolder.Name)'. Please ensure folder names are unique." "ERROR"
            return $null
        }

        $currentFolder = $childFolders
    }
    return $currentFolder
}


function Resolve-ResourcePoolByPath {
    param(
        [string]$ResourcePoolPath,
        [string]$ClusterName
    )
    $cluster = Get-Cluster -Name $ClusterName -ErrorAction SilentlyContinue
    if (-not $cluster) {
        Write-Log "Cluster '$ClusterName' not found" "ERROR"
        return $null
    }

    $rootRP = Get-ResourcePool -Name 'Resources' -Location $cluster -ErrorAction SilentlyContinue
    if (-not $rootRP) {
        Write-Log "Root resource pool 'Resources' not found under cluster '$ClusterName'" "ERROR"
        return $null
    }

    $parts = $ResourcePoolPath.Split('/')

    if ($parts[0] -eq 'Resources') {
        $parts = $parts[1..($parts.Length - 1)]
    }

    $currentRP = $rootRP
    foreach ($part in $parts) {
        $childRP = Get-ResourcePool -Name $part -Location $currentRP -ErrorAction SilentlyContinue
        if (-not $childRP) {
            Write-Log "Resource pool '$part' not found under '$($currentRP.Name)'" "ERROR"
            return $null
        }
        $currentRP = $childRP
    }
    return $currentRP
}

try {
    Write-Log "Connecting to vCenter server: $($NewVCenterServer)"
    Connect-VIServer -Server $NewVCenterServer -Credential $Credential -ErrorAction Stop

    $backupVMs = Load-VMBackupJson -JsonFilePath $JsonBackupPath

    foreach ($vmConfig in $backupVMs) {
        $vmName = $vmConfig.VMName

        if (-not $vmName -or [string]::IsNullOrWhiteSpace($vmName)) {
            Write-Log "Skipping VM entry with missing or empty VMName"
            continue
        }

        # Exclude cluster resource VMs starting with 'vCLS'
        if ($vmName -like 'vCLS*') {
            Write-Log "Skipping cluster resource VM: $($vmName)"
            continue
        }

        # Attempt to get the VM by name (which may have '-imported' suffix)
        $vm = Get-VM -Name $vmName -ErrorAction SilentlyContinue

        # If not found as is, try to find it with '-imported' suffix (common migration rename)
        if (-not $vm) {
            $importedName = "$vmName-imported"
            $vm = Get-VM -Name $importedName -ErrorAction SilentlyContinue
            if ($vm) {
                Write-Log "Found VM with '-imported' suffix: $importedName"
            }
        }

        if (-not $vm) {
            Write-Log "VM '$($vmName)' not found in vCenter (including '-imported' suffix)" "WARNING"
            continue
        }

        # Rename VM if it ends with '-imported'
        if ($vm.Name -like '*-imported') {
            $originalName = $vm.Name -replace '-imported$', ''

            # Check if original name is already taken
            $existingVM = Get-VM -Name $originalName -ErrorAction SilentlyContinue
            if ($existingVM) {
                Write-Log "Cannot rename VM '$($vm.Name)' to '$originalName' because a VM with that name already exists." "ERROR"
                # Skip rename, continue with current name
            }
            else {
                Write-Log "Renaming VM '$($vm.Name)' to '$originalName'"
                if ($WhatIf) {
                    Set-VM -VM $vm -Name $originalName -WhatIf -Confirm:$false
                }
                else {
                    Set-VM -VM $vm -Name $originalName -Confirm:$false -ErrorAction Stop
                    # Refresh VM object with new name
                    $vm = Get-VM -Name $originalName -ErrorAction Stop
                }
            }
        }

        Write-Log "Processing VM: $($vm.Name)"

        # Folder placement
        $targetFolder = Resolve-FolderByPath -FolderPath $vmConfig.FolderPath
        if (-not $targetFolder) {
            Write-Log "Target folder '$($vmConfig.FolderPath)' not found for VM '$($vm.Name)'" "ERROR"
        }
        elseif ($vm.Folder.Id -ne $targetFolder.Id) {
            Write-Log "Moving VM '$($vm.Name)' from folder '$($vm.Folder.Name)' to '$($targetFolder.Name)'"
            if ($WhatIf) {
                Move-VM -VM $vm -InventoryLocation $targetFolder -WhatIf
            }
            else {
                Move-VM -VM $vm -InventoryLocation $targetFolder -ErrorAction Stop
            }
        }
        else {
            Write-Log "VM '$($vm.Name)' is already in correct folder"
        }

        # Cluster name mapping
        $oldClusterName = $vmConfig.Cluster
        $clusterName = if ($clusterNameMap.ContainsKey($oldClusterName)) {
            $clusterNameMap[$oldClusterName]
        }
        else {
            $oldClusterName
        }

        if ($clusterName -ne $oldClusterName) {
            Write-Log "Mapping old cluster name '$oldClusterName' to new cluster name '$clusterName'"
        }

        # Resource pool placement
        $targetRP = Resolve-ResourcePoolByPath -ResourcePoolPath $vmConfig.ResourcePool -ClusterName $clusterName
        if (-not $targetRP) {
            Write-Log "Target resource pool '$($vmConfig.ResourcePool)' not found for VM '$($vm.Name)'" "ERROR"
        }
        elseif ($vm.ResourcePool.Id -ne $targetRP.Id) {
            Write-Log "Moving VM '$($vm.Name)' from resource pool '$($vm.ResourcePool.Name)' to '$($targetRP.Name)'"
            if ($WhatIf) {
                Move-VM -VM $vm -Destination $targetRP -WhatIf
            }
            else {
                Move-VM -VM $vm -Destination $targetRP -ErrorAction Stop
            }
        }
        else {
            Write-Log "VM '$($vm.Name)' is already in correct resource pool"
        }
    }
}
catch {
    Write-Log "Error occurred: $_" "ERROR"
}
finally {
    Write-Log "Disconnecting from vCenter"
    Disconnect-VIServer -Server $NewVCenterServer -Confirm:$false
    Write-Log "Script completed"
}
