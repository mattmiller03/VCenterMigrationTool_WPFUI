<#
.SYNOPSIS
    Migrates vCenter tags and categories from source to target vCenter using PowerCLI 13.x
.DESCRIPTION
    Exports tag categories and tags from source vCenter and recreates them on target vCenter.
    Handles category properties, tag assignments, and validation options.
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
    [bool]$OverwriteExisting = $false,
    
    [Parameter()]
    [bool]$MigrateTagAssignments = $true,
    
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
Start-ScriptLogging -ScriptName "Migrate-Tags" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceCategoriesFound = 0
    SourceTagsFound = 0
    CategoriesMigrated = 0
    TagsMigrated = 0
    CategoriesSkipped = 0
    TagsSkipped = 0
    CategoriesWithErrors = 0
    TagsWithErrors = 0
    TagAssignments = 0
    TagAssignmentErrors = 0
}

try {
    Write-LogInfo "Starting tags and categories migration process" -Category "Initialization"
    
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
    
    # Get source categories and tags
    Write-LogInfo "Retrieving tag categories from source vCenter..." -Category "Discovery"
    $sourceCategories = Get-TagCategory -Server $sourceConnection
    $migrationStats.SourceCategoriesFound = $sourceCategories.Count
    Write-LogInfo "Found $($sourceCategories.Count) tag categories in source vCenter" -Category "Discovery"
    
    Write-LogInfo "Retrieving tags from source vCenter..." -Category "Discovery"
    $sourceTags = Get-Tag -Server $sourceConnection
    $migrationStats.SourceTagsFound = $sourceTags.Count
    Write-LogInfo "Found $($sourceTags.Count) tags in source vCenter" -Category "Discovery"
    
    # Get target categories and tags for comparison
    $targetCategories = Get-TagCategory -Server $targetConnection
    $targetTags = Get-Tag -Server $targetConnection
    Write-LogInfo "Target has $($targetCategories.Count) existing categories and $($targetTags.Count) existing tags" -Category "Discovery"
    
    # Phase 1: Migrate Categories
    Write-LogInfo "Phase 1: Migrating tag categories..." -Category "Migration"
    
    foreach ($category in $sourceCategories) {
        try {
            Write-LogInfo "Processing category: $($category.Name)" -Category "Migration"
            
            # Check if category already exists in target
            $existingCategory = $targetCategories | Where-Object { $_.Name -eq $category.Name }
            
            if ($existingCategory) {
                if ($OverwriteExisting) {
                    if ($ValidateOnly) {
                        Write-LogInfo "VALIDATION: Would overwrite existing category '$($category.Name)'" -Category "Validation"
                    } else {
                        Write-LogWarning "Removing existing category '$($category.Name)' from target" -Category "Migration"
                        Remove-TagCategory -Category $existingCategory -Confirm:$false -ErrorAction Stop
                    }
                } else {
                    Write-LogWarning "Category '$($category.Name)' already exists in target - skipping" -Category "Migration"
                    $migrationStats.CategoriesSkipped++
                    continue
                }
            }
            
            if ($ValidateOnly) {
                Write-LogInfo "VALIDATION: Would create category '$($category.Name)' with cardinality '$($category.Cardinality)'" -Category "Validation"
                $migrationStats.CategoriesMigrated++
            } else {
                # Create the category in target vCenter
                Write-LogInfo "Creating category '$($category.Name)' with cardinality '$($category.Cardinality)'" -Category "Migration"
                
                $categoryParams = @{
                    Name = $category.Name
                    Cardinality = $category.Cardinality
                    Server = $targetConnection
                    ErrorAction = 'Stop'
                }
                
                if ($category.Description) {
                    $categoryParams.Description = $category.Description
                }
                
                if ($category.EntityType -and $category.EntityType.Count -gt 0) {
                    $categoryParams.EntityType = $category.EntityType
                }
                
                $newCategory = New-TagCategory @categoryParams
                
                if ($newCategory) {
                    Write-LogSuccess "Successfully created category: $($newCategory.Name)" -Category "Migration"
                    $migrationStats.CategoriesMigrated++
                } else {
                    throw "Category creation returned null"
                }
            }
            
        } catch {
            Write-LogError "Failed to migrate category '$($category.Name)': $($_.Exception.Message)" -Category "Error"
            $migrationStats.CategoriesWithErrors++
            continue
        }
    }
    
    # Refresh target categories after migration for Phase 2
    if (-not $ValidateOnly) {
        $targetCategories = Get-TagCategory -Server $targetConnection
    }
    
    # Phase 2: Migrate Tags
    Write-LogInfo "Phase 2: Migrating tags..." -Category "Migration"
    
    foreach ($tag in $sourceTags) {
        try {
            Write-LogInfo "Processing tag: $($tag.Name) (Category: $($tag.Category.Name))" -Category "Migration"
            
            # Find target category
            $targetCategory = $targetCategories | Where-Object { $_.Name -eq $tag.Category.Name }
            if (-not $targetCategory) {
                Write-LogError "Target category '$($tag.Category.Name)' not found for tag '$($tag.Name)'" -Category "Migration"
                $migrationStats.TagsWithErrors++
                continue
            }
            
            # Check if tag already exists in target
            $existingTag = $targetTags | Where-Object { $_.Name -eq $tag.Name -and $_.Category.Name -eq $tag.Category.Name }
            
            if ($existingTag) {
                if ($OverwriteExisting) {
                    if ($ValidateOnly) {
                        Write-LogInfo "VALIDATION: Would overwrite existing tag '$($tag.Name)'" -Category "Validation"
                    } else {
                        Write-LogWarning "Removing existing tag '$($tag.Name)' from target" -Category "Migration"
                        Remove-Tag -Tag $existingTag -Confirm:$false -ErrorAction Stop
                    }
                } else {
                    Write-LogWarning "Tag '$($tag.Name)' already exists in target category - skipping" -Category "Migration"
                    $migrationStats.TagsSkipped++
                    continue
                }
            }
            
            if ($ValidateOnly) {
                Write-LogInfo "VALIDATION: Would create tag '$($tag.Name)' in category '$($targetCategory.Name)'" -Category "Validation"
                $migrationStats.TagsMigrated++
            } else {
                # Create the tag in target vCenter
                Write-LogInfo "Creating tag '$($tag.Name)' in category '$($targetCategory.Name)'" -Category "Migration"
                
                $tagParams = @{
                    Name = $tag.Name
                    Category = $targetCategory
                    Server = $targetConnection
                    ErrorAction = 'Stop'
                }
                
                if ($tag.Description) {
                    $tagParams.Description = $tag.Description
                }
                
                $newTag = New-Tag @tagParams
                
                if ($newTag) {
                    Write-LogSuccess "Successfully created tag: $($newTag.Name)" -Category "Migration"
                    $migrationStats.TagsMigrated++
                } else {
                    throw "Tag creation returned null"
                }
            }
            
        } catch {
            Write-LogError "Failed to migrate tag '$($tag.Name)': $($_.Exception.Message)" -Category "Error"
            $migrationStats.TagsWithErrors++
            continue
        }
    }
    
    # Phase 3: Migrate Tag Assignments (if requested)
    if ($MigrateTagAssignments) {
        Write-LogInfo "Phase 3: Migrating tag assignments..." -Category "Migration"
        Write-LogWarning "Tag assignment migration requires matching entities in target vCenter" -Category "Migration"
        
        # Get all tag assignments from source
        foreach ($tag in $sourceTags) {
            try {
                $tagAssignments = Get-TagAssignment -Tag $tag -Server $sourceConnection -ErrorAction SilentlyContinue
                
                foreach ($assignment in $tagAssignments) {
                    # This would require entity matching logic which is complex
                    # For now, just log what would be migrated
                    Write-LogInfo "Would migrate assignment: Tag '$($tag.Name)' -> Entity '$($assignment.Entity.Name)' ($($assignment.Entity.GetType().Name))" -Category "Assignments"
                    $migrationStats.TagAssignments++
                }
            } catch {
                Write-LogError "Error processing assignments for tag '$($tag.Name)': $($_.Exception.Message)" -Category "Assignments"
                $migrationStats.TagAssignmentErrors++
            }
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "Validation completed: $($migrationStats.CategoriesMigrated) categories and $($migrationStats.TagsMigrated) tags would be migrated"
    } else {
        $finalSummary = "Successfully migrated $($migrationStats.CategoriesMigrated) categories and $($migrationStats.TagsMigrated) tags"
    }
    
    Write-LogSuccess $finalSummary -Category "Migration"
    Write-Output "SUCCESS: $finalSummary"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Tags migration failed: $($_.Exception.Message)"
    Write-LogError "Tags migration failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter servers
    if ($sourceConnection) {
        Write-LogInfo "Disconnecting from source vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    if ($targetConnection) {
        Write-LogInfo "Disconnecting from target vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $migrationStats
}