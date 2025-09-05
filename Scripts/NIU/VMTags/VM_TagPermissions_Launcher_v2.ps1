<#
.SYNOPSIS
    Enhanced VM Tags and Permissions Launcher with Configuration Management and Credential Storage
.DESCRIPTION
    Advanced launcher script that uses centralized configuration management for executing VM tags 
    and permissions automation with environment-specific settings. Includes secure credential storage
    and management capabilities to streamline authentication workflows.
    
    The script supports storing vCenter credentials securely on the local machine, with automatic
    expiration and validation features. Credentials are encrypted per-user and can be managed
    through dedicated parameters.
    
.PARAMETER Environment
    Target environment (DEV, PROD, KLEB, OT). This determines which configuration settings
    and vCenter server will be used for the operation.
    
.PARAMETER ConfigPath
    Path to the configuration file or directory containing VMTagsConfig.psd1.
    If not specified, the script will search in standard locations relative to the script directory.
    
.PARAMETER OverrideVCenter
    Override the configured vCenter server for this execution.
    Useful for testing or emergency scenarios where you need to target a different server.
    
.PARAMETER OverrideAppCSV
    Override the configured App Permissions CSV file path.
    Allows using a different permissions mapping file for this execution.
    
.PARAMETER OverrideOSCSV
    Override the configured OS Mapping CSV file path.
    Allows using a different OS classification file for this execution.
    
.PARAMETER ForceDebug
    Force debug logging regardless of environment configuration.
    Enables verbose output and detailed logging for troubleshooting.
    
.PARAMETER DryRun
    Perform validation and setup only without executing the main automation script.
    Useful for testing configuration, connectivity, and prerequisites.
    
.PARAMETER UseStoredCredentials
    Attempt to use previously stored credentials instead of prompting for authentication.
    If stored credentials are not found, expired, or invalid, will fall back to interactive prompt.
    Stored credentials are validated against vCenter before use (unless disabled in config).
    
.PARAMETER ListStoredCredentials
    Display a list of all stored credentials with their metadata including environment,
    username, creation date, target vCenter server, and expiration status.
    This is a utility operation that does not execute the main script.
    
.PARAMETER CleanupExpiredCredentials
    Remove all expired stored credentials from the credential store.
    Credentials are considered expired based on the StoredCredentialMaxAgeDays configuration setting.
    This is a utility operation that does not execute the main script.
    
.PARAMETER ClearAllCredentials
    Remove ALL stored credentials from the credential store after confirmation.
    This is a destructive operation that requires typing 'YES' to confirm.
    Use this for security cleanup or when troubleshooting credential issues.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -Environment "PROD"
    
    Execute the VM tags and permissions script against the PROD environment using default settings.
    Will prompt for credentials if none are stored.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -Environment "DEV" -UseStoredCredentials
    
    Execute against DEV environment using previously stored credentials.
    If no stored credentials exist or they're invalid, will prompt for new ones and offer to store them.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -Environment "DEV" -ForceDebug -DryRun
    
    Perform a dry run against DEV environment with debug logging enabled.
    Validates configuration and prerequisites without executing the main automation.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -Environment "PROD" -OverrideVCenter "vcenter-backup.company.com"
    
    Execute against PROD environment but target a different vCenter server.
    Useful for maintenance scenarios or disaster recovery testing.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -ListStoredCredentials
    
    Display all stored credentials with their status and metadata.
    Shows which credentials are expired and need renewal.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -CleanupExpiredCredentials
    
    Remove all expired credentials from the secure credential store.
    Helps maintain credential hygiene and security.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -ClearAllCredentials
    
    Remove all stored credentials after confirmation prompt.
    Useful for security cleanup or when switching to different authentication methods.
    
.EXAMPLE
    .\VM_TagPermissions_Launcher_v2.ps1 -Environment "KLEB" -ConfigPath "C:\CustomConfigs\" -UseStoredCredentials -ForceDebug
    
    Execute against KLEB environment using a custom configuration directory,
    stored credentials, and debug logging enabled.
    
.NOTES
    Name: VM_TagPermissions_Launcher_v2.ps1
    Author: [Your Name]
    Version: 2.0
    Requires: PowerShell 5.1 or higher, VMware PowerCLI
    
    CREDENTIAL SECURITY:
    - Stored credentials are encrypted using PowerShell's Export-Clixml
    - Credentials are tied to the current user account and machine
    - Files are protected with restrictive NTFS permissions (Windows only)
    - Credentials have configurable expiration (default 30 days)
    - Secure deletion overwrites files before removal
    
    CREDENTIAL STORAGE LOCATIONS:
    - Default: $env:USERPROFILE\.vmtags\credentials\
    - Configurable via Security.CredentialStorePath in config file
    - Files named: vcenter_{environment}_{username}.credential
    
    CONFIGURATION REQUIREMENTS:
    - VMTagsConfig.psd1 must be accessible
    - VMTagsConfigManager.psm1 module must be available
    - Main PowerShell 7 script must be in configured location
    - Required CSV files must be accessible
    
    SECURITY CONSIDERATIONS:
    - Always use least-privilege service accounts for vCenter access
    - Regularly rotate stored credentials
    - Monitor credential usage through logging
    - Consider disabling credential storage in high-security environments
    - Review and clean up expired credentials regularly
    
.LINK
    https://docs.vmware.com/en/VMware-vSphere/
    
.LINK
    https://developer.vmware.com/powercli
    
.COMPONENT
    VMware vSphere Automation
    
.ROLE
    Infrastructure Administrator
    
.FUNCTIONALITY
    VM Management, Permissions Assignment, Tag Management, Credential Management
#>

#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = "Target environment")]
    [ValidateSet('DEV', 'PROD', 'KLEB', 'OT')]
    [string]$Environment,
    
    [Parameter(Mandatory = $false, HelpMessage = "Path to configuration file")]
    [string]$ConfigPath,
    
    [Parameter(Mandatory = $false, HelpMessage = "Override vCenter server")]
    [string]$OverrideVCenter,
    
    [Parameter(Mandatory = $false, HelpMessage = "Override App Permissions CSV path")]
    [string]$OverrideAppCSV,
    
    [Parameter(Mandatory = $false, HelpMessage = "Override OS Mapping CSV path")]
    [string]$OverrideOSCSV,
    
    [Parameter(Mandatory = $false, HelpMessage = "Force debug logging")]
    [switch]$ForceDebug,
    
    [Parameter(Mandatory = $false, HelpMessage = "Validation only - don't execute")]
    [switch]$DryRun,
    
    [Parameter(Mandatory = $false, HelpMessage = "Use stored credentials")]
    [switch]$UseStoredCredentials,

    [Parameter(Mandatory = $false, HelpMessage = "List stored credentials")]
    [switch]$ListStoredCredentials,

    [Parameter(Mandatory = $false, HelpMessage = "Clean up old log files")]
    [switch]$CleanupLogs,

    [Parameter(Mandatory = $false, HelpMessage = "Clean up expired credentials")]
    [switch]$CleanupExpiredCredentials,

    [Parameter(Mandatory = $false, HelpMessage = "Remove all stored credentials")]
    [switch]$ClearAllCredentials
)

#region Initialization
# CHANGED: Set ErrorActionPreference to Continue instead of Stop to prevent immediate termination
$ErrorActionPreference = "Continue"
$VerbosePreference = if ($ForceDebug) { "Continue" } else { "SilentlyContinue" }

# Global variables - Initialize early
$script:Config = $null
$script:ExecutionId = (Get-Date -Format 'yyyyMMdd_HHmmss')
$script:CredentialPath = $null
$script:TranscriptPath = $null
$script:TempFiles = @()
$script:ActualConfigPath = $null
$script:ConfigLoaded = $false

# Import configuration manager module - FIXED PATH LOGIC WITH NULL CHECKING
$scriptRoot = $null
try {
    $myCommandPath = $MyInvocation.MyCommand.Path
    if (-not [string]::IsNullOrEmpty($myCommandPath)) {
        $scriptRoot = Split-Path -Parent $myCommandPath -ErrorAction Stop
    }
}
catch {
    Write-Host "Warning: Could not determine script root from MyInvocation: $($_.Exception.Message)" -ForegroundColor Yellow
}

# If scriptRoot is still null, try alternative methods
if ([string]::IsNullOrEmpty($scriptRoot)) {
    try {
        # Try using $PSScriptRoot (PowerShell 3.0+)
        if ($PSScriptRoot) {
            $scriptRoot = $PSScriptRoot
            Write-Host "Using PSScriptRoot: $($scriptRoot)" -ForegroundColor Green
        }
        # Try using current location as fallback
        elseif (Get-Location) {
            $scriptRoot = (Get-Location).Path
            Write-Host "Using current location as script root: $($scriptRoot)" -ForegroundColor Yellow
        }
        # Final fallback
        else {
            $scriptRoot = "C:\Temp\Scripts\VMTags"
            Write-Host "Using hardcoded fallback script root: $($scriptRoot)" -ForegroundColor Red
        }
    }
    catch {
        $scriptRoot = "C:\Temp\Scripts\VMTags"  # Final hardcoded fallback
        Write-Host "Using emergency fallback script root: $($scriptRoot)" -ForegroundColor Red
    }
}

Write-Host "Script root determined as: $($scriptRoot)" -ForegroundColor Cyan

# Determine the actual config directory
$actualConfigPath = $null
if ($ConfigPath) {
    if (Test-Path $ConfigPath -PathType Container) {
        # ConfigPath is a directory
        $actualConfigPath = $ConfigPath
    } elseif (Test-Path $ConfigPath -PathType Leaf) {
        # ConfigPath is a file, get its directory
        $actualConfigPath = Split-Path $ConfigPath -Parent
    } else {
        Write-Host "Warning: Specified ConfigPath does not exist: $($ConfigPath)" -ForegroundColor Yellow
    }
}

$script:ActualConfigPath = $actualConfigPath

# Try multiple locations for the module in order of preference
$moduleLocations = @()

# 1. If ConfigPath was provided and exists, try there first
if ($actualConfigPath) {
    $moduleLocations += Join-Path $actualConfigPath "VMTagsConfigManager.psm1"
}

# 2. Try the script directory
$moduleLocations += Join-Path $scriptRoot "VMTagsConfigManager.psm1"

# 3. Try the Modules subdirectory (where your module actually is)
$moduleLocations += Join-Path $scriptRoot "Modules\VMTagsConfigManager.psm1"

# 4. Try common subdirectories
$moduleLocations += Join-Path $scriptRoot "ConfigFiles\VMTagsConfigManager.psm1"

# 5. Try based on your actual directory structure
$moduleLocations += "C:\Temp\Scripts\VMTags\Modules\VMTagsConfigManager.psm1"

# 6. Try relative to script directory
$moduleLocations += Join-Path (Split-Path $scriptRoot -Parent) "ConfigFiles\VMTagsConfigManager.psm1"

Write-Host "Searching for VMTagsConfigManager.psm1 in:" -ForegroundColor Cyan
$moduleLocations | ForEach-Object { 
    $exists = Test-Path $_
    $status = if ($exists) { "[FOUND]" } else { "[NOT FOUND]" }
    $color = if ($exists) { "Green" } else { "Gray" }
    Write-Host "  $status $_" -ForegroundColor $color
}

$configModulePath = $moduleLocations | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $configModulePath) {
    Write-Host "`nERROR: Configuration manager module not found!" -ForegroundColor Red
    Write-Host "Please ensure VMTagsConfigManager.psm1 is in one of these locations:" -ForegroundColor Red
    $moduleLocations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    
    # Don't throw, just exit gracefully
    Write-Host "`nPress any key to exit..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    return
}

Write-Host "`nLoading configuration module from: $($configModulePath)" -ForegroundColor Green

try {
    Import-Module $configModulePath -Force -Verbose:$false
    Write-Host "Configuration module loaded successfully" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: Failed to load configuration module: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "`nPress any key to exit..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    return
}
#endregion

#region Functions
function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [ValidateSet('Info', 'Warning', 'Error', 'Success', 'Debug')]
        [string]$Level = 'Info'
    )
    
    # Use default timestamp format if config not loaded yet
    $timestampFormat = if ($script:Config -and $script:Config.Logging.TimestampFormat) {
        $script:Config.Logging.TimestampFormat
    } else {
        "yyyy-MM-dd HH:mm:ss"
    }
    
    $timestamp = Get-Date -Format $timestampFormat
    $logMessage = "[$timestamp] [$($Level.ToUpper().PadRight(7))] $Message"
    
    # Console output - always show critical messages
    $shouldShowOnConsole = if ($script:Config -and $script:Config.Logging.LogLevels.Console) {
        $Level -in $script:Config.Logging.LogLevels.Console
    } else {
        $Level -in @('Info', 'Warning', 'Error', 'Success')  # Default levels
    }
    
    if ($shouldShowOnConsole) {
        switch ($Level) {
            'Info'    { Write-Host $logMessage -ForegroundColor White }
            'Warning' { Write-Host $logMessage -ForegroundColor Yellow }
            'Error'   { Write-Host $logMessage -ForegroundColor Red }
            'Success' { Write-Host $logMessage -ForegroundColor Green }
            'Debug'   { Write-Host $logMessage -ForegroundColor Gray }
        }
    }
    
    # File output - only if config is loaded and directory exists
    if ($script:Config -and $script:Config.DataPaths.LogDirectory) {
        try {
            $logFile = Join-Path $script:Config.DataPaths.LogDirectory ("VMTagsLauncher_" + $script:ExecutionId + ".log")
            Add-Content -Path $logFile -Value $logMessage -ErrorAction SilentlyContinue
        }
        catch {
            # Ignore file logging errors to prevent recursion
        }
    }
}

function Remove-OldLogFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [int]$MaxLogFiles = 5,
        
        [Parameter(Mandatory = $false)]
        [string]$LogDirectory = $null,
        
        [Parameter(Mandatory = $false)]
        [switch]$Force
    )
    
    try {
        # Determine log directory
        $targetLogDir = if ($LogDirectory) {
            $LogDirectory
        } elseif ($script:Config -and $script:Config.DataPaths.LogDirectory) {
            $script:Config.DataPaths.LogDirectory
        } else {
            Write-Log "No log directory specified or configured" -Level Warning
            return
        }
        
        if (-not (Test-Path $targetLogDir)) {
            Write-Log "Log directory does not exist: $($targetLogDir)" -Level Warning
            return
        }
        
        Write-Log "Starting log cleanup in directory: $($targetLogDir)" -Level Info
        Write-Log "Keeping most recent $($MaxLogFiles) log files" -Level Info
        
        # Get all log files with different patterns
        $logPatterns = @(
            "VMTags_*.log",
            "VMTagsLauncher_*.log",
            "*VMTags*.log"
        )
        
        $allLogFiles = @()
        foreach ($pattern in $logPatterns) {
            $files = Get-ChildItem -Path $targetLogDir -Filter $pattern -File -ErrorAction SilentlyContinue
            $allLogFiles += $files
        }
        
        # Remove duplicates based on FullName
        $uniqueLogFiles = $allLogFiles | Sort-Object FullName -Unique
        
        if ($uniqueLogFiles.Count -eq 0) {
            Write-Log "No log files found to clean up" -Level Info
            return
        }
        
        Write-Log "Found $($uniqueLogFiles.Count) total log file(s)" -Level Info
        
        # Group log files by type/pattern to handle different log types separately
        $launcherLogs = $uniqueLogFiles | Where-Object { $_.Name -like "VMTagsLauncher_*" }
        $mainScriptLogs = $uniqueLogFiles | Where-Object { $_.Name -like "VMTags_*" -and $_.Name -notlike "VMTagsLauncher_*" }
        $otherLogs = $uniqueLogFiles | Where-Object { $_.Name -notlike "VMTags_*" -and $_.Name -notlike "VMTagsLauncher_*" }
        
        $logGroups = @(
            @{ Name = "Launcher Logs"; Files = $launcherLogs },
            @{ Name = "Main Script Logs"; Files = $mainScriptLogs },
            @{ Name = "Other VM Tags Logs"; Files = $otherLogs }
        )
        
        $totalFilesRemoved = 0
        $totalSizeFreed = 0
        
        foreach ($group in $logGroups) {
            if ($group.Files.Count -eq 0) {
                continue
            }
            
            Write-Log "Processing $($group.Name): $($group.Files.Count) file(s)" -Level Debug
            
            # Sort by LastWriteTime (newest first) and keep only the newest files
            $sortedFiles = $group.Files | Sort-Object LastWriteTime -Descending
            $filesToKeep = $sortedFiles | Select-Object -First $MaxLogFiles
            $filesToRemove = $sortedFiles | Select-Object -Skip $MaxLogFiles
            
            Write-Log "  Keeping $($filesToKeep.Count) newest file(s)" -Level Debug
            Write-Log "  Removing $($filesToRemove.Count) older file(s)" -Level Debug
            
            foreach ($file in $filesToRemove) {
                try {
                    $fileSize = $file.Length
                    $fileAge = (Get-Date) - $file.LastWriteTime
                    
                    if ($Force) {
                        Remove-Item $file.FullName -Force -ErrorAction Stop
                        Write-Log "  Removed: $($file.Name) (Size: $([math]::Round($fileSize/1KB, 2)) KB, Age: $($fileAge.Days) days)" -Level Info
                        $totalFilesRemoved++
                        $totalSizeFreed += $fileSize
                    } else {
                        # In non-force mode, only remove files older than 7 days
                        if ($fileAge.Days -gt 7) {
                            Remove-Item $file.FullName -Force -ErrorAction Stop
                            Write-Log "  Removed: $($file.Name) (Size: $([math]::Round($fileSize/1KB, 2)) KB, Age: $($fileAge.Days) days)" -Level Info
                            $totalFilesRemoved++
                            $totalSizeFreed += $fileSize
                        } else {
                            Write-Log "  Skipped: $($file.Name) (too recent: $($fileAge.Days) days old)" -Level Debug
                        }
                    }
                }
                catch {
                    Write-Log "  Failed to remove $($file.Name): $($_.Exception.Message)" -Level Warning
                }
            }
            
            # Log the files being kept
            foreach ($file in $filesToKeep) {
                $fileAge = (Get-Date) - $file.LastWriteTime
                Write-Log "  Keeping: $($file.Name) (Age: $($fileAge.Days) days)" -Level Debug
            }
        }
        
        # Summary
        if ($totalFilesRemoved -gt 0) {
            $sizeMB = [math]::Round($totalSizeFreed/1MB, 2)
            Write-Log "Log cleanup completed: Removed $($totalFilesRemoved) file(s), freed $($sizeMB) MB" -Level Success
        } else {
            Write-Log "Log cleanup completed: No files removed" -Level Info
        }
        
        # Check if log directory is getting too large
        try {
            $remainingFiles = Get-ChildItem -Path $targetLogDir -File -ErrorAction SilentlyContinue
            $totalSize = ($remainingFiles | Measure-Object -Property Length -Sum).Sum
            $totalSizeMB = [math]::Round($totalSize/1MB, 2)
            
            Write-Log "Log directory status: $($remainingFiles.Count) files, $($totalSizeMB) MB total" -Level Info
            
            # Warn if log directory is getting large
            $maxSizeMB = if ($script:Config -and $script:Config.Logging.MaxLogSizeMB) {
                $script:Config.Logging.MaxLogSizeMB
            } else {
                100  # Default 100MB warning threshold
            }
            
            if ($totalSizeMB -gt $maxSizeMB) {
                Write-Log "Warning: Log directory size ($($totalSizeMB) MB) exceeds recommended maximum ($($maxSizeMB) MB)" -Level Warning
                Write-Log "Consider reducing MaxLogFiles or implementing more aggressive cleanup" -Level Warning
            }
        }
        catch {
            Write-Log "Could not calculate log directory size: $($_.Exception.Message)" -Level Warning
        }
        
    }
    catch {
        Write-Log "Error during log cleanup: $($_.Exception.Message)" -Level Error
    }
}

function Remove-OldLogFilesAllEnvironments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [int]$MaxLogFiles = 5,
        
        [Parameter(Mandatory = $false)]
        [switch]$Force
    )
    
    try {
        Write-Log "Starting comprehensive log cleanup across all environments..." -Level Info
        
        if (-not $script:Config) {
            Write-Log "Configuration not loaded, cannot perform multi-environment cleanup" -Level Warning
            return
        }
        
        $environmentsProcessed = 0
        $totalFilesRemoved = 0
        
        # Clean up logs for each configured environment
        foreach ($envName in $script:Config.Environments.Keys) {
            try {
                Write-Log "Checking logs for environment: $($envName)" -Level Info
                
                # Get the log directory for this environment
                $envConfig = $script:Config.Environments[$envName]
                if ($envConfig -and $envConfig.DataPaths -and $envConfig.DataPaths.LogDirectory) {
                    $logDir = $envConfig.DataPaths.LogDirectory
                    
                    if (Test-Path $logDir) {
                        Write-Log "Processing log directory: $($logDir)" -Level Info
                        
                        $beforeCount = (Get-ChildItem -Path $logDir -File -ErrorAction SilentlyContinue).Count
                        Remove-OldLogFiles -MaxLogFiles $MaxLogFiles -LogDirectory $logDir -Force:$Force
                        $afterCount = (Get-ChildItem -Path $logDir -File -ErrorAction SilentlyContinue).Count
                        
                        $removedCount = $beforeCount - $afterCount
                        $totalFilesRemoved += $removedCount
                        $environmentsProcessed++
                        
                        Write-Log "Environment $($envName): Processed $($beforeCount) files, removed $($removedCount)" -Level Info
                    } else {
                        Write-Log "Log directory for $($envName) does not exist: $($logDir)" -Level Warning
                    }
                } else {
                    Write-Log "No log directory configured for environment: $($envName)" -Level Warning
                }
            }
            catch {
                Write-Log "Error processing logs for environment $($envName): $($_.Exception.Message)" -Level Error
            }
        }
        
        Write-Log "Multi-environment log cleanup completed: Processed $($environmentsProcessed) environment(s), removed $($totalFilesRemoved) total files" -Level Success
        
    }
    catch {
        Write-Log "Error during multi-environment log cleanup: $($_.Exception.Message)" -Level Error
    }
}

function Get-VMCredentials {
    param()
    try {
        Write-Log "Collecting vCenter credentials..." -Level Info
        if ($UseStoredCredentials) {
            Write-Log "Attempting to retrieve stored credentials..." -Level Info
            # Placeholder for stored credential logic
            Write-Log "Stored credential retrieval not implemented, falling back to prompt" -Level Warning
        }
        # Interactive credential prompt
        Write-Host "`n" -NoNewline
        Write-Host "=== vCenter Authentication Required ===" -ForegroundColor Yellow
        Write-Host "Environment: $($script:Config.CurrentEnvironment)" -ForegroundColor Cyan
        Write-Host "vCenter Server: $($script:Config.vCenterServer)" -ForegroundColor Cyan
        Write-Host "SSO Domain: $($script:Config.SSODomain)" -ForegroundColor Cyan
        Write-Host ""
        $credential = Get-Credential -Message "Enter vCenter credentials for $($script:Config.CurrentEnvironment) environment" -UserName $script:Config.DefaultCredentialUser
        if (-not $credential) {
            throw "vCenter credentials are required"
        }
        Write-Log "Credentials collected for user: $($credential.UserName)" -Level Success
        return $credential
    }
    catch {
        Write-Log "Failed to get credentials: $($_.Exception.Message)" -Level Error
        throw
    }
}

function Test-StoredCredential {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    
    try {
        Write-Log "Testing stored credentials against vCenter server..." -Level Debug
        
        # Import VMware PowerCLI if not already loaded
        if (-not (Get-Module -Name "VMware.VimAutomation.Core" -ErrorAction SilentlyContinue)) {
            Import-Module VMware.VimAutomation.Core -ErrorAction Stop
        }
        
        # Set PowerCLI configuration to ignore certificate warnings temporarily
        $originalCertPolicy = (Get-PowerCLIConfiguration -Scope Session).InvalidCertificateAction
        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
        
        # Attempt connection
        $connection = Connect-VIServer -Server $script:Config.vCenterServer -Credential $Credential -ErrorAction Stop -WarningAction SilentlyContinue
        
        if ($connection) {
            Write-Log "Credential validation successful" -Level Debug
            Disconnect-VIServer -Server $connection -Confirm:$false -ErrorAction SilentlyContinue
            
            # Restore original certificate policy
            Set-PowerCLIConfiguration -InvalidCertificateAction $originalCertPolicy -Confirm:$false -Scope Session | Out-Null
            
            return $true
        } else {
            Write-Log "Credential validation failed - no connection established" -Level Debug
            return $false
        }
    }
    catch {
        Write-Log "Credential validation failed: $($_.Exception.Message)" -Level Debug
        
        # Clean up any partial connections
        try {
            Get-VIServer | Where-Object { $_.Name -eq $script:Config.vCenterServer } | Disconnect-VIServer -Confirm:$false -ErrorAction SilentlyContinue
        }
        catch {
            # Ignore cleanup errors
        }
        
        return $false
    }
}

function Save-VMCredential {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential,
        
        [Parameter(Mandatory = $true)]
        [string]$Environment,
        
        [Parameter(Mandatory = $true)]
        [string]$CredentialStorePath
    )
    
    try {
        Write-Log "Storing credentials securely..." -Level Debug
        
        # Ensure credential directory exists
        if (-not (Test-Path $CredentialStorePath)) {
            New-Item -Path $CredentialStorePath -ItemType Directory -Force | Out-Null
            Write-Log "Created credential storage directory: $CredentialStorePath" -Level Info
        }
        
        # Set directory permissions (Windows only)
        if ($IsWindows -or $PSVersionTable.PSVersion.Major -le 5) {
            try {
                $acl = Get-Acl $CredentialStorePath
                $acl.SetAccessRuleProtection($true, $false)  # Remove inheritance
                $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($env:USERNAME, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
                $acl.SetAccessRule($accessRule)
                Set-Acl $CredentialStorePath $acl
                Write-Log "Set secure permissions on credential directory" -Level Debug
            }
            catch {
                Write-Log "Warning: Could not set secure permissions on credential directory: $($_.Exception.Message)" -Level Warning
            }
        }
        
        # Define file paths
        $credentialFileName = "vcenter_$($Environment.ToLower())_$($env:USERNAME).credential"
        $fullCredentialPath = Join-Path $CredentialStorePath $credentialFileName
        $metadataPath = "$fullCredentialPath.metadata"
        
        # Create metadata
        $metadata = [PSCustomObject]@{
            Environment = $Environment
            UserName = $Credential.UserName
            MachineName = $env:COMPUTERNAME
            CreatedBy = $env:USERNAME
            CreatedDate = Get-Date
            vCenterServer = $script:Config.vCenterServer
            ScriptVersion = if ($script:Config -and $script:Config.Application.Version) { $script:Config.Application.Version } else { "Unknown" }
        }
        
        # Export credential and metadata
        $Credential | Export-Clixml -Path $fullCredentialPath -Force
        $metadata | Export-Clixml -Path $metadataPath -Force
        
        # Set file permissions (Windows only)
        if ($IsWindows -or $PSVersionTable.PSVersion.Major -le 5) {
            try {
                foreach ($filePath in @($fullCredentialPath, $metadataPath)) {
                    $acl = Get-Acl $filePath
                    $acl.SetAccessRuleProtection($true, $false)  # Remove inheritance
                    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($env:USERNAME, "FullControl", "None", "None", "Allow")
                    $acl.SetAccessRule($accessRule)
                    Set-Acl $filePath $acl
                }
                Write-Log "Set secure permissions on credential files" -Level Debug
            }
            catch {
                Write-Log "Warning: Could not set secure permissions on credential files: $($_.Exception.Message)" -Level Warning
            }
        }
        
        Write-Log "Credentials stored successfully at: $fullCredentialPath" -Level Success
        Write-Log "Credential metadata stored at: $metadataPath" -Level Debug
    }
    catch {
        Write-Log "Failed to store credentials: $($_.Exception.Message)" -Level Error
        throw
    }
}

function Remove-StoredCredential {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$CredentialPath
    )
    
    try {
        $filesToRemove = @($CredentialPath, "$CredentialPath.metadata")
        
        foreach ($file in $filesToRemove) {
            if (Test-Path $file) {
                # Securely overwrite the file before deletion
                try {
                    $fileSize = (Get-Item $file).Length
                    $randomBytes = New-Object byte[] $fileSize
                    [System.Security.Cryptography.RNGCryptoServiceProvider]::Create().GetBytes($randomBytes)
                    [System.IO.File]::WriteAllBytes($file, $randomBytes)
                }
                catch {
                    # If overwrite fails, still try to delete
                }
                
                Remove-Item $file -Force -ErrorAction Stop
                Write-Log "Removed credential file: $(Split-Path $file -Leaf)" -Level Debug
            }
        }
    }
    catch {
        Write-Log "Warning: Could not remove stored credential files: $($_.Exception.Message)" -Level Warning
    }
}

# Helper function to list stored credentials (useful for debugging)
function Get-StoredCredentials {
    [CmdletBinding()]
    param()
    
    try {
        $credentialStorePath = if ($script:Config -and $script:Config.Security.CredentialStorePath) {
            $script:Config.Security.CredentialStorePath
        } else {
            Join-Path $env:USERPROFILE ".vmtags\credentials"
        }
        
        if (-not (Test-Path $credentialStorePath)) {
            Write-Log "No credential store directory found" -Level Info
            return @()
        }
        
        $credentialFiles = Get-ChildItem -Path $credentialStorePath -Filter "*.credential" -ErrorAction SilentlyContinue
        $storedCredentials = @()
        
        foreach ($credFile in $credentialFiles) {
            $metadataFile = "$($credFile.FullName).metadata"
            
            if (Test-Path $metadataFile) {
                try {
                    $metadata = Import-Clixml -Path $metadataFile -ErrorAction Stop
                    $storedCredentials += [PSCustomObject]@{
                        FileName = $credFile.Name
                        Environment = $metadata.Environment
                        UserName = $metadata.UserName
                        CreatedDate = $metadata.CreatedDate
                        vCenterServer = $metadata.vCenterServer
                        IsExpired = if ($script:Config -and $script:Config.Security.StoredCredentialMaxAgeDays) {
                            (Get-Date) -gt $metadata.CreatedDate.AddDays($script:Config.Security.StoredCredentialMaxAgeDays)
                        } else {
                            (Get-Date) -gt $metadata.CreatedDate.AddDays(30)
                        }
                    }
                }
                catch {
                    Write-Log "Could not read metadata for $($credFile.Name): $($_.Exception.Message)" -Level Warning
                    $storedCredentials += [PSCustomObject]@{
                        FileName = $credFile.Name
                        Environment = "Unknown"
                        UserName = "Unknown"
                        CreatedDate = $credFile.CreationTime
                        vCenterServer = "Unknown"
                        IsExpired = $true  # Treat as expired if metadata is unreadable
                    }
                }
            } else {
                # No metadata file, use file properties
                $storedCredentials += [PSCustomObject]@{
                    FileName = $credFile.Name
                    Environment = "Unknown"
                    UserName = "Unknown"
                    CreatedDate = $credFile.CreationTime
                    vCenterServer = "Unknown"
                    IsExpired = (Get-Date) -gt $credFile.CreationTime.AddDays(30)
                }
            }
        }
        
        return $storedCredentials
    }
    catch {
        Write-Log "Error retrieving stored credentials list: $($_.Exception.Message)" -Level Warning
        return @()
    }
}

# Add cleanup function for expired credentials
function Remove-ExpiredCredentials {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [switch]$Force
    )
    
    try {
        Write-Log "Checking for expired stored credentials..." -Level Info
        
        $storedCredentials = Get-StoredCredentials
        $expiredCredentials = $storedCredentials | Where-Object { $_.IsExpired }
        
        if ($expiredCredentials.Count -eq 0) {
            Write-Log "No expired credentials found" -Level Info
            return
        }
        
        Write-Log "Found $($expiredCredentials.Count) expired credential(s)" -Level Warning
        
        $credentialStorePath = if ($script:Config -and $script:Config.Security.CredentialStorePath) {
            $script:Config.Security.CredentialStorePath
        } else {
            Join-Path $env:USERPROFILE ".vmtags\credentials"
        }
        
        foreach ($expiredCred in $expiredCredentials) {
            $credentialPath = Join-Path $credentialStorePath $expiredCred.FileName
            
            if ($Force -or $script:Config.Security.AutoCleanupExpiredCredentials) {
                Write-Log "Removing expired credential: $($expiredCred.FileName) (created: $($expiredCred.CreatedDate))" -Level Info
                Remove-StoredCredential -CredentialPath $credentialPath
            } else {
                Write-Log "Expired credential found but auto-cleanup disabled: $($expiredCred.FileName)" -Level Warning
            }
        }
    }
    catch {
        Write-Log "Error during expired credential cleanup: $($_.Exception.Message)" -Level Warning
    }
}
function Initialize-Configuration {
    param()
    
    try {
        Write-Host "Loading configuration for environment: $Environment" -ForegroundColor Cyan
        
        # Determine the config file path
        $configFilePath = $null
        
        if ($ConfigPath) {
            if (Test-Path $ConfigPath -PathType Leaf) {
                # ConfigPath is directly pointing to the config file
                $configFilePath = $ConfigPath
            } elseif (Test-Path $ConfigPath -PathType Container) {
                # ConfigPath is a directory, look for the config file in it
                $configFilePath = Join-Path $ConfigPath "VMTagsConfig.psd1"
            }
        }
        
        # If still not found, try relative to the module we loaded
        if (-not $configFilePath -or -not (Test-Path $configFilePath)) {
            if ($script:ActualConfigPath) {
                $configFilePath = Join-Path $script:ActualConfigPath "VMTagsConfig.psd1"
            }
        }
        
        # Last resort: try same directory as script
        if (-not $configFilePath -or -not (Test-Path $configFilePath)) {
            $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
            $configFilePath = Join-Path $scriptRoot "VMTagsConfig.psd1"
        }
        
        Write-Host "Config file path: $configFilePath" -ForegroundColor Cyan
        Write-Host "Config file exists: $(Test-Path $configFilePath)" -ForegroundColor Cyan
        
        if (-not (Test-Path $configFilePath)) {
            throw "Configuration file not found: $configFilePath"
        }
        
        # Load configuration
        $loadParams = @{
            Environment = $Environment
            ConfigPath = $configFilePath
            Verbose = $VerbosePreference -eq "Continue"
        }
        
        $script:Config = Get-VMTagsConfig @loadParams
        
        if (-not $script:Config) {
            throw "Failed to load configuration"
        }
        
        $script:ConfigLoaded = $true
        
        # Apply overrides
        if ($OverrideVCenter) {
            $script:Config.vCenterServer = $OverrideVCenter
            Write-Host "Overriding vCenter server: $OverrideVCenter" -ForegroundColor Yellow
        }
        
        if ($OverrideAppCSV) {
            $script:Config.DataPaths.AppPermissionsCSV = $OverrideAppCSV
            Write-Host "Overriding App Permissions CSV: $OverrideAppCSV" -ForegroundColor Yellow
        }
        
        if ($OverrideOSCSV) {
            $script:Config.DataPaths.OSMappingCSV = $OverrideOSCSV
            Write-Host "Overriding OS Mapping CSV: $OverrideOSCSV" -ForegroundColor Yellow
        }
        
        if ($ForceDebug) {
            $script:Config.EnvironmentSettings.EnableDebugLogging = $true
            Write-Host "Debug logging enabled" -ForegroundColor Yellow
        }
        
        # Create required directories
        $null = New-VMTagsDirectories -Config $script:Config
        
        # Ensure the log directory specifically exists
        if ($script:Config.DataPaths.LogDirectory) {
            if (-not (Test-Path $script:Config.DataPaths.LogDirectory)) {
                New-Item -Path $script:Config.DataPaths.LogDirectory -ItemType Directory -Force | Out-Null
                Write-Host "Created log directory: $($script:Config.DataPaths.LogDirectory)" -ForegroundColor Green
            }
        }
        
        Write-Host "Configuration loaded successfully" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "ERROR: Failed to initialize configuration: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Test-Prerequisites {
    param()
    
    try {
        Write-Log "Running comprehensive prerequisites check..." -Level Info
        
        $issues = @()
        $warnings = @()
        
        # Test configuration paths first
        if (-not (Test-ConfigurationPaths)) {
            $issues += "Configuration path validation failed"
        }
        
        # Test configuration
        if ($script:Config) {
            $configValidation = Test-VMTagsConfig -Config $script:Config
            if (-not $configValidation.IsValid) {
                $issues += $configValidation.Issues
            }
            $warnings += $configValidation.Warnings
        }
        
        # Test VMware PowerCLI (skip in dry run to avoid long delays)
        if (-not $DryRun) {
            try {
                $powerCLI = Get-Module -Name "VMware.PowerCLI" -ListAvailable -ErrorAction Stop
                if ($powerCLI) {
                    Write-Log "Found VMware PowerCLI version: $($powerCLI[0].Version)" -Level Success
                } else {
                    $issues += "VMware PowerCLI module not found"
                }
            }
            catch {
                $issues += "Could not check VMware PowerCLI: $($_.Exception.Message)"
            }
        }
        
        # Test network connectivity (skip in dry run)
        if (-not $DryRun -and $script:Config -and $script:Config.vCenterServer) {
            Write-Log "Testing connectivity to vCenter: $($script:Config.vCenterServer)" -Level Debug
            try {
                $connection = Test-NetConnection -ComputerName $script:Config.vCenterServer -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue -ErrorAction Stop
                if ($connection) {
                    Write-Log "Network connectivity to vCenter confirmed" -Level Success
                } else {
                    $issues += "Cannot reach vCenter server: $($script:Config.vCenterServer)"
                }
            }
            catch {
                $warnings += "Could not test network connectivity: $($_.Exception.Message)"
            }
        }
        
        # Test CSV files
        if ($script:Config) {
            if ($script:Config.DataPaths.AppPermissionsCSV) {
                if (Test-Path $script:Config.DataPaths.AppPermissionsCSV) {
                    Write-Log "App Permissions CSV found" -Level Success
                } else {
                    $issues += "App Permissions CSV not found: $($script:Config.DataPaths.AppPermissionsCSV)"
                }
            }
            
            if ($script:Config.DataPaths.OSMappingCSV) {
                if (Test-Path $script:Config.DataPaths.OSMappingCSV) {
                    Write-Log "OS Mapping CSV found" -Level Success
                } else {
                    $issues += "OS Mapping CSV not found: $($script:Config.DataPaths.OSMappingCSV)"
                }
            }
        }
        
        # Display warnings
        foreach ($warning in $warnings) {
            Write-Log $warning -Level Warning
        }
        
        # Display issues
        if ($issues.Count -gt 0) {
            Write-Log "Prerequisites check failed with $($issues.Count) issue(s):" -Level Error
            foreach ($issue in $issues) {
                Write-Log "  - $issue" -Level Error
            }
            return $false
        }
        
        Write-Log "Prerequisites check passed" -Level Success
        return $true
    }
    catch {
        Write-Log "Prerequisites check failed: $($_.Exception.Message)" -Level Error
        return $false
    }
}

function New-CredentialFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    
    try {
        Write-Log "Creating secure credential file..." -Level Debug
        
        # Validate temp directory exists
        $credentialDir = $script:Config.DefaultPaths.TempDirectory
        Write-Log "Credential directory configured as: $($credentialDir)" -Level Debug
        
        if ([string]::IsNullOrEmpty($credentialDir)) {
            throw "TempDirectory is not configured in the configuration"
        }
        
        # Ensure temp directory exists
        if (-not (Test-Path $credentialDir)) {
            Write-Log "Creating credential directory: $($credentialDir)" -Level Info
            New-Item -Path $credentialDir -ItemType Directory -Force | Out-Null
        }
        
        # Create the credential file paths
        $script:CredentialPath = Join-Path $credentialDir "VMTagsCredential_$($script:ExecutionId)"
        Write-Log "Base credential path: $($script:CredentialPath)" -Level Debug
        
        $script:TempFiles += $script:CredentialPath
        
        # Export credential to encrypted XML
        $credentialFile = "$($script:CredentialPath).credential.xml"
        Write-Log "Credential XML file: $($credentialFile)" -Level Debug
        
        $script:TempFiles += $credentialFile
        $Credential | Export-Clixml -Path $credentialFile -Force
        
        # Verify the credential file was created
        if (-not (Test-Path $credentialFile)) {
            throw "Failed to create credential XML file: $($credentialFile)"
        }
        
        Write-Log "Credential XML file created successfully: $($credentialFile)" -Level Debug
        
        # Create metadata file
        $metadata = [PSCustomObject]@{
            CredentialFile = $credentialFile
            UserName = $Credential.UserName
            CreatedAt = Get-Date -Format $script:Config.Logging.TimestampFormat
            ExpiresAt = (Get-Date).AddMinutes($script:Config.Security.CredentialTimeoutMinutes).ToString($script:Config.Logging.TimestampFormat)
            MachineName = $env:COMPUTERNAME
            CreatedBy = $env:USERNAME
            ExecutionId = $script:ExecutionId
            Environment = $script:Config.CurrentEnvironment
        }
        
        $metadata | Export-Csv -Path $script:CredentialPath -NoTypeInformation -Force
        
        # Verify the metadata file was created
        if (-not (Test-Path $script:CredentialPath)) {
            throw "Failed to create credential metadata file: $($script:CredentialPath)"
        }
        
        Write-Log "Credential metadata file created successfully: $($script:CredentialPath)" -Level Debug
        
        # Verify both files exist and are readable
        $metadataSize = (Get-Item $script:CredentialPath).Length
        $credentialSize = (Get-Item $credentialFile).Length
        
        Write-Log "Metadata file size: $($metadataSize) bytes" -Level Debug
        Write-Log "Credential file size: $($credentialSize) bytes" -Level Debug
        
        if ($metadataSize -eq 0) {
            throw "Credential metadata file is empty: $($script:CredentialPath)"
        }
        
        if ($credentialSize -eq 0) {
            throw "Credential XML file is empty: $($credentialFile)"
        }
        
        Write-Log "Credential file created successfully" -Level Success
        return $true
    }
    catch {
        Write-Log "Failed to create credential file: $($_.Exception.Message)" -Level Error
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level Debug
        return $false
    }
}

function Start-MainScript {
    param()
    
    try {
        Write-Log "Preparing to execute main script..." -Level Info
        
        # Validate critical paths first
        if ([string]::IsNullOrEmpty($script:Config.DefaultPaths.PowerShell7Path)) {
            throw "PowerShell7Path is not configured in the configuration file"
        }
        
        if ([string]::IsNullOrEmpty($script:Config.DefaultPaths.MainScriptPath)) {
            throw "MainScriptPath is not configured in the configuration file"
        }
        
        if (-not (Test-Path $script:Config.DefaultPaths.PowerShell7Path)) {
            throw "PowerShell 7 executable not found: $($script:Config.DefaultPaths.PowerShell7Path)"
        }
        
        if (-not (Test-Path $script:Config.DefaultPaths.MainScriptPath)) {
            throw "Main script not found: $($script:Config.DefaultPaths.MainScriptPath)"
        }
        
        # Build execution parameters manually instead of using Get-VMTagsExecutionParameters
        Write-Log "Building execution parameters manually..." -Level Debug
        
        # Get values directly from config with null checking
        $vCenterServer = if ($script:Config.vCenterServer) { $script:Config.vCenterServer } else { throw "vCenterServer not configured" }
        $appCsvPath = if ($script:Config.DataPaths.AppPermissionsCSV) { $script:Config.DataPaths.AppPermissionsCSV } else { throw "AppPermissionsCSV path not configured" }
        $osCsvPath = if ($script:Config.DataPaths.OSMappingCSV) { $script:Config.DataPaths.OSMappingCSV } else { throw "OSMappingCSV path not configured" }
        $currentEnvironment = if ($script:Config.CurrentEnvironment) { $script:Config.CurrentEnvironment } else { $Environment }
        
        Write-Log "Execution parameters:" -Level Debug
        Write-Log "  vCenter Server: $($vCenterServer)" -Level Debug
        Write-Log "  App CSV: $($appCsvPath)" -Level Debug
        Write-Log "  OS CSV: $($osCsvPath)" -Level Debug
        Write-Log "  Environment: $($currentEnvironment)" -Level Debug
        Write-Log "  Credential Path: $($script:CredentialPath)" -Level Debug
        
        # Validate CSV files exist
        if (-not (Test-Path $appCsvPath)) {
            throw "App Permissions CSV file not found: $($appCsvPath)"
        }
        
        if (-not (Test-Path $osCsvPath)) {
            throw "OS Mapping CSV file not found: $($osCsvPath)"
        }
        
        if ([string]::IsNullOrEmpty($script:CredentialPath) -or -not (Test-Path $script:CredentialPath)) {
            throw "Credential file not found or path is null: $($script:CredentialPath)"
        }
        
        # Build PowerShell 7 arguments step by step
        $ps7Arguments = @()
        
        # Add main script file
        $ps7Arguments += '-File'
        $ps7Arguments += "`"$($script:Config.DefaultPaths.MainScriptPath)`""
        
        # Add vCenter server
        $ps7Arguments += '-vCenterServer'
        $ps7Arguments += "`"$($vCenterServer)`""
        
        # Add credential path
        $ps7Arguments += '-CredentialPath'
        $ps7Arguments += "`"$($script:CredentialPath)`""
        
        # Add CSV paths
        $ps7Arguments += '-AppPermissionsCsvPath'
        $ps7Arguments += "`"$($appCsvPath)`""
        
        $ps7Arguments += '-OsMappingCsvPath'
        $ps7Arguments += "`"$($osCsvPath)`""
        
        # Add environment
        $ps7Arguments += '-Environment'
        $ps7Arguments += $currentEnvironment
        
        # Add debug parameter if enabled
        if ($script:Config.EnvironmentSettings -and $script:Config.EnvironmentSettings.EnableDebugLogging) {
            $ps7Arguments += '-EnableScriptDebug'
        }
        
        # Add standard PowerShell arguments
        if ($script:Config.PowerShell7 -and $script:Config.PowerShell7.StandardArguments) {
            foreach ($arg in $script:Config.PowerShell7.StandardArguments) {
                if (-not [string]::IsNullOrEmpty($arg)) {
                    $ps7Arguments += $arg
                }
            }
        }
        
        # Add debug arguments if debug is enabled
        if (($script:Config.EnvironmentSettings -and $script:Config.EnvironmentSettings.EnableDebugLogging) -or $ForceDebug) {
            if ($script:Config.PowerShell7 -and $script:Config.PowerShell7.DebugArguments) {
                foreach ($arg in $script:Config.PowerShell7.DebugArguments) {
                    if (-not [string]::IsNullOrEmpty($arg)) {
                        $ps7Arguments += $arg
                    }
                }
            }
        }
        
        $commandLine = "$($script:Config.DefaultPaths.PowerShell7Path) $($ps7Arguments -join ' ')"
        Write-Log "Execution command: $($commandLine)" -Level Debug
        
        if ($DryRun) {
            Write-Log "DRY RUN MODE: Would execute the following command:" -Level Info
            Write-Log $commandLine -Level Info
            return @{ ExitCode = 0; ExecutionTime = "00:00:00" }
        }
        
        # Determine working directory with comprehensive fallbacks
        $workingDir = $null
        $potentialDirs = @()
        
        # Try configured working directory first
        if ($script:Config.PowerShell7 -and -not [string]::IsNullOrEmpty($script:Config.PowerShell7.WorkingDirectory)) {
            $potentialDirs += $script:Config.PowerShell7.WorkingDirectory
        }
        
        # Try main script directory
        try {
            $scriptDir = Split-Path $script:Config.DefaultPaths.MainScriptPath -Parent -ErrorAction Stop
            if (-not [string]::IsNullOrEmpty($scriptDir)) {
                $potentialDirs += $scriptDir
            }
        }
        catch {
            Write-Log "Could not determine main script directory: $($_.Exception.Message)" -Level Warning
        }
        
        # Try temp directory
        if ($script:Config.DefaultPaths -and -not [string]::IsNullOrEmpty($script:Config.DefaultPaths.TempDirectory)) {
            $potentialDirs += $script:Config.DefaultPaths.TempDirectory
        }
        
        # Try launcher script directory - IMPROVED VERSION using scriptRoot from initialization
        if ($scriptRoot -and -not [string]::IsNullOrEmpty($scriptRoot)) {
            $potentialDirs += $scriptRoot
            Write-Log "Added launcher directory from initialization scriptRoot: $($scriptRoot)" -Level Debug
        } else {
            # Fallback methods
            try {
                $myCommandPath = $MyInvocation.MyCommand.Path
                if (-not [string]::IsNullOrEmpty($myCommandPath)) {
                    $launcherDir = Split-Path -Parent $myCommandPath -ErrorAction Stop
                    if (-not [string]::IsNullOrEmpty($launcherDir)) {
                        $potentialDirs += $launcherDir
                        Write-Log "Added launcher directory from MyInvocation: $($launcherDir)" -Level Debug
                    }
                } elseif ($PSScriptRoot -and -not [string]::IsNullOrEmpty($PSScriptRoot)) {
                    $potentialDirs += $PSScriptRoot
                    Write-Log "Added launcher directory from PSScriptRoot: $($PSScriptRoot)" -Level Debug
                } else {
                    Write-Log "Could not determine launcher directory - all methods returned null or empty" -Level Debug
                }
            }
            catch {
                Write-Log "Could not determine launcher directory: $($_.Exception.Message)" -Level Debug
            }
        }
        
        # System fallbacks
        $potentialDirs += @("C:\Temp", $env:TEMP, "C:\Windows\Temp", "C:\")
        
        # Find first valid directory
        foreach ($dir in $potentialDirs) {
            if (-not [string]::IsNullOrEmpty($dir)) {
                try {
                    if (Test-Path $dir -PathType Container -ErrorAction Stop) {
                        $workingDir = $dir
                        Write-Log "Selected working directory: $($workingDir)" -Level Info
                        break
                    }
                }
                catch {
                    Write-Log "Could not validate directory $($dir): $($_.Exception.Message)" -Level Debug
                    continue
                }
            }
        }
        
        # Final fallback to current location
        if ([string]::IsNullOrEmpty($workingDir)) {
            try {
                $currentLocation = Get-Location -ErrorAction Stop
                if ($currentLocation -and $currentLocation.Path) {
                    $workingDir = $currentLocation.Path
                    Write-Log "Using current directory as working directory: $($workingDir)" -Level Warning
                }
            }
            catch {
                Write-Log "Could not determine current directory: $($_.Exception.Message)" -Level Warning
            }
        }
        
        # Build process arguments with careful null checking
        $processArgs = @{
            FilePath = $script:Config.DefaultPaths.PowerShell7Path
            ArgumentList = $ps7Arguments
            Wait = $true
            PassThru = $true
            NoNewWindow = $true
        }
        
        # Only add WorkingDirectory if we have a valid, non-null path
        if (-not [string]::IsNullOrEmpty($workingDir)) {
            try {
                if (Test-Path $workingDir -PathType Container -ErrorAction Stop) {
                    $processArgs.WorkingDirectory = $workingDir
                    Write-Log "Working directory set to: $($workingDir)" -Level Debug
                } else {
                    Write-Log "Working directory path is not valid: $($workingDir)" -Level Warning
                }
            }
            catch {
                Write-Log "Error validating working directory $($workingDir): $($_.Exception.Message)" -Level Warning
            }
        } else {
            Write-Log "No valid working directory found, using process default" -Level Warning
        }
        
        Write-Log "Starting PowerShell 7 execution..." -Level Info
        Write-Log "Process FilePath: $($processArgs.FilePath)" -Level Debug
        Write-Log "Process Arguments count: $($processArgs.ArgumentList.Count)" -Level Debug
        if ($processArgs.ContainsKey('WorkingDirectory')) {
            Write-Log "Process WorkingDirectory: $($processArgs.WorkingDirectory)" -Level Debug
        } else {
            Write-Log "Process WorkingDirectory: Not specified" -Level Debug
        }
        
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        
        # Start process with comprehensive error handling
        $process = $null
        try {
            Write-Log "Attempting to start process..." -Level Debug
            $process = Start-Process @processArgs -ErrorAction Stop
            Write-Log "Process started successfully with PID: $($process.Id)" -Level Info
        }
        catch {
            Write-Log "Failed to start process: $($_.Exception.Message)" -Level Error
            
            # If working directory might be the issue, try without it
            if ($processArgs.ContainsKey('WorkingDirectory')) {
                Write-Log "Retrying without working directory..." -Level Warning
                $processArgs.Remove('WorkingDirectory')
                
                try {
                    $process = Start-Process @processArgs -ErrorAction Stop
                    Write-Log "Process started successfully without working directory, PID: $($process.Id)" -Level Info
                }
                catch {
                    Write-Log "Failed to start process even without working directory: $($_.Exception.Message)" -Level Error
                    throw "Could not start PowerShell 7 process: $($_.Exception.Message)"
                }
            } else {
                throw "Could not start PowerShell 7 process: $($_.Exception.Message)"
            }
        }
        
        if (-not $process) {
            throw "Process object is null after Start-Process call"
        }
        
        # Wait for completion with timeout
        $timeoutMinutes = 60  # Default
        if ($script:Config.PowerShell7 -and $script:Config.PowerShell7.TimeoutMinutes) {
            $timeoutMinutes = $script:Config.PowerShell7.TimeoutMinutes
        }
        
        $timeoutMs = $timeoutMinutes * 60 * 1000
        Write-Log "Waiting for process completion (timeout: $($timeoutMinutes) minutes)..." -Level Debug
        
        $completed = $false
        try {
            $completed = $process.WaitForExit($timeoutMs)
        }
        catch {
            Write-Log "Error waiting for process: $($_.Exception.Message)" -Level Error
            $completed = $false
        }
        
                if (-not $completed) {
            Write-Log "Process exceeded timeout of $($timeoutMinutes) minutes, attempting to terminate..." -Level Error
            try {
                if (-not $process.HasExited) {
                    $process.Kill()
                    Start-Sleep -Seconds 2
                    if (-not $process.HasExited) {
                        $process.WaitForExit(5000)  # Wait up to 5 more seconds
                    }
                }
            }
            catch {
                Write-Log "Failed to terminate timed-out process: $($_.Exception.Message)" -Level Warning
            }
            
            $stopwatch.Stop()
            return @{
                ExitCode = 1
                ExecutionTime = "TIMEOUT"
                ErrorMessage = "Process exceeded timeout of $($timeoutMinutes) minutes"
            }
        }
        
        $stopwatch.Stop()
        $executionTime = $stopwatch.Elapsed.ToString("hh\:mm\:ss")
        
        $exitCode = if ($process.ExitCode -ne $null) { $process.ExitCode } else { 1 }
        
        Write-Log "Process completed with exit code: $($exitCode)" -Level Info
        Write-Log "Execution time: $($executionTime)" -Level Info
        
        return @{
            ExitCode = $exitCode
            ExecutionTime = $executionTime
        }
    }
    catch {
        Write-Log "Failed to execute main script: $($_.Exception.Message)" -Level Error
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level Debug
        
        return @{
            ExitCode = 1
            ExecutionTime = "ERROR"
            ErrorMessage = $_.Exception.Message
        }
    }
}
function Remove-TempFiles {
    param()
    
    try {
        Write-Log "Cleaning up temporary files..." -Level Debug
        
        foreach ($tempFile in $script:TempFiles) {
            if (Test-Path $tempFile) {
                try {
                    # Overwrite sensitive files before deletion
                    if ($tempFile -like "*.credential*") {
                        try {
                            $randomBytes = New-Object byte[] (Get-Item $tempFile).Length
                            [System.Security.Cryptography.RNGCryptoServiceProvider]::Create().GetBytes($randomBytes)
                            [System.IO.File]::WriteAllBytes($tempFile, $randomBytes)
                        }
                        catch {
                            # If overwrite fails, still try to delete
                        }
                    }
                    
                    Remove-Item $tempFile -Force -ErrorAction Stop
                    Write-Log "Removed temporary file: $(Split-Path $tempFile -Leaf)" -Level Debug
                }
                catch {
                    Write-Log "Warning: Could not remove temporary file $tempFile : $($_.Exception.Message)" -Level Warning
                }
            }
        }
        
        Write-Log "Cleanup completed" -Level Debug
    }
    catch {
        Write-Log "Error during cleanup: $($_.Exception.Message)" -Level Warning
    }
}

function Test-ConfigurationPaths {
    param()
    
    try {
        Write-Log "Validating configuration paths..." -Level Debug
        
        $issues = @()
        $warnings = @()
        
        # Test PowerShell 7 path
        if (-not $script:Config.DefaultPaths.PowerShell7Path) {
            $issues += "PowerShell7Path is not configured"
        } elseif (-not (Test-Path $script:Config.DefaultPaths.PowerShell7Path)) {
            $issues += "PowerShell 7 executable not found: $($script:Config.DefaultPaths.PowerShell7Path)"
        } else {
            Write-Log "PowerShell 7 path validated: $($script:Config.DefaultPaths.PowerShell7Path)" -Level Success
        }
        
        # Test main script path
        if (-not $script:Config.DefaultPaths.MainScriptPath) {
            $issues += "MainScriptPath is not configured"
        } elseif (-not (Test-Path $script:Config.DefaultPaths.MainScriptPath)) {
            $issues += "Main script not found: $($script:Config.DefaultPaths.MainScriptPath)"
        } else {
            Write-Log "Main script path validated: $($script:Config.DefaultPaths.MainScriptPath)" -Level Success
        }
        
        # Test working directory
        if ($script:Config.PowerShell7.WorkingDirectory) {
            if (-not (Test-Path $script:Config.PowerShell7.WorkingDirectory)) {
                $warnings += "Configured working directory does not exist: $($script:Config.PowerShell7.WorkingDirectory)"
            } else {
                Write-Log "Working directory validated: $($script:Config.PowerShell7.WorkingDirectory)" -Level Success
            }
        } else {
            $warnings += "WorkingDirectory is not configured, will use fallback"
        }
        
        # Test temp directory
        if ($script:Config.DefaultPaths.TempDirectory) {
            if (-not (Test-Path $script:Config.DefaultPaths.TempDirectory)) {
                try {
                    New-Item -Path $script:Config.DefaultPaths.TempDirectory -ItemType Directory -Force | Out-Null
                    Write-Log "Created temp directory: $($script:Config.DefaultPaths.TempDirectory)" -Level Info
                } catch {
                    $warnings += "Could not create temp directory: $($script:Config.DefaultPaths.TempDirectory)"
                }
            } else {
                Write-Log "Temp directory validated: $($script:Config.DefaultPaths.TempDirectory)" -Level Success
            }
        }
        
        # Report results
        foreach ($warning in $warnings) {
            Write-Log $warning -Level Warning
        }
        
        if ($issues.Count -gt 0) {
            foreach ($issue in $issues) {
                Write-Log $issue -Level Error
            }
            return $false
        }
        
        Write-Log "Configuration paths validation completed" -Level Success
        return $true
    }
    catch {
        Write-Log "Error during path validation: $($_.Exception.Message)" -Level Error
        return $false
    }
}
function Write-ExecutionSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Result
    )
    
    Write-Host "`n" -NoNewline
    Write-Host "=== EXECUTION SUMMARY ===" -ForegroundColor Cyan
    
    if ($script:Config) {
        Write-Host "Environment: $($script:Config.CurrentEnvironment)" -ForegroundColor White
        Write-Host "vCenter Server: $($script:Config.vCenterServer)" -ForegroundColor White
    }
    
    Write-Host "Execution ID: $script:ExecutionId" -ForegroundColor White
    Write-Host "Execution Time: $($Result.ExecutionTime)" -ForegroundColor White
    Write-Host "Exit Code: $($Result.ExitCode)" -ForegroundColor $(if ($Result.ExitCode -eq 0) { "Green" } else { "Red" })
    
    if ($script:TranscriptPath) {
        Write-Host "Launcher Log: $script:TranscriptPath" -ForegroundColor White
    }
    
    if ($script:Config -and $script:Config.DataPaths.LogDirectory) {
        $mainLogPattern = Join-Path $script:Config.DataPaths.LogDirectory "*$script:ExecutionId*.log"
        $logFiles = Get-ChildItem -Path $mainLogPattern -ErrorAction SilentlyContinue
        if ($logFiles) {
            Write-Host "Script Logs: $($logFiles.FullName -join ', ')" -ForegroundColor White
        }
    }
    
    # Status interpretation
    switch ($Result.ExitCode) {
        0 { Write-Host "Status: SUCCESS" -ForegroundColor Green }
        1 { Write-Host "Status: COMPLETED WITH WARNINGS" -ForegroundColor Yellow }
        default { Write-Host "Status: FAILED" -ForegroundColor Red }
    }
    
    Write-Host ""
    Write-Host "Check the logs above for detailed execution results." -ForegroundColor Yellow
}

function Wait-ForUserInput {
    param([string]$Message = "Press any key to exit...")
    
    Write-Host "`n$Message" -ForegroundColor Yellow
    try {
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    catch {
        # If ReadKey fails, use Read-Host as fallback
        Read-Host "Press Enter to continue"
    }
}
#endregion

#region Handle credential management operations
if ($ListStoredCredentials) {
    if (-not (Initialize-Configuration)) {
        Write-Host "Configuration initialization failed. Cannot list credentials." -ForegroundColor Red
        return
    }
    
    Write-Host "`n=== Stored Credentials ===" -ForegroundColor Cyan
    $storedCreds = Get-StoredCredentials
    
    if ($storedCreds.Count -eq 0) {
        Write-Host "No stored credentials found." -ForegroundColor Yellow
    } else {
        $storedCreds | Format-Table -AutoSize -Property Environment, UserName, CreatedDate, vCenterServer, IsExpired
        
        $expiredCount = ($storedCreds | Where-Object { $_.IsExpired }).Count
        if ($expiredCount -gt 0) {
            Write-Host "`nFound $expiredCount expired credential(s). Use -CleanupExpiredCredentials to remove them." -ForegroundColor Yellow
        }
    }
    return
}

if ($CleanupExpiredCredentials) {
    if (-not (Initialize-Configuration)) {
        Write-Host "Configuration initialization failed. Cannot cleanup credentials." -ForegroundColor Red
        return
    }
    
    Remove-ExpiredCredentials -Force
    Write-Host "Expired credential cleanup completed." -ForegroundColor Green
    return
}

if ($ClearAllCredentials) {
    if (-not (Initialize-Configuration)) {
        Write-Host "Configuration initialization failed. Cannot clear credentials." -ForegroundColor Red
        return
    }
    
    $confirm = Read-Host "Are you sure you want to remove ALL stored credentials? (Type 'YES' to confirm)"
    if ($confirm -eq 'YES') {
        $credentialStorePath = if ($script:Config -and $script:Config.Security.CredentialStorePath) {
            $script:Config.Security.CredentialStorePath
        } else {
            Join-Path $env:USERPROFILE ".vmtags\credentials"
        }
        
        if (Test-Path $credentialStorePath) {
            Get-ChildItem -Path $credentialStorePath -Filter "*.credential*" | ForEach-Object {
                Remove-StoredCredential -CredentialPath $_.FullName.Replace('.metadata', '')
            }
            Write-Host "All stored credentials have been removed." -ForegroundColor Green
        } else {
            Write-Host "No credential store found." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Operation cancelled." -ForegroundColor Yellow
    }
    return
}

#endregion

#region Main Execution
try {
    Write-Host "=== VM Tags and Permissions Launcher v2.0 ===" -ForegroundColor Green
    Write-Host "Execution ID: $($script:ExecutionId)" -ForegroundColor Cyan
    
    # Initialize configuration
    if (-not (Initialize-Configuration)) {
        Write-Host "Configuration initialization failed. Cannot continue." -ForegroundColor Red
        Wait-ForUserInput
        return
    }
    
    # Start transcript only after config is loaded
    if ($script:Config -and $script:Config.DefaultPaths.TempDirectory) {
        $script:TranscriptPath = Join-Path $script:Config.DefaultPaths.TempDirectory "VMTagsLauncher_$script:ExecutionId.txt"
        
        try {
            Start-Transcript -Path $script:TranscriptPath -Append -ErrorAction Stop
            Write-Log "Transcript started: $($script:TranscriptPath)" -Level Info
        }
        catch {
            Write-Host "Warning: Could not start transcript: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    
    Write-Log "=== VM Tags and Permissions Launcher v2.0 Started ===" -Level Success
    Write-Log "Configuration Version: $($script:Config.Application.Version)" -Level Info
    Write-Log "PowerShell Version: $($PSVersionTable.PSVersion)" -Level Info
    Write-Log "Machine: $($env:COMPUTERNAME)" -Level Info
    Write-Log "User: $($env:USERNAME)" -Level Info
    Write-Log "Execution ID: $($script:ExecutionId)" -Level Info
    Write-Log "Environment: $($script:Config.CurrentEnvironment)" -Level Info
    Write-Log "vCenter Server: $($script:Config.vCenterServer)" -Level Info
    Write-Log "SSO Domain: $($script:Config.SSODomain)" -Level Info
    Write-Log "App Permissions CSV: $($script:Config.DataPaths.AppPermissionsCSV)" -Level Info
    Write-Log "OS Mapping CSV: $($script:Config.DataPaths.OSMappingCSV)" -Level Info
    Write-Log "Debug Logging: $($script:Config.EnvironmentSettings.EnableDebugLogging)" -Level Info
    Write-Log "Dry Run Mode: $($DryRun)" -Level Info
    
    # Run prerequisites
    if (-not (Test-Prerequisites)) {
        Write-Log "Prerequisites validation failed. Cannot continue." -Level Error
        Wait-ForUserInput
        return
    }
    
    # Get credentials (skip for dry run)
    $credential = $null
    if (-not $DryRun) {
        try {
            $credential = Get-VMCredentials
            
            if (-not (New-CredentialFile -Credential $credential)) {
                Write-Log "Failed to create credential file. Cannot continue." -Level Error
                Wait-ForUserInput
                return
            }
            
            # ADDITIONAL VALIDATION - Add this section
            Write-Log "Validating credential file before main script execution..." -Level Debug
            

            Write-Log "Validating credential path for main script..." -Level Debug
            Write-Log "  CredentialPath variable: '$($script:CredentialPath)'" -Level Debug

            if ([string]::IsNullOrEmpty($script:CredentialPath)) {
                throw "Credential path is null or empty. This indicates a problem with credential file creation."
            }

            if (-not (Test-Path $script:CredentialPath)) {
                throw "Credential metadata file not found: $($script:CredentialPath)"
            }

            # Additional validation - check if it's a valid metadata file
            try {
                $credMetadata = Import-Csv -Path $script:CredentialPath
                if (-not $credMetadata -or -not $credMetadata.CredentialFile) {
                    throw "Credential metadata file is invalid or corrupted: $($script:CredentialPath)"
                }
                
                if (-not (Test-Path $credMetadata.CredentialFile)) {
                    throw "Credential XML file referenced in metadata does not exist: $($credMetadata.CredentialFile)"
                }
                
                Write-Log "Credential files validated for main script execution" -Level Debug
                Write-Log "  Metadata: $($script:CredentialPath)" -Level Debug
                Write-Log "  XML File: $($credMetadata.CredentialFile)" -Level Debug
            }
            catch {
                throw "Failed to validate credential files: $($_.Exception.Message)"
            }
            
            if (-not (Test-Path $script:CredentialPath)) {
                Write-Log "ERROR: Credential metadata file does not exist: $($script:CredentialPath)" -Level Error
                Wait-ForUserInput
                return
            }
            
            # Test reading the metadata
            try {
                $testMetadata = Import-Csv -Path $script:CredentialPath
                if (-not $testMetadata -or -not $testMetadata.CredentialFile) {
                    Write-Log "ERROR: Credential metadata is invalid or empty" -Level Error
                    Wait-ForUserInput
                    return
                }
                
                if (-not (Test-Path $testMetadata.CredentialFile)) {
                    Write-Log "ERROR: Credential XML file does not exist: $($testMetadata.CredentialFile)" -Level Error
                    Wait-ForUserInput
                    return
                }
                
                Write-Log "Credential files validated successfully" -Level Success
                Write-Log "  Metadata file: $($script:CredentialPath)" -Level Debug
                Write-Log "  Credential file: $($testMetadata.CredentialFile)" -Level Debug
            }
            catch {
                Write-Log "ERROR: Failed to validate credential files: $($_.Exception.Message)" -Level Error
                Wait-ForUserInput
                return
            }
        }
        catch {
            Write-Log "Credential collection failed: $($_.Exception.Message)" -Level Error
            Wait-ForUserInput
            return
        }
    }

        # Execute main script
    Write-Log "Starting main script execution..." -Level Info
    $result = Start-MainScript

    #Automatic log cleanup if configured
    if ($script:Config.Logging -and $script:Config.Logging.MaxLogFiles) {
        try {
            Remove-OldLogFiles -MaxLogFiles $script:Config.Logging.MaxLogFiles
        }
        catch {
            Write-Log "Automatic log cleanup failed: $($_.Exception.Message)" -Level Warning
        }
    }
    # Report results
    Write-ExecutionSummary -Result $result
    
    if ($result.ExitCode -eq 0) {
        Write-Log "=== VM Tags and Permissions Launcher Completed Successfully ===" -Level Success
    } else {
        Write-Log "=== VM Tags and Permissions Launcher Completed with Issues ===" -Level Warning
    }
    
    # Don't automatically exit - let user see results
    if (-not $DryRun) {
        Wait-ForUserInput "Press any key to exit..."
    }
    # Clean up expired credentials automatically if configured
    if ($script:Config.Security.AutoCleanupExpiredCredentials) {
        Remove-ExpiredCredentials
    }

    # Handle log cleanup operation
    if ($CleanupLogs) {
        if (-not (Initialize-Configuration)) {
            Write-Host "Configuration initialization failed. Cannot cleanup logs." -ForegroundColor Red
            return
        }
        
        Write-Host "`n=== Log Cleanup ===" -ForegroundColor Cyan
        
        $maxFiles = if ($script:Config -and $script:Config.Logging.MaxLogFiles) {
            $script:Config.Logging.MaxLogFiles
        } else {
            5  # Default to 5 files
        }
        
        Write-Host "Cleaning up logs (keeping most recent $($maxFiles) files per environment)..." -ForegroundColor Yellow
        
        # Ask user if they want to clean current environment only or all environments
        $scope = Read-Host "Clean logs for current environment only ($($Environment)) or All environments? (C/A)"
        
        if ($scope -eq 'A' -or $scope -eq 'a') {
            Remove-OldLogFilesAllEnvironments -MaxLogFiles $maxFiles -Force
        } else {
            Remove-OldLogFiles -MaxLogFiles $maxFiles -Force
        }
        
        Write-Host "Log cleanup completed." -ForegroundColor Green
        return
    }

}
catch {
    Write-Host "LAUNCHER FATAL ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    
    # Report failure with safe values
    $failureResult = @{
        ExitCode = 1
        ExecutionTime = "ERROR"
    }
    
    try {
        Write-ExecutionSummary -Result $failureResult
    }
    catch {
        Write-Host "Could not generate execution summary: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Don't exit immediately - let user see the error
    Wait-ForUserInput "Press any key to exit after error..."
}
finally {
    # Cleanup
    try {
        Remove-TempFiles
    }
    catch {
        Write-Host "Warning: Cleanup failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    # Stop transcript
    if ($script:TranscriptPath) {
        try {
            Stop-Transcript
            Write-Host "Transcript saved to: $script:TranscriptPath" -ForegroundColor Cyan
        }
        catch {
            # Transcript might not be active
            Write-Host "Note: Transcript may not have been saved properly" -ForegroundColor Yellow
        }
    }
    
    # Remove configuration module
    try {
        Remove-Module VMTagsConfigManager -Force -ErrorAction SilentlyContinue
    }
    catch {
        # Module might not be loaded
    }
    
    # Clear sensitive variables
    try {
        if (Get-Variable -Name credential -Scope Script -ErrorAction SilentlyContinue) {
            Remove-Variable -Name credential -Scope Script -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        # Variable might not exist
    }
}
#endregion