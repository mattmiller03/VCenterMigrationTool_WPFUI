param(
    [Parameter(Mandatory = $true)]
    [string]$BackupLocation
)

try {
    Write-Host "Validating VM backup files in: $BackupLocation"
    
    if (-not (Test-Path $BackupLocation)) {
        throw "Backup location does not exist: $BackupLocation"
    }
    
    # Find backup files
    $backupFiles = @()
    $backupFiles += Get-ChildItem -Path $BackupLocation -Filter "VM_Backup_*.json" -ErrorAction SilentlyContinue
    $backupFiles += Get-ChildItem -Path $BackupLocation -Filter "VM_Backup_*.zip" -ErrorAction SilentlyContinue
    
    if ($backupFiles.Count -eq 0) {
        Write-Host "No VM backup files found"
        Write-Host "Looking for files matching: VM_Backup_*.json or VM_Backup_*.zip"
        exit 0
    }
    
    # Sort by creation time (newest first)
    $backupFiles = $backupFiles | Sort-Object CreationTime -Descending
    
    Write-Host "SUCCESS: Found $($backupFiles.Count) backup file(s)"
    Write-Host ""
    
    foreach ($file in $backupFiles) {
        $fileInfo = @{
            Name = $file.Name
            SizeKB = [math]::Round($file.Length / 1KB, 0)
            Created = $file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss")
            Type = if ($file.Extension -eq ".zip") { "Compressed" } else { "JSON" }
        }
        
        Write-Host "File: $($fileInfo.Name)"
        Write-Host "  Size: $($fileInfo.SizeKB) KB"
        Write-Host "  Created: $($fileInfo.Created)"
        Write-Host "  Type: $($fileInfo.Type)"
        
        # Try to read backup info
        try {
            if ($file.Extension -eq ".zip") {
                # Quick peek into ZIP file
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
                $jsonEntry = $zip.Entries | Where-Object { $_.Name.EndsWith('.json') } | Select-Object -First 1
                
                if ($jsonEntry) {
                    $stream = $jsonEntry.Open()
                    $reader = New-Object System.IO.StreamReader($stream)
                    $content = $reader.ReadToEnd() | ConvertFrom-Json
                    $reader.Close()
                    $stream.Close()
                    
                    Write-Host "  VMs: $($content.VMs.Count)"
                    Write-Host "  Source: $($content.BackupInfo.Source)"
                    Write-Host "  Scope: $($content.BackupInfo.BackupScope)"
                }
                $zip.Dispose()
            }
            else {
                # Read JSON directly
                $content = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json
                Write-Host "  VMs: $($content.VMs.Count)"
                Write-Host "  Source: $($content.BackupInfo.Source)"
                Write-Host "  Scope: $($content.BackupInfo.BackupScope)"
            }
        }
        catch {
            Write-Host "  Status: Could not read backup content"
        }
        
        Write-Host ""
    }
    
    Write-Host "Validation completed successfully"
}
catch {
    Write-Error "Backup validation failed: $($_.Exception.Message)"
    exit 1
}
finally {
    Write-Host "VM backup validation script execution completed."
}