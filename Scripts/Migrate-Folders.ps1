<#
.SYNOPSIS
    Migrates vCenter folders from source to target vCenter using PowerCLI 13.x
.DESCRIPTION
    Recreates folder structure from source vCenter to target vCenter.
    Handles nested folder hierarchies, different folder types (VM, Host, Datacenter, etc.) with validation options.
.NOTES
    Version: 1.0 - PowerCLI 13.x optimized
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$SourceCredentials,
    
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$TargetCredentials,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetVCenterServer,
    
    [Parameter()]
    [bool]$ValidateOnly = $false,
    
    [Parameter()]
    [bool]$SkipExisting = $true,
    
    [Parameter()]
    [string[]]$FolderTypes = @("VM", "Host", "Network", "Datastore"),
    
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

function Get-FolderPath {
    param($Folder)
    
    $path = @()
    $current = $Folder
    
    # Walk up the hierarchy until we reach a datacenter
    while ($current -and $current.GetType().Name -ne "Datacenter") {
        $path = @($current.Name) + $path
        $current = $current.Parent
    }
    
    return ($path -join "/")
}

function Find-TargetParent {
    param($SourceFolder, $TargetDatacenters)
    
    # Find the source datacenter
    $sourceParent = $SourceFolder.Parent
    while ($sourceParent -and $sourceParent.GetType().Name -ne "Datacenter") {
        $sourceParent = $sourceParent.Parent
    }
    
    if ($sourceParent) {
        # Find matching datacenter in target
        $targetDC = $TargetDatacenters | Where-Object { $_.Name -eq $sourceParent.Name }
        
        if ($targetDC) {
            # Navigate to the correct parent folder in target
            $targetParent = $targetDC
            
            # Get the folder path from source
            $pathElements = @()
            $current = $SourceFolder.Parent
            
            while ($current -and $current.GetType().Name -ne "Datacenter") {
                $pathElements = @($current.Name) + $pathElements
                $current = $current.Parent
            }
            
            # Navigate the path in target
            foreach ($pathElement in $pathElements) {
                $childFolder = Get-Folder -Name $pathElement -Location $targetParent -Type $SourceFolder.Type -ErrorAction SilentlyContinue
                if ($childFolder) {
                    $targetParent = $childFolder
                } else {
                    # Parent folder doesn't exist, need to create it first
                    return $null
                }
            }
            
            return $targetParent
        }
    }
    
    return $null
}

# Start logging
Start-ScriptLogging -ScriptName "Migrate-Folders" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceFoldersFound = 0
    FoldersMigrated = 0
    FoldersSkipped = 0
    FoldersWithErrors = 0
    MissingParents = 0
}

# Initialize type-specific counters
foreach ($type in $FolderTypes) {
    $migrationStats["${type}FoldersFound"] = 0
    $migrationStats["${type}FoldersMigrated"] = 0
}

try {
    Write-LogInfo "Starting folder migration process" -Category "Initialization"
    Write-LogInfo "Folder types to migrate: $($FolderTypes -join ', ')" -Category "Initialization"
    
    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
        Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
    }
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to source vCenter
    Write-LogInfo "Connecting to source vCenter: $SourceVCenterServer" -Category "Connection"
    $sourceConnection = Connect-VIServer -Server $SourceVCenterServer -Credential $SourceCredentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to source vCenter: $($sourceConnection.Name) (v$($sourceConnection.Version))" -Category "Connection"
    
    # Connect to target vCenter
    Write-LogInfo "Connecting to target vCenter: $TargetVCenterServer" -Category "Connection"
    $targetConnection = Connect-VIServer -Server $TargetVCenterServer -Credential $TargetCredentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to target vCenter: $($targetConnection.Name) (v$($targetConnection.Version))" -Category "Connection"
    
    # Get datacenters from both environments
    $sourceDatacenters = Get-Datacenter -Server $sourceConnection
    $targetDatacenters = Get-Datacenter -Server $targetConnection
    Write-LogInfo "Found $($sourceDatacenters.Count) datacenters in source, $($targetDatacenters.Count) in target" -Category "Discovery"
    
    # Get source folders by type
    $allSourceFolders = @()
    foreach ($folderType in $FolderTypes) {
        $foldersOfType = Get-Folder -Type $folderType -Server $sourceConnection | Where-Object { $_.Parent.GetType().Name -ne "Datacenter" }
        $allSourceFolders += $foldersOfType
        $migrationStats["${folderType}FoldersFound"] = $foldersOfType.Count
        Write-LogInfo "Found $($foldersOfType.Count) $folderType folders in source" -Category "Discovery"
    }
    
    $migrationStats.SourceFoldersFound = $allSourceFolders.Count
    Write-LogInfo "Total folders found: $($allSourceFolders.Count)" -Category "Discovery"
    
    # Sort folders by depth (shallow to deep) to ensure parent folders are created first
    $sortedFolders = $allSourceFolders | Sort-Object { (Get-FolderPath $_).Split('/').Count }
    
    if ($sortedFolders.Count -eq 0) {
        Write-LogWarning "No custom folders found to migrate" -Category "Migration"
    } else {
        # Process each folder in depth order
        foreach ($folder in $sortedFolders) {
            try {
                $folderPath = Get-FolderPath $folder
                Write-LogInfo "Processing folder: $($folder.Name) ($($folder.Type)) - Path: $folderPath" -Category "Migration"
                
                # Find target datacenter
                $sourceDatacenter = $folder
                while ($sourceDatacenter -and $sourceDatacenter.GetType().Name -ne "Datacenter") {
                    $sourceDatacenter = $sourceDatacenter.Parent
                }
                
                $targetDatacenter = $targetDatacenters | Where-Object { $_.Name -eq $sourceDatacenter.Name }
                if (-not $targetDatacenter) {
                    Write-LogError "Target datacenter '$($sourceDatacenter.Name)' not found" -Category "Migration"
                    $migrationStats.FoldersWithErrors++
                    continue
                }
                
                # Find the correct parent location in target
                $targetParentLocation = $targetDatacenter
                
                # Navigate to parent folder if not directly under datacenter
                if ($folder.Parent.GetType().Name -ne "Datacenter") {
                    $parentPath = Get-FolderPath $folder.Parent
                    $pathElements = $parentPath -split "/"
                    
                    foreach ($pathElement in $pathElements) {
                        $parentFolder = Get-Folder -Name $pathElement -Location $targetParentLocation -Type $folder.Type -ErrorAction SilentlyContinue
                        if ($parentFolder) {
                            $targetParentLocation = $parentFolder
                        } else {
                            Write-LogError "Parent folder '$pathElement' not found in target. Process folders in correct order." -Category "Migration"
                            $migrationStats.MissingParents++
                            $targetParentLocation = $null
                            break
                        }
                    }
                }
                
                if (-not $targetParentLocation) {
                    $migrationStats.FoldersWithErrors++
                    continue
                }
                
                # Check if folder already exists
                $existingFolder = Get-Folder -Name $folder.Name -Location $targetParentLocation -Type $folder.Type -ErrorAction SilentlyContinue
                
                if ($existingFolder) {
                    if ($SkipExisting) {
                        Write-LogInfo "Folder '$($folder.Name)' already exists in target - skipping" -Category "Migration"
                        $migrationStats.FoldersSkipped++
                        continue
                    } else {
                        Write-LogWarning "Folder '$($folder.Name)' already exists in target but will proceed" -Category "Migration"
                    }
                }
                
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION: Would create folder '$($folder.Name)' ($($folder.Type)) in '$($targetParentLocation.Name)'" -Category "Validation"
                    $migrationStats.FoldersMigrated++
                    $migrationStats["$($folder.Type)FoldersMigrated"]++
                } else {
                    # Create the folder in target
                    Write-LogInfo "Creating folder '$($folder.Name)' ($($folder.Type)) in '$($targetParentLocation.Name)'" -Category "Migration"
                    
                    $newFolder = New-Folder -Name $folder.Name -Location $targetParentLocation -Server $targetConnection -ErrorAction Stop
                    
                    if ($newFolder) {
                        Write-LogSuccess "Successfully created folder: $($newFolder.Name)" -Category "Migration"
                        $migrationStats.FoldersMigrated++
                        $migrationStats["$($folder.Type)FoldersMigrated"]++
                    } else {
                        throw "Folder creation returned null"
                    }
                }
                
            } catch {
                Write-LogError "Failed to migrate folder '$($folder.Name)': $($_.Exception.Message)" -Category "Error"
                $migrationStats.FoldersWithErrors++
                continue
            }
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "Validation completed: $($migrationStats.FoldersMigrated) folders would be migrated, $($migrationStats.FoldersSkipped) skipped"
    } else {
        $finalSummary = "Successfully migrated $($migrationStats.FoldersMigrated) folders, $($migrationStats.FoldersSkipped) skipped, $($migrationStats.FoldersWithErrors) errors"
    }
    
    Write-LogSuccess $finalSummary -Category "Migration"
    Write-Output "SUCCESS: $finalSummary"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Folder migration failed: $($_.Exception.Message)"
    Write-LogError "Folder migration failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter servers
    if ($sourceConnection) {
        Write-LogInfo "Disconnecting from source vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $sourceConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    if ($targetConnection) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $targetConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $migrationStats
}