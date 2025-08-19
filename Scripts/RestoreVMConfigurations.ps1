<#
.SYNOPSIS
    Validates or restores VM configurations from a backup file.
.DESCRIPTION
    Loads a JSON or ZIP backup file containing VM configurations and either validates
    its content or applies the configurations to the target environment.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFilePath,
    
    [Parameter(Mandatory = $false)]
    [bool]$ValidateOnly = $true,

    [Parameter(Mandatory = $false)]
    [string]$LogPath,

    [Parameter(Mandatory = $false)]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "RestoreVMConfigurations" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$backupContent = $null

try {
    Write-LogInfo "Starting VM configuration restore/validation..." -Category "Initialization"
    
    if (-not (Test-Path $BackupFilePath)) {
        throw "Backup file not found: $BackupFilePath"
    }
    Write-LogInfo "Backup file found: $BackupFilePath" -Category "Validation"
    
    # Load backup data
    Write-LogInfo "Loading backup data..." -Category "DataLoad"
    if ($BackupFilePath.EndsWith('.zip')) {
        Write-LogDebug "Detected compressed backup file (.zip)." -Category "DataLoad"
        $tempDir = Join-Path [System.IO.Path]::GetTempPath() "VMRestore_$(Get-Random)"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($BackupFilePath, $tempDir)
        $jsonFile = Get-ChildItem -Path $tempDir -Filter "*.json" | Select-Object -First 1
        if (-not $jsonFile) { throw "No JSON file found in backup archive" }
        $backupContent = Get-Content -Path $jsonFile.FullName -Raw | ConvertFrom-Json
        Remove-Item $tempDir -Recurse -Force
    } 
    else {
        Write-LogDebug "Detected uncompressed JSON backup file." -Category "DataLoad"
        $backupContent = Get-Content -Path $BackupFilePath -Raw | ConvertFrom-Json
    }
    
    Write-LogSuccess "Backup file loaded successfully." -Category "DataLoad"
    Write-LogInfo "Backup Source: $($backupContent.BackupInfo.Source)" -Category "BackupInfo"
    Write-LogInfo "Backup Timestamp: $($backupContent.BackupInfo.Timestamp)" -Category "BackupInfo"
    Write-LogInfo "Backup Scope: $($backupContent.BackupInfo.BackupScope)" -Category "BackupInfo"
    Write-LogInfo "VMs in Backup: $($backupContent.VMs.Count)" -Category "BackupInfo"
    
    if ($ValidateOnly) {
        Write-LogInfo "Running in VALIDATION ONLY mode." -Category "OperationMode"
        Write-LogInfo "--- VM Configuration Summary ---" -Category "Summary"
        foreach ($vmConfig in $backupContent.VMs) {
            Write-LogInfo "  - VM: $($vmConfig.Name) | Host: $($vmConfig.VMHost) | CPUs: $($vmConfig.NumCpu) | Memory: $($vmConfig.MemoryMB) MB" -Category "Summary"
        }
        Write-LogInfo "--- End of Summary ---" -Category "Summary"
        
        $scriptSuccess = $true
        $finalSummary = "Validation completed successfully for $($backupContent.VMs.Count) VMs. No changes were made."
    }
    else {
        Write-LogWarning "Restore functionality not yet implemented." -Category "OperationMode"
        # Placeholder for future restore logic
        $scriptSuccess = $true # Mark as success as it's not an error
        $finalSummary = "Restore mode selected, but functionality is not yet implemented. No changes were made."
    }
}
catch {
    $scriptSuccess = $false
    $finalSummary = "VM restore failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    exit 1
}
finally {
    $finalStats = @{
        "BackupFile" = $BackupFilePath
        "ValidationOnly" = $ValidateOnly
        "VMsFoundInBackup" = if($backupContent) { $backupContent.VMs.Count } else { 0 }
    }
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}