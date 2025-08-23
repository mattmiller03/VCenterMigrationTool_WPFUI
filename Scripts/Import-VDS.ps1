<#
.SYNOPSIS
    Imports Virtual Distributed Switch (vDS) using PowerCLI 13.x native New-VDSwitch -BackupPath cmdlet
.DESCRIPTION
    Connects to target vCenter and recreates vDS switches from native PowerCLI backup files
    using the New-VDSwitch -BackupPath cmdlet for complete configuration restoration.
.NOTES
    Version: 2.0 - Using native PowerCLI New-VDSwitch -BackupPath cmdlet
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
    [Parameter(Mandatory=$true)]
    [string]$ImportPath,
    
    [Parameter()]
    [string]$DatacenterName = "",  # Target datacenter, if empty uses first available
    
    [Parameter()]
    [bool]$ValidateOnly = $false,
    
    [Parameter()]
    [bool]$OverwriteExisting = $false,
    
    [Parameter()]
    [bool]$BypassModuleCheck = $false,
    
    [Parameter()]
    [string]$LogPath,
    
    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Import-VDS" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$importStats = @{
    SwitchesRestored = 0
    SwitchesSkipped = 0
    Errors = 0
}

try {
    Write-LogInfo "Starting vDS import process using native New-VDSwitch -BackupPath cmdlet" -Category "Initialization"
    
    # Validate import file exists
    if (-not (Test-Path $ImportPath)) {
        throw "Import file not found: $ImportPath"
    }
    
    Write-LogInfo "Reading import reference file: $ImportPath" -Category "Import"
    $importContent = Get-Content -Path $ImportPath -Raw
    $importReference = $importContent | ConvertFrom-Json
    
    # Check if this is a native VDS backup
    if ($importReference.ExportType -ne "VDS_Native_Backup") {
        throw "Invalid import file format. Expected VDS_Native_Backup, got: $($importReference.ExportType)"
    }
    
    $vdsExportDir = $importReference.ExportDirectory
    $manifestFile = $importReference.ManifestFile
    
    if (-not (Test-Path $vdsExportDir)) {
        throw "VDS export directory not found: $vdsExportDir"
    }
    
    if (-not (Test-Path $manifestFile)) {
        throw "Manifest file not found: $manifestFile"
    }
    
    # Read manifest
    Write-LogInfo "Reading export manifest: $manifestFile" -Category "Import"
    $manifest = Get-Content -Path $manifestFile -Raw | ConvertFrom-Json
    Write-LogInfo "Manifest contains $($manifest.TotalSwitches) vDS switches from $($manifest.SourceVCenter)" -Category "Import"
    
    if ($ValidateOnly) {
        Write-LogInfo "VALIDATION MODE: No changes will be made to the target vCenter" -Category "Validation"
    }
    
    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
        Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
    }
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to vCenter
    Write-LogInfo "Connecting to target vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Get target datacenter
    if ($DatacenterName) {
        $datacenter = Get-Datacenter -Name $DatacenterName -ErrorAction SilentlyContinue
        if (-not $datacenter) {
            throw "Datacenter '$DatacenterName' not found in target vCenter"
        }
    }
    else {
        $datacenter = Get-Datacenter | Select-Object -First 1
        if (-not $datacenter) {
            throw "No datacenters found in target vCenter"
        }
        Write-LogInfo "Using datacenter: $($datacenter.Name)" -Category "Import"
    }
    
    # Process each vDS from the manifest
    foreach ($vdsInfo in $manifest.ExportedSwitches) {
        try {
            Write-LogInfo "Processing vDS: $($vdsInfo.Name)" -Category "Import"
            
            # Check if backup file exists
            if (-not (Test-Path $vdsInfo.BackupFile)) {
                Write-LogError "Backup file not found: $($vdsInfo.BackupFile)" -Category "Error"
                $importStats.Errors++
                continue
            }
            
            # Check if vDS already exists
            $existingVds = Get-VDSwitch -Name $vdsInfo.Name -ErrorAction SilentlyContinue
            
            if ($existingVds) {
                if ($OverwriteExisting -and -not $ValidateOnly) {
                    Write-LogWarning "Removing existing vDS: $($vdsInfo.Name)" -Category "Import"
                    Remove-VDSwitch -VDSwitch $existingVds -Confirm:$false -ErrorAction Stop
                    $existingVds = $null
                }
                else {
                    Write-LogWarning "vDS '$($vdsInfo.Name)' already exists - skipping" -Category "Import"
                    $importStats.SwitchesSkipped++
                    continue
                }
            }
            
            if ($ValidateOnly) {
                Write-LogInfo "VALIDATION: Would restore vDS '$($vdsInfo.Name)' from backup $($vdsInfo.BackupFile)" -Category "Validation"
                $importStats.SwitchesRestored++
                continue
            }
            
            # Restore vDS using native New-VDSwitch -BackupPath
            Write-LogInfo "Restoring vDS '$($vdsInfo.Name)' from backup: $($vdsInfo.BackupFile)" -Category "Import"
            
            try {
                $restoredVds = New-VDSwitch -Name $vdsInfo.Name -Location $datacenter -BackupPath $vdsInfo.BackupFile -ErrorAction Stop
                
                if ($restoredVds) {
                    Write-LogSuccess "Successfully restored vDS: $($restoredVds.Name)" -Category "Import"
                    $importStats.SwitchesRestored++
                    
                    # Get port group count for logging
                    $portGroups = Get-VDPortgroup -VDSwitch $restoredVds | Where-Object { -not $_.IsUplink }
                    Write-LogInfo "Restored vDS contains $($portGroups.Count) port groups" -Category "Import"
                }
                else {
                    throw "New-VDSwitch returned null"
                }
                
            } catch {
                # If restore fails due to name conflict, try with a temporary name and rename
                if ($_.Exception.Message -match "already exists" -or $_.Exception.Message -match "duplicate") {
                    Write-LogWarning "Name conflict detected, attempting alternative restore method..." -Category "Import"
                    
                    $tempName = "$($vdsInfo.Name)_temp_$(Get-Random)"
                    $tempVds = New-VDSwitch -Name $tempName -Location $datacenter -BackupPath $vdsInfo.BackupFile -ErrorAction Stop
                    
                    if ($tempVds) {
                        # Rename to original name
                        $restoredVds = Set-VDSwitch -VDSwitch $tempVds -Name $vdsInfo.Name -Confirm:$false -ErrorAction Stop
                        Write-LogSuccess "Successfully restored vDS with rename: $($restoredVds.Name)" -Category "Import"
                        $importStats.SwitchesRestored++
                    }
                    else {
                        throw "Failed to restore with temporary name"
                    }
                }
                else {
                    throw $_
                }
            }
            
        } catch {
            Write-LogError "Failed to restore vDS '$($vdsInfo.Name)': $($_.Exception.Message)" -Category "Error"
            $importStats.Errors++
        }
    }
    
    # Generate summary
    $scriptSuccess = ($importStats.Errors -eq 0)
    
    if ($ValidateOnly) {
        $finalSummary = "Validation complete: Would restore $($importStats.SwitchesRestored) vDS switches"
    }
    else {
        $finalSummary = "Import complete: Restored $($importStats.SwitchesRestored) vDS switches using native PowerCLI backup"
        if ($importStats.SwitchesSkipped -gt 0) {
            $finalSummary += " (Skipped: $($importStats.SwitchesSkipped) switches)"
        }
        if ($importStats.Errors -gt 0) {
            $finalSummary += " - $($importStats.Errors) errors occurred"
        }
    }
    
    # Output summary for the application
    if ($scriptSuccess) {
        Write-Output "SUCCESS: $finalSummary"
    }
    else {
        Write-Output "WARNING: $finalSummary"
    }
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Import failed: $($_.Exception.Message)"
    Write-LogError "Import failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $importStats
}