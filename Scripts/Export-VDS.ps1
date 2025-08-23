<#
.SYNOPSIS
    Exports Virtual Distributed Switch (vDS) using PowerCLI 13.x native Export-VDSwitch cmdlet
.DESCRIPTION
    Connects to vCenter and exports all vDS switches using the native PowerCLI
    Export-VDSwitch cmdlet for complete configuration backup.
.NOTES
    Version: 2.0 - Using native PowerCLI Export-VDSwitch cmdlet
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
    [Parameter(Mandatory=$true)]
    [string]$ExportPath,
    
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
Start-ScriptLogging -ScriptName "Export-VDS" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$exportStats = @{
    SwitchesExported = 0
    BackupFilesCreated = 0
    Errors = 0
}

try {
    Write-LogInfo "Starting vDS export process using native Export-VDSwitch cmdlet" -Category "Initialization"
    
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
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Get all distributed virtual switches
    Write-LogInfo "Retrieving Virtual Distributed Switches..." -Category "Discovery"
    $vdSwitches = Get-VDSwitch -ErrorAction SilentlyContinue
    
    if (-not $vdSwitches) {
        Write-LogWarning "No Virtual Distributed Switches found in vCenter" -Category "Discovery"
        $finalSummary = "No vDS switches found to export"
        Write-Output "SUCCESS: $finalSummary"
    }
    else {
        Write-LogInfo "Found $($vdSwitches.Count) Virtual Distributed Switches" -Category "Discovery"
        
        # Ensure export directory exists
        $exportDir = Split-Path -Parent $ExportPath
        if (-not (Test-Path $exportDir)) {
            New-Item -ItemType Directory -Path $exportDir -Force | Out-Null
            Write-LogInfo "Created export directory: $exportDir" -Category "Export"
        }
        
        # Create a master export directory for all VDS backups
        $exportBaseName = [System.IO.Path]::GetFileNameWithoutExtension($ExportPath)
        $vdsExportDir = Join-Path $exportDir $exportBaseName
        if (-not (Test-Path $vdsExportDir)) {
            New-Item -ItemType Directory -Path $vdsExportDir -Force | Out-Null
            Write-LogInfo "Created VDS export directory: $vdsExportDir" -Category "Export"
        }
        
        # Export each vDS using native Export-VDSwitch cmdlet
        $exportedSwitches = @()
        foreach ($vds in $vdSwitches) {
            try {
                Write-LogInfo "Exporting vDS: $($vds.Name)" -Category "Export"
                
                # Create backup file path for this vDS
                $vdsBackupFile = Join-Path $vdsExportDir "$($vds.Name)_backup.zip"
                
                # Use native Export-VDSwitch cmdlet
                Export-VDSwitch -VDSwitch $vds -Destination $vdsBackupFile -ErrorAction Stop
                
                if (Test-Path $vdsBackupFile) {
                    $fileSize = (Get-Item $vdsBackupFile).Length
                    Write-LogSuccess "Exported vDS '$($vds.Name)' to $vdsBackupFile (Size: $($fileSize / 1KB) KB)" -Category "Export"
                    
                    # Add to exported switches list
                    $exportedSwitches += @{
                        Name = $vds.Name
                        BackupFile = $vdsBackupFile
                        Size = $fileSize
                        Uuid = $vds.ExtensionData.Uuid
                        Version = $vds.Version
                        NumPorts = $vds.NumPorts
                        PortGroupCount = (Get-VDPortgroup -VDSwitch $vds | Where-Object { -not $_.IsUplink }).Count
                    }
                    
                    $exportStats.SwitchesExported++
                    $exportStats.BackupFilesCreated++
                }
                else {
                    throw "Backup file was not created: $vdsBackupFile"
                }
                
            } catch {
                Write-LogError "Failed to export vDS '$($vds.Name)': $($_.Exception.Message)" -Category "Error"
                $exportStats.Errors++
            }
        }
        
        # Create summary manifest file
        $manifestFile = Join-Path $vdsExportDir "export_manifest.json"
        $manifest = @{
            ExportDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            SourceVCenter = $VCenterServer
            ExportMethod = "Native Export-VDSwitch"
            TotalSwitches = $vdSwitches.Count
            SuccessfulExports = $exportStats.SwitchesExported
            FailedExports = $exportStats.Errors
            ExportedSwitches = $exportedSwitches
        }
        
        $manifest | ConvertTo-Json -Depth 5 | Out-File -FilePath $manifestFile -Encoding UTF8
        Write-LogInfo "Created export manifest: $manifestFile" -Category "Export"
        
        # Create main export path as a reference file
        $exportReference = @{
            ExportType = "VDS_Native_Backup"
            ExportDirectory = $vdsExportDir
            ManifestFile = $manifestFile
            TotalSwitches = $exportStats.SwitchesExported
            ExportDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        }
        $exportReference | ConvertTo-Json | Out-File -FilePath $ExportPath -Encoding UTF8
        
        $scriptSuccess = ($exportStats.Errors -eq 0)
        if ($scriptSuccess) {
            $finalSummary = "Successfully exported $($exportStats.SwitchesExported) vDS switches using native PowerCLI backup"
            Write-Output "SUCCESS: Exported $($exportStats.SwitchesExported) vDS switches to $vdsExportDir"
        }
        else {
            $finalSummary = "Exported $($exportStats.SwitchesExported) vDS switches with $($exportStats.Errors) errors"
            Write-Output "WARNING: $finalSummary"
        }
    }
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Export failed: $($_.Exception.Message)"
    Write-LogError "Export failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary
}