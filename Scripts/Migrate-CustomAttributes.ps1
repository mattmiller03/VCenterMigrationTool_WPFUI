<#
.SYNOPSIS
    Migrates vCenter custom attributes from source to target vCenter using PowerCLI 13.x
.DESCRIPTION
    Exports custom attributes from source vCenter and recreates them on target vCenter.
    Handles attribute types, applicable object types, and value assignments with validation options.
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
    [bool]$MigrateAttributeValues = $true,
    
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
Start-ScriptLogging -ScriptName "Migrate-CustomAttributes" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$migrationStats = @{
    SourceAttributesFound = 0
    AttributesMigrated = 0
    AttributesSkipped = 0
    AttributesWithErrors = 0
    AttributeValuesFound = 0
    AttributeValuesMigrated = 0
    AttributeValuesWithErrors = 0
}

try {
    Write-LogInfo "Starting custom attributes migration process" -Category "Initialization"
    
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
    
    # Get source custom attributes
    Write-LogInfo "Retrieving custom attributes from source vCenter..." -Category "Discovery"
    $sourceAttributes = Get-CustomAttribute -Server $sourceConnection
    $migrationStats.SourceAttributesFound = $sourceAttributes.Count
    Write-LogInfo "Found $($sourceAttributes.Count) custom attributes in source vCenter" -Category "Discovery"
    
    # Get target custom attributes for comparison
    $targetAttributes = Get-CustomAttribute -Server $targetConnection
    Write-LogInfo "Found $($targetAttributes.Count) existing custom attributes in target vCenter" -Category "Discovery"
    
    if ($sourceAttributes.Count -eq 0) {
        Write-LogWarning "No custom attributes found to migrate" -Category "Migration"
    } else {
        # Phase 1: Migrate Custom Attributes
        Write-LogInfo "Phase 1: Migrating custom attribute definitions..." -Category "Migration"
        
        foreach ($attribute in $sourceAttributes) {
            try {
                Write-LogInfo "Processing custom attribute: $($attribute.Name)" -Category "Migration"
                
                # Check if attribute already exists in target
                $existingAttribute = $targetAttributes | Where-Object { $_.Name -eq $attribute.Name }
                
                if ($existingAttribute) {
                    if ($OverwriteExisting) {
                        if ($ValidateOnly) {
                            Write-LogInfo "VALIDATION: Would overwrite existing custom attribute '$($attribute.Name)'" -Category "Validation"
                        } else {
                            Write-LogWarning "Removing existing custom attribute '$($attribute.Name)' from target" -Category "Migration"
                            Remove-CustomAttribute -CustomAttribute $existingAttribute -Confirm:$false -ErrorAction Stop
                        }
                    } else {
                        Write-LogWarning "Custom attribute '$($attribute.Name)' already exists in target - skipping" -Category "Migration"
                        $migrationStats.AttributesSkipped++
                        continue
                    }
                }
                
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION: Would create custom attribute '$($attribute.Name)' for types: $($attribute.TargetType -join ', ')" -Category "Validation"
                    $migrationStats.AttributesMigrated++
                } else {
                    # Create the custom attribute in target vCenter
                    Write-LogInfo "Creating custom attribute '$($attribute.Name)' for types: $($attribute.TargetType -join ', ')" -Category "Migration"
                    
                    $attributeParams = @{
                        Name = $attribute.Name
                        Server = $targetConnection
                        ErrorAction = 'Stop'
                    }
                    
                    # Add target type if specified (empty means global)
                    if ($attribute.TargetType -and $attribute.TargetType.Count -gt 0) {
                        $attributeParams.TargetType = $attribute.TargetType
                    }
                    
                    $newAttribute = New-CustomAttribute @attributeParams
                    
                    if ($newAttribute) {
                        Write-LogSuccess "Successfully created custom attribute: $($newAttribute.Name)" -Category "Migration"
                        $migrationStats.AttributesMigrated++
                    } else {
                        throw "Custom attribute creation returned null"
                    }
                }
                
            } catch {
                Write-LogError "Failed to migrate custom attribute '$($attribute.Name)': $($_.Exception.Message)" -Category "Error"
                $migrationStats.AttributesWithErrors++
                continue
            }
        }
        
        # Phase 2: Migrate Custom Attribute Values (if requested)
        if ($MigrateAttributeValues -and -not $ValidateOnly) {
            Write-LogInfo "Phase 2: Migrating custom attribute values..." -Category "Migration"
            Write-LogWarning "Custom attribute value migration requires matching entities in target vCenter" -Category "Migration"
            
            # Refresh target attributes after creation
            $targetAttributes = Get-CustomAttribute -Server $targetConnection
            
            # Get all entities with custom attributes from source
            $sourceEntities = @()
            $sourceEntities += Get-VM -Server $sourceConnection | Where-Object { $_.CustomFields.Count -gt 0 }
            $sourceEntities += Get-VMHost -Server $sourceConnection | Where-Object { $_.CustomFields.Count -gt 0 }
            $sourceEntities += Get-Datacenter -Server $sourceConnection | Where-Object { $_.CustomFields.Count -gt 0 }
            $sourceEntities += Get-Cluster -Server $sourceConnection | Where-Object { $_.CustomFields.Count -gt 0 }
            $sourceEntities += Get-ResourcePool -Server $sourceConnection | Where-Object { $_.CustomFields.Count -gt 0 }
            
            Write-LogInfo "Found $($sourceEntities.Count) entities with custom attribute values" -Category "Migration"
            $migrationStats.AttributeValuesFound = $sourceEntities.Count
            
            foreach ($sourceEntity in $sourceEntities) {
                try {
                    # Find matching entity in target by name and type
                    $entityType = $sourceEntity.GetType().Name
                    $targetEntity = $null
                    
                    switch ($entityType) {
                        "VirtualMachine" { $targetEntity = Get-VM -Name $sourceEntity.Name -Server $targetConnection -ErrorAction SilentlyContinue }
                        "VMHost" { $targetEntity = Get-VMHost -Name $sourceEntity.Name -Server $targetConnection -ErrorAction SilentlyContinue }
                        "Datacenter" { $targetEntity = Get-Datacenter -Name $sourceEntity.Name -Server $targetConnection -ErrorAction SilentlyContinue }
                        "ClusterComputeResource" { $targetEntity = Get-Cluster -Name $sourceEntity.Name -Server $targetConnection -ErrorAction SilentlyContinue }
                        "ResourcePool" { $targetEntity = Get-ResourcePool -Name $sourceEntity.Name -Server $targetConnection -ErrorAction SilentlyContinue }
                    }
                    
                    if (-not $targetEntity) {
                        Write-LogWarning "Entity '$($sourceEntity.Name)' ($entityType) not found in target - skipping attribute values" -Category "Migration"
                        continue
                    }
                    
                    # Copy custom attribute values
                    foreach ($customField in $sourceEntity.CustomFields) {
                        if (-not [string]::IsNullOrEmpty($customField.Value)) {
                            $targetAttribute = $targetAttributes | Where-Object { $_.Name -eq $customField.Key }
                            
                            if ($targetAttribute) {
                                Write-LogInfo "Setting custom attribute '$($customField.Key)' = '$($customField.Value)' on entity '$($targetEntity.Name)'" -Category "Migration"
                                
                                Set-Annotation -Entity $targetEntity -CustomAttribute $targetAttribute -Value $customField.Value -ErrorAction Stop
                                $migrationStats.AttributeValuesMigrated++
                            } else {
                                Write-LogWarning "Custom attribute '$($customField.Key)' not found in target" -Category "Migration"
                            }
                        }
                    }
                    
                } catch {
                    Write-LogError "Failed to migrate attribute values for entity '$($sourceEntity.Name)': $($_.Exception.Message)" -Category "Error"
                    $migrationStats.AttributeValuesWithErrors++
                    continue
                }
            }
        }
    }
    
    $scriptSuccess = $true
    if ($ValidateOnly) {
        $finalSummary = "Validation completed: $($migrationStats.AttributesMigrated) custom attributes would be migrated, $($migrationStats.AttributesSkipped) skipped"
    } else {
        if ($MigrateAttributeValues) {
            $finalSummary = "Successfully migrated $($migrationStats.AttributesMigrated) custom attributes and $($migrationStats.AttributeValuesMigrated) attribute values"
        } else {
            $finalSummary = "Successfully migrated $($migrationStats.AttributesMigrated) custom attributes, $($migrationStats.AttributesSkipped) skipped"
        }
    }
    
    Write-LogSuccess $finalSummary -Category "Migration"
    Write-Output "SUCCESS: $finalSummary"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Custom attributes migration failed: $($_.Exception.Message)"
    Write-LogError "Custom attributes migration failed: $($_.Exception.Message)" -Category "Error"
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