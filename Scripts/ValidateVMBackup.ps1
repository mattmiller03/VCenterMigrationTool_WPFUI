<#
.SYNOPSIS
    Validates VM backup files in a specified location.
.DESCRIPTION
    Scans a directory for VM backup files (.json or .zip), reads their metadata,
    and logs a summary of each file found.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupLocation,

    [Parameter(Mandatory = $false)]
    [string]$LogPath,

    [Parameter(Mandatory = $false)]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "ValidateVMBackup" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$filesFound = 0
$filesReadable = 0

try {
    Write-LogInfo "Validating VM backup files in: $BackupLocation" -Category "Initialization"
    
    if (-not (Test-Path $BackupLocation)) {
        throw "Backup location does not exist: $BackupLocation"
    }
    
    # Find backup files
    $backupFiles = Get-ChildItem -Path $BackupLocation -Filter "VM_Backup_*.json" -ErrorAction SilentlyContinue
    $backupFiles += Get-ChildItem -Path $BackupLocation -Filter "VM_Backup_*.zip" -ErrorAction SilentlyContinue
    
    $filesFound = $backupFiles.Count
    if ($filesFound -eq 0) {
        Write-LogWarning "No VM backup files found matching 'VM_Backup_*.json' or 'VM_Backup_*.zip'." -Category "Discovery"
        $scriptSuccess = $true # Not an error, just no files
        $finalSummary = "No backup files found to validate."
    }
    else {
        Write-LogSuccess "Found $filesFound backup file(s)." -Category "Discovery"
        $backupFiles = $backupFiles | Sort-Object CreationTime -Descending
        
        foreach ($file in $backupFiles) {
            $fileInfo = @{
                Name = $file.Name
                SizeKB = [math]::Round($file.Length / 1KB, 0)
                Created = $file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
                Type = if ($file.Extension -eq ".zip") { "Compressed" } else { "JSON" }
            }
            Write-LogInfo "Validating File: $($fileInfo.Name) (Size: $($fileInfo.SizeKB) KB, Created: $($fileInfo.Created))" -Category "Validation"
            
            try {
                $content = $null
                if ($file.Extension -eq ".zip") {
                    Add-Type -AssemblyName System.IO.Compression.FileSystem
                    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
                    $jsonEntry = $zip.Entries | Where-Object { $_.Name.EndsWith('.json') } | Select-Object -First 1
                    if ($jsonEntry) {
                        $streamReader = New-Object System.IO.StreamReader($jsonEntry.Open())
                        $content = $streamReader.ReadToEnd() | ConvertFrom-Json
                        $streamReader.Close()
                    }
                    $zip.Dispose()
                } else {
                    $content = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json
                }
                
                if ($content) {
                    Write-LogSuccess "  -> Status: Readable. VMs: $($content.VMs.Count), Source: $($content.BackupInfo.Source)" -Category "Validation"
                    $filesReadable++
                } else {
                    throw "Could not extract valid JSON content."
                }
            } catch {
                Write-LogWarning "  -> Status: Could not read backup content. Error: $($_.Exception.Message)" -Category "Validation"
            }
        }
        
        $scriptSuccess = $true
        $finalSummary = "Validation completed. Found $filesFound file(s), of which $filesReadable were readable."
    }
}
catch {
    $scriptSuccess = $false
    $finalSummary = "Backup validation failed with a critical error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    exit 1
}
finally {
    $finalStats = @{
        "BackupLocation" = $BackupLocation
        "BackupFilesFound" = $filesFound
        "ReadableBackups" = $filesReadable
    }
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}