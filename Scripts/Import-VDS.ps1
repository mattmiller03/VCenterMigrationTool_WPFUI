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

# Embedded logging functions for SDK execution compatibility
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $false

function Write-LogInfo { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Info] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor White }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogSuccess { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Success] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Green }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogWarning { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Warning] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Yellow }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogError { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Error] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Red }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Start-ScriptLogging {
    param(
        [string]$ScriptName = '',
        [string]$LogPath = $null,
        [bool]$SuppressConsoleOutput = $false
    )
    
    $Global:SuppressConsoleOutput = $SuppressConsoleOutput
    
    if ($LogPath) {
        if ([System.IO.Path]::HasExtension($LogPath)) {
            $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
        } else {
            $logDir = $LogPath
        }
        
        $psLogDir = Join-Path $logDir "PowerShell"
        if (-not (Test-Path $psLogDir)) {
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }
        
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $sessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir "${ScriptName}_${timestamp}_${sessionId}.log"
        
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        "SCRIPT START: $ScriptName" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

function Stop-ScriptLogging {
    param(
        [bool]$Success = $true,
        [string]$Summary = "",
        [hashtable]$Statistics = @{}
    )
    
    if ($Global:ScriptLogFile) {
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        if ($Success) {
            "SCRIPT COMPLETED SUCCESSFULLY" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        } else {
            "SCRIPT FAILED" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Summary) {
            "Summary: $Summary" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Statistics.Count -gt 0) {
            "Statistics:" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            foreach ($key in $Statistics.Keys) {
                "    $key = $($Statistics[$key])" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            }
        }
        
        "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

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
    
    # Get available datacenters for datacenter mapping
    $availableDatacenters = Get-Datacenter
    if (-not $availableDatacenters) {
        throw "No datacenters found in target vCenter"
    }
    
    Write-LogInfo "Available target datacenters: $($availableDatacenters.Name -join ', ')" -Category "Import"
    
    # Set default datacenter for cases where original DC is not found
    $defaultDatacenter = if ($DatacenterName) {
        $specifiedDC = $availableDatacenters | Where-Object { $_.Name -eq $DatacenterName }
        if ($specifiedDC) { 
            $specifiedDC 
        } else { 
            throw "Specified datacenter '$DatacenterName' not found in target vCenter"
        }
    } else {
        $availableDatacenters | Select-Object -First 1
    }
    
    Write-LogInfo "Default datacenter for missing mappings: $($defaultDatacenter.Name)" -Category "Import"
    
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
            
            # Determine target datacenter for this vDS
            $targetDatacenter = $defaultDatacenter
            if ($vdsInfo.DatacenterName -and $vdsInfo.DatacenterName -ne "Unknown") {
                $matchingDatacenter = $availableDatacenters | Where-Object { $_.Name -eq $vdsInfo.DatacenterName }
                if ($matchingDatacenter) {
                    $targetDatacenter = $matchingDatacenter
                    Write-LogInfo "Using original datacenter '$($targetDatacenter.Name)' for vDS '$($vdsInfo.Name)'" -Category "Import"
                } else {
                    Write-LogWarning "Original datacenter '$($vdsInfo.DatacenterName)' not found, using default '$($defaultDatacenter.Name)'" -Category "Import"
                }
            } else {
                Write-LogWarning "No datacenter info in export for vDS '$($vdsInfo.Name)', using default '$($defaultDatacenter.Name)'" -Category "Import"
            }

            # Restore vDS using native New-VDSwitch -BackupPath
            Write-LogInfo "Restoring vDS '$($vdsInfo.Name)' to datacenter '$($targetDatacenter.Name)' from backup: $($vdsInfo.BackupFile)" -Category "Import"
            
            try {
                $restoredVds = New-VDSwitch -Name $vdsInfo.Name -Location $targetDatacenter -BackupPath $vdsInfo.BackupFile -ErrorAction Stop
                
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