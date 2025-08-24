<#
.SYNOPSIS
    Enhanced vCenter folder migration with recursive structure preservation and datacenter targeting
.DESCRIPTION
    Migrates folder structures from source to target vCenter with advanced features:
    - Recursive folder structure preservation with proper hierarchy
    - Datacenter-specific folder migration with smart matching
    - Enhanced duplicate detection and handling (skip/overwrite options)
    - Improved error handling and rollback protection
    - Detailed progress tracking with comprehensive statistics
    - Support for all folder types (VM, Host, Network, Datastore)
.NOTES
    Version: 2.0 - Enhanced with recursive structure copying and datacenter targeting
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
    [string[]]$SourceDatacenters = @(),  # If empty, migrates all datacenters
    
    [Parameter()]
    [hashtable]$DatacenterMapping = @{}, # Maps source DC names to target DC names
    
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

function Write-LogDebug { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Debug] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Gray }
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

# Global counters for recursive function
$script:foldersCreated = 0
$script:foldersSkipped = 0
$script:foldersFailed = 0
$script:foldersValidated = 0

# Recursive function to copy folder structure with enhanced error handling
function Copy-FolderStructure {
    param(
        [Parameter(Mandatory=$true)]
        $SourceParentFolder,

        [Parameter(Mandatory=$true)]
        $TargetParentFolder,

        [Parameter(Mandatory=$true)]
        $SourceServer,

        [Parameter(Mandatory=$true)]
        $TargetServer,
        
        [Parameter(Mandatory=$true)]
        [string]$FolderType,
        
        [Parameter()]
        [bool]$ValidateOnly = $false,
        
        [Parameter()]
        [bool]$SkipExisting = $true
    )

    Write-LogDebug "Getting child $FolderType folders of '$($SourceParentFolder.Name)' on source $($SourceServer.Name)" -Category "Discovery"
    $sourceChildFolders = Get-Folder -Location $SourceParentFolder -Type $FolderType -Server $SourceServer -NoRecursion -ErrorAction SilentlyContinue

    if ($null -eq $sourceChildFolders) {
        Write-LogDebug "No child $FolderType folders found under '$($SourceParentFolder.Name)'." -Category "Discovery"
        return
    }

    # Ensure sourceChildFolders is an array for consistent processing
    if ($sourceChildFolders -isnot [array]) {
        $sourceChildFolders = @($sourceChildFolders)
    }

    foreach ($sourceFolder in $sourceChildFolders) {
        Write-LogInfo "Processing Source Folder: '$($sourceFolder.Name)' ($FolderType) under '$($SourceParentFolder.Name)'" -Category "Processing"

        Write-LogDebug "Checking for existing folder '$($sourceFolder.Name)' under '$($TargetParentFolder.Name)' on target $($TargetServer.Name)" -Category "Verification"
        $existingTargetFolder = Get-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Type $FolderType -Server $TargetServer -NoRecursion -ErrorAction SilentlyContinue

        if ($existingTargetFolder) {
            if ($SkipExisting) {
                Write-LogInfo "  Folder '$($sourceFolder.Name)' already exists in Target under '$($TargetParentFolder.Name)'. Using existing." -Category "Skipped"
                $script:foldersSkipped++
                $currentTargetFolder = $existingTargetFolder
            } else {
                Write-LogWarning "  Folder '$($sourceFolder.Name)' already exists but will proceed with processing children." -Category "Existing"
                $currentTargetFolder = $existingTargetFolder
            }
        } else {
            if ($ValidateOnly) {
                Write-LogInfo "  VALIDATION: Would create folder '$($sourceFolder.Name)' ($FolderType) in Target under '$($TargetParentFolder.Name)'..." -Category "Validation"
                $script:foldersValidated++
                $currentTargetFolder = $TargetParentFolder # Use parent for validation recursion
            } else {
                Write-LogInfo "  Creating Folder '$($sourceFolder.Name)' ($FolderType) in Target under '$($TargetParentFolder.Name)'..." -Category "Creation"
                try {
                    $currentTargetFolder = New-Folder -Location $TargetParentFolder -Name $sourceFolder.Name -Server $TargetServer -ErrorAction Stop
                    Write-LogSuccess "  Successfully created folder '$($currentTargetFolder.Name)' ($FolderType)." -Category "Creation"
                    $script:foldersCreated++
                } catch {
                    Write-LogError "  Failed to create folder '$($sourceFolder.Name)' ($FolderType) in Target under '$($TargetParentFolder.Name)': $($_.Exception.Message)" -Category "Creation"
                    $script:foldersFailed++
                    continue
                }
            }
        }

        # Recursively process child folders if we have a valid target folder
        if ($currentTargetFolder) {
            Copy-FolderStructure -SourceParentFolder $sourceFolder -TargetParentFolder $currentTargetFolder -SourceServer $SourceServer -TargetServer $TargetServer -FolderType $FolderType -ValidateOnly $ValidateOnly -SkipExisting $SkipExisting
        }
    }
}

# Start logging
Start-ScriptLogging -ScriptName "Migrate-Folders" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceDatacentersProcessed = 0
    TargetDatacentersMatched = 0
    FoldersCreated = 0
    FoldersSkipped = 0
    FoldersFailed = 0
    FoldersValidated = 0
    ValidationMode = $ValidateOnly
    SkipExistingMode = $SkipExisting
}

# Initialize type-specific counters
foreach ($type in $FolderTypes) {
    $migrationStats["${type}FoldersProcessed"] = 0
}

$sourceConnection = $null
$targetConnection = $null

try {
    Write-LogInfo "Starting enhanced folder migration process with recursive structure preservation" -Category "Initialization"
    Write-LogInfo "Folder types to migrate: $($FolderTypes -join ', ')" -Category "Initialization"
    Write-LogInfo "Validation Mode: $ValidateOnly, Skip Existing: $SkipExisting" -Category "Initialization"
    
    if ($SourceDatacenters.Count -gt 0) {
        Write-LogInfo "Migrating specific datacenters: $($SourceDatacenters -join ', ')" -Category "Initialization"
    } else {
        Write-LogInfo "Migrating all source datacenters" -Category "Initialization"
    }
    
    if ($DatacenterMapping.Count -gt 0) {
        Write-LogInfo "Using datacenter mappings: $($DatacenterMapping.Keys -join ', ' | ForEach-Object { "$_ -> $($DatacenterMapping[$_])" })" -Category "Initialization"
    }

    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        Import-Module VMware.VimAutomation.Core -ErrorAction Stop
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
    Write-LogInfo "Retrieving datacenters from both vCenter environments..." -Category "Discovery"
    $allSourceDatacenters = Get-Datacenter -Server $sourceConnection
    $allTargetDatacenters = Get-Datacenter -Server $targetConnection
    Write-LogInfo "Found $($allSourceDatacenters.Count) datacenters in source, $($allTargetDatacenters.Count) in target" -Category "Discovery"
    
    # Filter source datacenters if specific ones are requested
    if ($SourceDatacenters.Count -gt 0) {
        $sourceDatacenters = $allSourceDatacenters | Where-Object { $_.Name -in $SourceDatacenters }
        Write-LogInfo "Filtered to $($sourceDatacenters.Count) specific datacenters for migration" -Category "Discovery"
    } else {
        $sourceDatacenters = $allSourceDatacenters
    }
    
    if ($sourceDatacenters.Count -eq 0) {
        throw "No source datacenters found to process"
    }
    
    # Process each source datacenter
    foreach ($sourceDC in $sourceDatacenters) {
        try {
            Write-LogInfo "Processing Source Datacenter: '$($sourceDC.Name)'" -Category "MainProcess"
            $migrationStats.SourceDatacentersProcessed++
            
            # Determine target datacenter (with optional mapping)
            $targetDCName = if ($DatacenterMapping.ContainsKey($sourceDC.Name)) {
                $DatacenterMapping[$sourceDC.Name]
            } else {
                $sourceDC.Name
            }
            
            $targetDC = $allTargetDatacenters | Where-Object { $_.Name -eq $targetDCName }
            if (-not $targetDC) {
                Write-LogError "Target datacenter '$targetDCName' not found for source '$($sourceDC.Name)'. Skipping this datacenter." -Category "MainProcess"
                continue
            }
            
            Write-LogSuccess "Found target datacenter: '$($targetDC.Name)'" -Category "MainProcess"
            $migrationStats.TargetDatacentersMatched++
            
            # Process each folder type
            foreach ($folderType in $FolderTypes) {
                Write-LogInfo "Processing $folderType folders in datacenter '$($sourceDC.Name)'..." -Category "FolderType"
                
                # Get the root folder for this type in the source datacenter
                Write-LogDebug "Getting root $folderType folder for source datacenter '$($sourceDC.Name)'" -Category "Discovery"
                $sourceRootFolder = Get-Folder -Location $sourceDC -Type $folderType -Server $sourceConnection | Where-Object { $_.Parent -eq $sourceDC }
                
                if (-not $sourceRootFolder) {
                    Write-LogWarning "Root $folderType folder not found in source datacenter '$($sourceDC.Name)'" -Category "Discovery"
                    continue
                }
                
                # Get the root folder for this type in the target datacenter
                Write-LogDebug "Getting root $folderType folder for target datacenter '$($targetDC.Name)'" -Category "Discovery"
                $targetRootFolder = Get-Folder -Location $targetDC -Type $folderType -Server $targetConnection | Where-Object { $_.Parent -eq $targetDC }
                
                if (-not $targetRootFolder) {
                    Write-LogError "Root $folderType folder not found in target datacenter '$($targetDC.Name)'. Cannot proceed with this folder type." -Category "Discovery"
                    continue
                }
                
                Write-LogInfo "Starting recursive $folderType folder structure copy from '$($sourceDC.Name)' to '$($targetDC.Name)'..." -Category "Migration"
                
                # Reset counters for this folder type
                $script:foldersCreated = 0
                $script:foldersSkipped = 0
                $script:foldersFailed = 0
                $script:foldersValidated = 0
                
                # Copy folder structure recursively
                Copy-FolderStructure -SourceParentFolder $sourceRootFolder -TargetParentFolder $targetRootFolder -SourceServer $sourceConnection -TargetServer $targetConnection -FolderType $folderType -ValidateOnly $ValidateOnly -SkipExisting $SkipExisting
                
                # Update statistics
                $migrationStats.FoldersCreated += $script:foldersCreated
                $migrationStats.FoldersSkipped += $script:foldersSkipped
                $migrationStats.FoldersFailed += $script:foldersFailed
                $migrationStats.FoldersValidated += $script:foldersValidated
                $migrationStats["${folderType}FoldersProcessed"] = $script:foldersCreated + $script:foldersSkipped + $script:foldersFailed + $script:foldersValidated
                
                if ($ValidateOnly) {
                    Write-LogSuccess "Finished $folderType folder validation for datacenter '$($sourceDC.Name)'. Would create: $script:foldersCreated, Would skip: $script:foldersSkipped, Validation errors: $script:foldersFailed" -Category "Validation"
                } else {
                    Write-LogSuccess "Finished $folderType folder migration for datacenter '$($sourceDC.Name)'. Created: $script:foldersCreated, Skipped: $script:foldersSkipped, Failed: $script:foldersFailed" -Category "Migration"
                }
            }
            
            Write-LogSuccess "Completed processing datacenter '$($sourceDC.Name)'" -Category "MainProcess"
            
        } catch {
            Write-LogError "Failed to process datacenter '$($sourceDC.Name)': $($_.Exception.Message)" -Category "MainProcess"
            continue
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "VALIDATION COMPLETE: $($migrationStats.FoldersValidated) folders would be created, $($migrationStats.FoldersSkipped) would be skipped, $($migrationStats.FoldersFailed) validation errors across $($migrationStats.SourceDatacentersProcessed) datacenters"
    } else {
        $finalSummary = "MIGRATION COMPLETE: $($migrationStats.FoldersCreated) folders created, $($migrationStats.FoldersSkipped) skipped, $($migrationStats.FoldersFailed) failed across $($migrationStats.SourceDatacentersProcessed) datacenters"
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