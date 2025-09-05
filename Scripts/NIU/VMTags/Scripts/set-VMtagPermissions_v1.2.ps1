<#
.SYNOPSIS
    Automates the configuration of vCenter VM tags and permissions based on two separate CSV input files.
.DESCRIPTION
    This script provides a powerful and repeatable way to manage vCenter VM tags and permissions.
    It connects to a specified vCenter Server and reads its configuration from two distinct CSV files:
    1.  App Permissions CSV: Contains mappings for application-specific tags to specific roles and security groups.
    2.  OS Mapping CSV: Defines how to tag VMs based on their Guest OS name. It maps OS patterns (e.g., "Microsoft Windows Server.*")
       to a target OS tag, a role, and an administrative security group.
    The script features robust logging, pre-flight checks, and ensures all connections are properly closed upon completion.
.PARAMETER vCenterServer
    The FQDN or IP address of the vCenter Server to connect to.
.PARAMETER Credential
    A PSCredential object for authenticating to the vCenter Server and its SSO domain.
.PARAMETER AppPermissionsCsvPath
    The full path to the CSV file containing application-specific permission data.
    Required columns: 'TagCategory', 'TagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName'.
.PARAMETER OsMappingCsvPath
    The full path to the CSV file that maps Guest OS patterns to tags and permissions.
    Required columns: 'GuestOSPattern', 'TargetTagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName'.
.PARAMETER Environment
    Specifies the operational environment (e.g., DEV, PROD). This determines which tag categories are used.
.PARAMETER EnableScriptDebug
    A switch parameter to enable verbose debug-level logging.
.EXAMPLE
    # Execute for the PROD environment using separate CSV files.
    $cred = Get-Credential
    .\Set-vCenterObjects_Tag_Assigments.ps1 -vCenterServer 'vcsa01.corp.local' -Credential $cred `
        -AppPermissionsCsvPath 'C:\vCenter\App-Permissions.csv' `
        -OsMappingCsvPath 'C:\vCenter\OS-Mappings.csv' `
        -Environment 'PROD' -EnableScriptDebug
.NOTES
    REQUIREMENTS:
    - VMware PowerCLI module v12 or higher must be installed.
    - Valid CSV files are required for input.
#>
[CmdletBinding()]
Param(
    [Parameter(Mandatory = $true, HelpMessage = "vCenter Server name or IP")]
    [string]$vCenterServer,
    
    # Modified to accept either credential object or credential file path
    [Parameter(Mandatory = $false, HelpMessage = "Credential object for vCenter and SSO")]
    [System.Management.Automation.PSCredential]$Credential,
    
    [Parameter(Mandatory = $false, HelpMessage = "Path to credential file (alternative to Credential parameter)")]
    [string]$CredentialPath,
    
    [Parameter(Mandatory = $true, HelpMessage = "Path to the CSV file for App-team permissions.")]
    [string]$AppPermissionsCsvPath,

    [Parameter(Mandatory = $false, HelpMessage = "Log directory override from launcher")]
    [string]$LogDirectory,
    
    [Parameter(Mandatory = $true, HelpMessage = "Path to the CSV file for OS pattern mapping.")]
    [string]$OsMappingCsvPath,
    
    [Parameter(Mandatory = $true, HelpMessage = "Environment (e.g., DEV, PROD, KLEB, OT) to determine category names")]
    [ValidateSet('DEV', 'PROD', 'KLEB', 'OT')]
    [string]$Environment,
    
    [Parameter(HelpMessage = "Enable detailed script debug logging")]
    [switch]$EnableScriptDebug
)

#region A) Credential Loading and Configs
# Add this credential loading logic at the beginning of your script
if ($CredentialPath) {
    # Handle special dry run mode
    if ($CredentialPath -eq "DRYRUN_MODE") {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Running in dry run mode - skipping credential loading" -ForegroundColor Yellow
        # Create a dummy credential for dry run mode
        $securePassword = ConvertTo-SecureString "DryRunPassword" -AsPlainText -Force
        $Credential = New-Object System.Management.Automation.PSCredential("DryRunUser", $securePassword)
    }
    elseif (Test-Path $CredentialPath) {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Loading credentials from launcher..." -ForegroundColor Green
        try {
            $credentialMetadata = Import-Csv -Path $CredentialPath
            $credentialFile = $credentialMetadata.CredentialFile
            
            if (Test-Path $credentialFile) {
                $Credential = Import-Clixml -Path $credentialFile
                Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Credentials loaded successfully for user: $($Credential.UserName)" -ForegroundColor Green
            } else {
                throw "Credential file not found: $credentialFile"
            }
        }
        catch {
            Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR] Failed to load credentials from file: $_" -ForegroundColor Red
            throw
        }
    }
    else {
        throw "Credential file path does not exist: $CredentialPath"
    }
}

if (-not $Credential) {
    throw "Either -Credential or -CredentialPath parameter must be provided"
}

# Standardized on $script: scope for all script-level variables for consistency and correctness.
$script:outputLog = @()
$script:logFolder = Join-Path (Split-Path $MyInvocation.MyCommand.Path) "Logs"
$script:ssoConnected = $false
$script:ScriptDebugEnabled = $EnableScriptDebug.IsPresent

$EnvironmentCategoryConfig = @{
    'DEV'  = @{ App = 'vCenter-DEV-App-team'; Function = 'vCenter-DEV-Function'; OS = 'vCenter-DEV-OS' };
    'PROD' = @{ App = 'vCenter-PROD-App-team'; Function = 'vCenter-PROD-Function'; OS = 'vCenter-PROD-OS' };
    'KLEB' = @{ App = 'vCenter-Kleber-App-team'; Function = 'vCenter-Kleber-Function'; OS = 'vCenter-Kleber-OS' };
    'OT'   = @{ App = 'vCenter-OT-App-team'; Function = 'vCenter-OT-Function'; OS = 'vCenter-OT-OS' };
}

# Central mapping of environment to its corresponding SSO domain. This is the single source of truth.
$EnvironmentDomainMap = @{
    'DEV'  = 'DLA-Test-Dev.local'
    'PROD' = 'DLA-Prod.local'
    'KLEB' = 'DLA-Kleber.local'
    'OT'   = 'DLA-DaytonOT.local'
}
#endregion

#region B) LOGGING - FIXED VERSION
# Determine log folder - prioritize launcher LogDirectory parameter, fallback to script directory
$script:logFolder = $null

# Method 1: Use LogDirectory parameter if provided by launcher
if ($LogDirectory -and -not [string]::IsNullOrEmpty($LogDirectory)) {
    $script:logFolder = $LogDirectory
    Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Using launcher log directory: $($script:logFolder)" -ForegroundColor Green
}
else {
    # Fallback: Try multiple methods to determine the script location
    try {
        # Method 2: Try $MyInvocation.MyCommand.Path
        if ($MyInvocation.MyCommand.Path -and -not [string]::IsNullOrEmpty($MyInvocation.MyCommand.Path)) {
            $scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
            if (-not [string]::IsNullOrEmpty($scriptDirectory)) {
                $script:logFolder = Join-Path $scriptDirectory "Logs"
                Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Using MyInvocation path for logs: $($script:logFolder)" -ForegroundColor Cyan
            }
        }
        # Method 3: Try $PSScriptRoot (PowerShell 3.0+)
        elseif ($PSScriptRoot -and -not [string]::IsNullOrEmpty($PSScriptRoot)) {
            $script:logFolder = Join-Path $PSScriptRoot "Logs"
            Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Using PSScriptRoot for logs: $($script:logFolder)" -ForegroundColor Cyan
        }
        # Method 4: Use current location
        else {
            $currentLocation = Get-Location
            if ($currentLocation -and $currentLocation.Path) {
                $script:logFolder = Join-Path $currentLocation.Path "Logs"
                Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Using current location for logs: $($script:logFolder)" -ForegroundColor Yellow
            }
        }
    }
    catch {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN ] Error determining script location: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Final fallback to temp directory if all methods failed
if (-not $script:logFolder -or [string]::IsNullOrEmpty($script:logFolder)) {
    $script:logFolder = Join-Path $env:TEMP "VMTags_Logs"
    Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN ] Using temp directory fallback for logs: $($script:logFolder)" -ForegroundColor Yellow
}

# Create a single timestamp for this script execution
$script:ExecutionTimestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$script:LogFileName = "VMTags_$($Environment)_$($script:ExecutionTimestamp).log"
$script:LogFilePath = Join-Path $script:logFolder $script:LogFileName

# Determine Reports folder - should be sibling to Logs folder
$script:reportsFolder = $null
if ($LogDirectory -and -not [string]::IsNullOrEmpty($LogDirectory)) {
    # If launcher provided log directory, create reports folder alongside it
    $parentDir = Split-Path $LogDirectory -Parent
    $script:reportsFolder = Join-Path $parentDir "Reports"
} else {
    # Fallback: create Reports folder relative to script location
    if ($script:logFolder) {
        $parentDir = Split-Path $script:logFolder -Parent
        $script:reportsFolder = Join-Path $parentDir "Reports"
    } else {
        $script:reportsFolder = Join-Path (Get-Location) "Reports"
    }
}

# Add environment subdirectory to reports folder
$script:reportsFolder = Join-Path $script:reportsFolder $Environment

# Ensure log directory exists
if (-not (Test-Path $script:logFolder)) {
    try {
        New-Item -Path $script:logFolder -ItemType Directory -Force | Out-Null
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Log folder created: $($script:logFolder)" -ForegroundColor Green
    }
    catch {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR] Failed to create log folder $($script:logFolder): $($_.Exception.Message)" -ForegroundColor Red
        # Fallback to temp directory
        $script:logFolder = $env:TEMP
        $script:LogFilePath = Join-Path $script:logFolder $script:LogFileName
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN ] Using temp directory for logs: $($script:logFolder)" -ForegroundColor Yellow
    }
}

# Ensure reports directory exists
if (-not (Test-Path $script:reportsFolder)) {
    try {
        New-Item -Path $script:reportsFolder -ItemType Directory -Force | Out-Null
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Reports folder created: $($script:reportsFolder)" -ForegroundColor Green
    }
    catch {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR] Failed to create reports folder $($script:reportsFolder): $($_.Exception.Message)" -ForegroundColor Red
        # Fallback to using log folder for reports
        $script:reportsFolder = $script:logFolder
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN ] Using log directory for reports: $($script:reportsFolder)" -ForegroundColor Yellow
    }
}

# Aria-specific logging format
function Write-AriaLog {
    param($Message, $Level = "INFO")
    
    $ariaLogEntry = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        Level = $Level
        Source = "VMTags-Automation"
        Environment = $Environment
        Message = $Message
        Machine = $env:COMPUTERNAME
        User = $env:USERNAME
    }
    
    # Output in format Aria can parse
    $ariaLogEntry | ConvertTo-Json -Compress | Write-Host
}

function Write-Log {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        [Parameter(Mandatory = $false)]
        [ValidateSet("INFO", "WARN", "ERROR", "DEBUG")]
        [string]$Level = "INFO"
    )
    
    $writeThisLog = $false
    switch ($Level.ToUpper()) {
        "INFO" { $writeThisLog = $true }
        "WARN" { $writeThisLog = $true }
        "ERROR" { $writeThisLog = $true }
        "DEBUG" {
            # Check the correct $script: scoped variable. This makes -EnableScriptDebug work.
            if ($script:ScriptDebugEnabled) {
                $writeThisLog = $true
            }
        }
    }
    
    if ($writeThisLog) {
        $logEntry = [PSCustomObject]@{
            Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            Level     = $Level.ToUpper()
            Message   = $Message
        }
        
        $script:outputLog += $logEntry
        
        $hostColor = switch ($Level.ToUpper()) {
            "INFO"  { "Green" }
            "WARN"  { "Yellow" }
            "ERROR" { "Red" }
            "DEBUG" { "Gray" }
            Default { "White" }
        }
        
        Write-Host "$($logEntry.Timestamp) [$($logEntry.Level.PadRight(5))] $($logEntry.Message)" -ForegroundColor $hostColor
        
        # Write to THE SAME log file for this execution
        try {
            "$($logEntry.Timestamp) [$($logEntry.Level.PadRight(5))] $($logEntry.Message)" | Out-File -FilePath $script:LogFilePath -Append -Encoding UTF8
        }
        catch {
            # Don't spam console with file logging errors
        }
    }
}

function Save-ExecutionLog {
    # Save the complete log at the end of execution
    try {
        $csvLogFileName = "VMTags_$($Environment)_Complete_$($script:ExecutionTimestamp).csv"
        $csvLogFilePath = Join-Path $script:reportsFolder $csvLogFileName
        
        $script:outputLog | Export-Csv -Path $csvLogFilePath -NoTypeInformation
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Complete execution log saved: $($csvLogFilePath)" -ForegroundColor Green
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO ] Text log file: $($script:LogFilePath)" -ForegroundColor Green
    }
    catch {
        Write-Host "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN ] Failed to save execution log: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Cleanup-OldLogs {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true)]
        [string]$LogFolder,
        [Parameter(Mandatory = $true)]
        [int]$MaxLogsToKeep
    )
    
    Write-Log "Cleaning up old logs in '$($LogFolder)', keeping $($MaxLogsToKeep) most recent." "DEBUG"
    
    try {
        # Validate log folder path before proceeding
        if ([string]::IsNullOrEmpty($LogFolder) -or -not (Test-Path $LogFolder)) {
            Write-Log "Log folder '$($LogFolder)' does not exist or is invalid, skipping cleanup." "WARN"
            return
        }
        
        # Clean up .log files
        $logFiles = Get-ChildItem -Path $LogFolder -Filter "*.log" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
        if ($logFiles -and $logFiles.Count -gt $MaxLogsToKeep) {
            $filesToRemove = $logFiles | Select-Object -Skip $MaxLogsToKeep
            foreach ($file in $filesToRemove) {
                Write-Log "Removing old log file: $($file.FullName)" "DEBUG"
                Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
            }
            Write-Log "Removed $($filesToRemove.Count) old log files" "DEBUG"
        }
        
        # Clean up .csv files
        $csvFiles = Get-ChildItem -Path $LogFolder -Filter "*.csv" -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
        if ($csvFiles -and $csvFiles.Count -gt $MaxLogsToKeep) {
            $filesToRemove = $csvFiles | Select-Object -Skip $MaxLogsToKeep
            foreach ($file in $filesToRemove) {
                Write-Log "Removing old CSV file: $($file.FullName)" "DEBUG"
                Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
            }
            Write-Log "Removed $($filesToRemove.Count) old CSV files" "DEBUG"
        }
        
        Write-Log "Log cleanup completed successfully" "DEBUG"
    }
    catch {
        Write-Log "Error during log cleanup: $($_.Exception.Message)" "WARN"
    }
}

# Initialize logging
Write-Log "Script started." "INFO"
Write-Log "Log directory: $($script:logFolder)" "INFO"
Write-Log "Log file: $($script:LogFileName)" "INFO"

# Perform log cleanup
try {
    Cleanup-OldLogs -LogFolder $script:logFolder -MaxLogsToKeep 5
}
catch {
    Write-Log "Log cleanup failed: $($_.Exception.Message)" "WARN"
}

Write-Log "Environment: $($Environment)" "INFO"
Write-Log "vCenter Server: $($vCenterServer)" "INFO"
Write-Log "Script Debug Enabled: $($script:ScriptDebugEnabled)" "INFO"
#endregion

#region C) SSO & OTHER HELPER FUNCTIONS
function Test-SsoModuleAvailable {
    #//-- FIXED --// Corrected the SSO module name for modern PowerCLI versions (v12+).
    $ssoModuleName = "VMware.vSphere.SsoAdmin"
    Write-Log "Checking for SSO module '$($ssoModuleName)'..." "DEBUG"
    try {
        if (Get-Module -Name $ssoModuleName -ListAvailable) {
            if (-not (Get-Module -Name $ssoModuleName)) {
                Import-Module $ssoModuleName -ErrorAction Stop | Out-Null
            }
            return $true
        }
        Write-Log "SSO module '$($ssoModuleName)' not found." "DEBUG"
        return $false
    }
    catch {
        Write-Log "Error checking/importing SSO module: $_" "WARN"
        return $false
    }
}
function Connect-SsoAdmin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Server,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    Write-Log "Attempting to connect to SSO Admin server '$($Server)'..." "DEBUG"
    $script:ssoConnected = $false
    try {
        Connect-SsoAdminServer -Server $Server -Credential $Credential -ErrorAction Stop | Out-Null
        $script:ssoConnected = $true
        Write-Log "Successfully connected to SSO Admin server." "INFO"
        return $true
    }
    catch {
        Write-Log "Failed to connect to SSO Admin server '$($Server)': $_" "ERROR"
        return $false
    }
}

function Test-VMOSDetection {
    param([string]$VMName = "*")
    
    Write-Log "=== VM OS Detection Diagnostic ===" "INFO"
    $vms = Get-VM -Name $VMName | Where-Object Name -notmatch '^(vCLS|VLC|stCtlVM)' | Select-Object -First 10
    
    foreach ($vm in $vms) {
        $osInfo = Get-VMOSInformation -VM $vm
        
        Write-Log "VM: $($osInfo.VMName)" "INFO"
        Write-Log "  Power State: $($osInfo.PowerState)" "INFO"
        Write-Log "  VMware Tools: $($osInfo.VMwareToolsStatus)" "INFO"
        Write-Log "  Guest OS (Tools): $($osInfo.GuestOS)" "INFO"
        Write-Log "  Configured OS: $($osInfo.ConfiguredOS)" "INFO"
        Write-Log "  Guest ID: $($osInfo.GuestID)" "INFO"
        Write-Log "  Has OS Info: $($osInfo.HasOSInfo)" "INFO"
        Write-Log "  ---" "INFO"
    }
}

function Get-VMOSInformation {
    param([VMware.VimAutomation.ViCore.Types.V1.Inventory.VirtualMachine]$VM)
    
    $osInfo = @{
        VMName = $VM.Name
        PowerState = $VM.PowerState
        GuestOS = $null
        ConfiguredOS = $null
        GuestID = $null
        VMwareToolsStatus = $null
        HasOSInfo = $false
    }
    
    # Get VMware Tools status
    try {
        $osInfo.VMwareToolsStatus = $VM.ExtensionData.Guest.ToolsStatus
    }
    catch {
        $osInfo.VMwareToolsStatus = "Unknown"
    }
    
    # Get Guest OS (from VMware Tools)
    if (-not [string]::IsNullOrWhiteSpace($VM.Guest.OSFullName)) {
        $osInfo.GuestOS = $VM.Guest.OSFullName
        $osInfo.HasOSInfo = $true
    }
    
    # Get Configured OS (from VM settings)
    if (-not [string]::IsNullOrWhiteSpace($VM.ExtensionData.Config.GuestFullName)) {
        $osInfo.ConfiguredOS = $VM.ExtensionData.Config.GuestFullName
        $osInfo.HasOSInfo = $true
    }
    
    # Get Guest ID
    if (-not [string]::IsNullOrWhiteSpace($VM.ExtensionData.Config.GuestId)) {
        $osInfo.GuestID = $VM.ExtensionData.Config.GuestId
        $osInfo.HasOSInfo = $true
    }
    
    return $osInfo
}

function Test-SsoGroupExistsSimple {
    param(
        [string]$Domain,
        [string]$GroupName
    )
    if (-not $script:ssoConnected) {
        Write-Log "SSO is not connected. Cannot check group existence for '$($Domain)\$($GroupName)'." "WARN"
        return $true # Fails open - assume it exists if we can't check
    }
    Write-Log "Checking SSO group existence for '$($GroupName)' in domain '$($Domain)'..." "DEBUG"
    try {
        Get-SsoGroup -Name $GroupName -Domain $Domain -ErrorAction Stop | Out-Null
        Write-Log "SSO group '$($GroupName)@$($Domain)' found." "DEBUG"
        return $true
    }
    catch {
        Write-Log "SSO group '$($GroupName)@$($Domain)' not found: $($_.Exception.Message)" "ERROR"
        return $false
    }
}
function Ensure-TagCategory {
    param([string]$CategoryName, [string]$Description = "Managed by script", [string]$Cardinality = "MULTIPLE", [string[]]$EntityType = @("VirtualMachine"))
    $existingCat = Get-TagCategory -Name $CategoryName -ErrorAction SilentlyContinue
    if ($existingCat) { return $existingCat }
    Write-Log "Category '$($CategoryName)' not found, creating..." "INFO"
    try {
        return New-TagCategory -Name $CategoryName -Description $Description -Cardinality $Cardinality -EntityType $EntityType -ErrorAction Stop
    }
    catch {
        Write-Log "Failed to create category '$($CategoryName)': $_" "ERROR"
        return $null
    }
}
function Ensure-Tag {
    param([string]$TagName, [VMware.VimAutomation.ViCore.Types.V1.Tagging.TagCategory]$Category)
    $existingTag = Get-Tag -Name $TagName -Category $Category -ErrorAction SilentlyContinue
    if ($existingTag) { return $existingTag }
    Write-Log "Tag '$($TagName)' not found in category '$($Category.Name)', creating..." "INFO"
    try {
        return New-Tag -Name $TagName -Category $Category -Description "Managed by script" -ErrorAction Stop
    }
    catch {
        Write-Log "Failed to create tag '$($TagName)' in category '$($Category.Name)': $_" "ERROR"
        return $null
    }
}
function Clone-RoleFromSupportAdminTemplate {
    param([string]$NewRoleName)
    $templateRole = Get-VIRole -Name "Support Admin Template" -ErrorAction SilentlyContinue
    if (-not $templateRole) {
        Write-Log "Template role 'SupportAdmin' not found. Cannot clone role." "ERROR"
        return $null
    }
    try {
        Write-Log "Cloning role '$($NewRoleName)' from template 'SupportAdmin'..." "INFO"
        return New-VIRole -Name $NewRoleName -Privilege (Get-viPrivilege -Role $templateRole) -ErrorAction Stop
    }
    catch {
        Write-Log "Failed to clone role for '$($NewRoleName)': $_" "ERROR"
        return $null
    }
}
function Assign-PermissionIfNeeded {
    param(
        [psobject]$VM, 
        [string]$Principal, 
        [string]$RoleName
    )
    
    Write-Log "Checking permission: VM='$($VM.Name)', Principal='$($Principal)', Role='$($RoleName)'" "DEBUG"
    
    try {
        # Check if role exists, create if needed
        $role = Get-VIRole -Name $RoleName -ErrorAction SilentlyContinue
        if (-not $role) {
            $role = Clone-RoleFromSupportAdminTemplate -NewRoleName $RoleName
        }
        if (-not $role) { 
            throw "Could not find or create role '$($RoleName)'." 
        }
        
        # Check for existing permissions - FIXED LOGIC
        $existingPermissions = Get-VIPermission -Entity $VM -ErrorAction SilentlyContinue
        
        # Look for exact match: same principal AND same role
        $duplicatePermission = $existingPermissions | Where-Object {
            $_.Principal -eq $Principal -and $_.Role -eq $RoleName
        }
        
        if ($duplicatePermission) {
            Write-Log "Permission already exists: VM='$($VM.Name)', Principal='$($Principal)', Role='$($RoleName)' - SKIPPING" "DEBUG"
            return @{
                Action = "Skipped"
                Reason = "Permission already exists"
                Principal = $Principal
                Role = $RoleName
                Propagate = $duplicatePermission.Propagate
            }
        }
        
        # Check for conflicting permissions (same principal, different role)
        $conflictingPermission = $existingPermissions | Where-Object {
            $_.Principal -eq $Principal -and $_.Role -ne $RoleName
        }
        
        if ($conflictingPermission) {
            Write-Log "WARNING: Conflicting permission found for VM='$($VM.Name)', Principal='$($Principal)'. Existing Role='$($conflictingPermission.Role)', New Role='$($RoleName)'" "WARN"
            # You can choose to skip or replace - for now we'll skip
            return @{
                Action = "Skipped"
                Reason = "Conflicting permission exists"
                Principal = $Principal
                ExistingRole = $conflictingPermission.Role
                RequestedRole = $RoleName
            }
        }
        
        # Assign the new permission
        Write-Log "Assigning permission: VM='$($VM.Name)', Principal='$($Principal)', Role='$($role.Name)'" "INFO"
        $newPermission = New-VIPermission -Entity $VM -Principal $Principal -Role $role -Propagate:$false -ErrorAction Stop
        
        return @{
            Action = "Created"
            Principal = $Principal
            Role = $RoleName
            Propagate = $false
            PermissionObject = $newPermission
        }
    }
    catch {
        Write-Log "Failed to assign permission for '$($Principal)' on VM '$($VM.Name)': $_" "ERROR"
        return @{
            Action = "Failed"
            Principal = $Principal
            Role = $RoleName
            Error = $_.Exception.Message
        }
    }
}

function Find-VMsWithoutExplicitPermissions {
    param(
        [array]$VMs,
        [string]$Environment
    )
    
    Write-Log "=== Analyzing VMs for Missing Explicit Permissions ===" "INFO"
    
    $vmsWithoutPermissions = @()
    $vmsWithOnlyInherited = @()
    $vmsWithExplicit = @()
    $totalChecked = 0
    
    foreach ($vm in $VMs) {
        $totalChecked++
        
        try {
            # Get all permissions for this VM
            $allPermissions = Get-VIPermission -Entity $vm -ErrorAction SilentlyContinue
            
            if (-not $allPermissions -or $allPermissions.Count -eq 0) {
                # No permissions at all (very unusual)
                $vmsWithoutPermissions += [PSCustomObject]@{
                    VMName = $vm.Name
                    PowerState = $vm.PowerState
                    GuestOS = $vm.Guest.OSFullName
                    ConfiguredOS = $vm.ExtensionData.Config.GuestFullName
                    Folder = $vm.Folder.Name
                    Issue = "No permissions found"
                    InheritedPermissions = 0
                    ExplicitPermissions = 0
                }
                Write-Log "VM '$($vm.Name)' has NO permissions (unusual)" "WARN"
                continue
            }
            
            # Separate inherited vs explicit permissions
            $inheritedPermissions = $allPermissions | Where-Object { $_.Propagate -eq $true }
            $explicitPermissions = $allPermissions | Where-Object { $_.Propagate -eq $false }
            
            if ($explicitPermissions.Count -eq 0) {
                # Only inherited permissions
                $vmsWithOnlyInherited += [PSCustomObject]@{
                    VMName = $vm.Name
                    PowerState = $vm.PowerState
                    GuestOS = $vm.Guest.OSFullName
                    ConfiguredOS = $vm.ExtensionData.Config.GuestFullName
                    Folder = $vm.Folder.Name
                    Issue = "Only inherited permissions"
                    InheritedPermissions = $inheritedPermissions.Count
                    ExplicitPermissions = 0
                    InheritedRoles = ($inheritedPermissions | ForEach-Object { "$($_.Principal):$($_.Role)" }) -join "; "
                }
                Write-Log "VM '$($vm.Name)' has only inherited permissions ($($inheritedPermissions.Count) inherited)" "WARN"
            }
            else {
                # Has explicit permissions
                $vmsWithExplicit += [PSCustomObject]@{
                    VMName = $vm.Name
                    PowerState = $vm.PowerState
                    InheritedPermissions = $inheritedPermissions.Count
                    ExplicitPermissions = $explicitPermissions.Count
                    ExplicitRoles = ($explicitPermissions | ForEach-Object { "$($_.Principal):$($_.Role)" }) -join "; "
                }
                Write-Log "VM '$($vm.Name)' has $($explicitPermissions.Count) explicit and $($inheritedPermissions.Count) inherited permissions" "DEBUG"
            }
        }
        catch {
            Write-Log "Error checking permissions for VM '$($vm.Name)': $_" "ERROR"
        }
        
        # Progress reporting
        if ($totalChecked % 50 -eq 0) {
            Write-Log "Permission analysis progress: $totalChecked/$($VMs.Count) VMs checked" "INFO"
        }
    }
    
    # Generate summary report
    Write-Log "=== Permission Analysis Summary ===" "INFO"
    Write-Log "Total VMs Analyzed: $totalChecked" "INFO"
    Write-Log "VMs with explicit permissions: $($vmsWithExplicit.Count)" "INFO"
    Write-Log "VMs with only inherited permissions: $($vmsWithOnlyInherited.Count)" "INFO"
    Write-Log "VMs with no permissions: $($vmsWithoutPermissions.Count)" "INFO"
    
    # Process OS tagging for VMs with only inherited permissions
    if ($vmsWithOnlyInherited.Count -gt 0) {
        Write-Log "=== Processing OS Tags for VMs with Only Inherited Permissions ===" "INFO"
        
        try {
            # Get OS mapping data
            $osMappingData = Import-Csv -Path $OsMappingCsvPath
            if (-not $osMappingData -or $osMappingData.Count -eq 0) {
                Write-Log "No OS mapping data found in $OsMappingCsvPath - skipping OS tag assignment for inherited VMs" "WARN"
            } else {
                $inheritedVMsOSTagged = 0
                $inheritedVMsOSSkipped = 0
                
                foreach ($vmRecord in $vmsWithOnlyInherited) {
                    try {
                        # Get the actual VM object
                        $vm = Get-VM -Name $vmRecord.VMName -ErrorAction SilentlyContinue
                        if (-not $vm) {
                            Write-Log "VM '$($vmRecord.VMName)' not found - skipping OS tag assignment" "WARN"
                            $inheritedVMsOSSkipped++
                            continue
                        }
                        
                        # Get OS information for this VM
                        $osInfo = Get-VMOSInformation -VM $vm
                        $osToCheck = @($osInfo.Guest, $osInfo.Config) | Where-Object { $_.OSName -and $_.OSName -ne "Unknown" -and $_.OSName -ne "" }
                        
                        if ($osToCheck.Count -eq 0) {
                            Write-Log "No valid OS information found for VM '$($vm.Name)' - skipping OS tag assignment" "WARN"
                            $inheritedVMsOSSkipped++
                            continue
                        }
                        
                        $vmMatched = $false
                        
                        # Try each OS source against the mapping patterns
                        foreach ($osSource in $osToCheck) {
                            if ($vmMatched) { break }
                            
                            foreach ($osMapRow in $osMappingData) {
                                try {
                                    if ($osSource.OSName -match $osMapRow.GuestOSPattern) {
                                        Write-Log "Inherited VM '$($vm.Name)' matches OS pattern '$($osMapRow.GuestOSPattern)' using $($osSource.Source): '$($osSource.OSName)'" "INFO"
                                        
                                        $targetTagName = $osMapRow.TargetTagName
                                        $osTagObj = Get-Tag -Category $osCat -Name $targetTagName -ErrorAction SilentlyContinue
                                        if (-not $osTagObj) { 
                                            Write-Log "Target OS tag '$($targetTagName)' not found in category '$($osCat.Name)' - skipping" "ERROR"
                                            break
                                        }
                                        
                                        # Check if VM already has this OS tag
                                        $existingOSTags = Get-TagAssignment -Entity $vm -Category $osCat -ErrorAction SilentlyContinue
                                        if ($existingOSTags | Where-Object { $_.Tag.Name -eq $targetTagName }) {
                                            Write-Log "Inherited VM '$($vm.Name)' already has OS tag '$($targetTagName)' - skipping" "DEBUG"
                                        } else {
                                            # Assign the OS tag
                                            New-TagAssignment -Tag $osTagObj -Entity $vm -ErrorAction Stop
                                            Write-Log "Assigned OS tag '$($targetTagName)' to inherited VM '$($vm.Name)'" "INFO"
                                            $inheritedVMsOSTagged++
                                        }
                                        
                                        $vmMatched = $true
                                        break
                                    }
                                }
                                catch {
                                    Write-Log "Error processing OS pattern '$($osMapRow.GuestOSPattern)' for inherited VM '$($vm.Name)': $_" "ERROR"
                                }
                            }
                        }
                        
                        if (-not $vmMatched) {
                            $osNames = ($osToCheck | ForEach-Object { "$($_.Source): '$($_.OSName)'" }) -join "; "
                            Write-Log "Inherited VM '$($vm.Name)' did not match any OS patterns. Available OS info: $osNames" "WARN"
                            $inheritedVMsOSSkipped++
                        }
                    }
                    catch {
                        Write-Log "Error processing inherited VM '$($vmRecord.VMName)' for OS tagging: $_" "ERROR"
                        $inheritedVMsOSSkipped++
                    }
                }
                
                Write-Log "OS tag processing for inherited VMs completed: $inheritedVMsOSTagged tagged, $inheritedVMsOSSkipped skipped" "INFO"
            }
        }
        catch {
            Write-Log "Error during OS tag processing for inherited VMs: $_" "ERROR"
        }
    }
    
    # Save detailed reports - FIXED FILE NAMING
    try {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        
        if ($vmsWithOnlyInherited.Count -gt 0) {
            $inheritedOnlyReport = Join-Path $script:reportsFolder "VMsWithOnlyInheritedPermissions_$($Environment)_$timestamp.csv"
            $vmsWithOnlyInherited | Export-Csv -Path $inheritedOnlyReport -NoTypeInformation
            Write-Log "VMs with only inherited permissions saved to: $inheritedOnlyReport" "INFO"
        }
        
        if ($vmsWithoutPermissions.Count -gt 0) {
            $noPermissionsReport = Join-Path $script:reportsFolder "VMsWithNoPermissions_$($Environment)_$timestamp.csv"
            $vmsWithoutPermissions | Export-Csv -Path $noPermissionsReport -NoTypeInformation
            Write-Log "VMs with no permissions saved to: $noPermissionsReport" "INFO"
        }
        
        if ($vmsWithExplicit.Count -gt 0) {
            $explicitPermissionsReport = Join-Path $script:reportsFolder "VMsWithExplicitPermissions_$($Environment)_$timestamp.csv"
            $vmsWithExplicit | Export-Csv -Path $explicitPermissionsReport -NoTypeInformation
            Write-Log "VMs with explicit permissions saved to: $explicitPermissionsReport" "INFO"
        }
    }
    catch {
        Write-Log "Failed to save permission analysis reports: $_" "ERROR"
    }
    
    return @{
        WithoutPermissions = $vmsWithoutPermissions
        OnlyInherited = $vmsWithOnlyInherited
        WithExplicit = $vmsWithExplicit
        TotalChecked = $totalChecked
    }
}

function Track-PermissionAssignment {
    param($Result, $VM, $Source)
    
    if (-not $script:PermissionResults) {
        $script:PermissionResults = @()
    }
    
    $script:PermissionResults += [PSCustomObject]@{
        VMName = $VM.Name
        PowerState = $VM.PowerState
        Source = $Source  # "AppPermissions" or "OSMapping"
        Action = $Result.Action
        Principal = $Result.Principal
        Role = $Result.Role
        Reason = $Result.Reason
        Error = $Result.Error
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
}
#endregion

#region I) MAIN EXECUTION
# Initialize tracking variables
$script:PermissionResults = @()
$script:ExecutionSummary = @{
    TagsCreated = 0
    TagsAssigned = 0
    PermissionsAssigned = 0
    PermissionsSkipped = 0
    PermissionsFailed = 0
    VMsProcessed = 0
    VMsSkipped = 0
    ErrorsEncountered = 0
}

try {
    # --- Pre-flight Checks and Connections ---
    Write-Log "Starting preflight checks..." "INFO"
    
    if (-not $EnvironmentCategoryConfig.ContainsKey($Environment)) {
        throw "Environment '$($Environment)' is not defined in EnvironmentCategoryConfig."
    }
    
    $config = $EnvironmentCategoryConfig[$Environment]
    $AppCategoryName = $config.App
    $FunctionCategoryName = $config.Function
    $OsCategoryName = $config.OS
    
    Write-Log "Environment set to '$($Environment)'. Using categories: App='$($AppCategoryName)', OS='$($OsCategoryName)'" "INFO"
    
    # Determine the current SSO Domain from the environment map
    if (-not $EnvironmentDomainMap.ContainsKey($Environment)) {
        throw "SSO Domain for environment '$($Environment)' is not defined in EnvironmentDomainMap."
    }
    
    $currentSsoDomain = $EnvironmentDomainMap[$Environment]
    Write-Log "Using SSO domain '$($currentSsoDomain)' for OS permission assignments in this environment." "INFO"
    
    # Test network connectivity
    Write-Log "Testing connectivity to vCenter '$($vCenterServer)'..." "INFO"
    if (-not (Test-NetConnection $vCenterServer -Port 443 -InformationLevel Quiet -ErrorAction SilentlyContinue)) {
        throw "Cannot reach vCenter '$($vCenterServer)' on port 443."
    }
    Write-Log "Network connectivity confirmed." "INFO"
    
    # Disconnect any existing vCenter sessions
    if ($global:DefaultVIServers.Count -gt 0) {
        Write-Log "Disconnecting existing vCenter sessions..." "INFO"
        Disconnect-VIServer -Server * -Confirm:$false -Force -ErrorAction SilentlyContinue
    }
    
    # Connect to vCenter
    Write-Log "Connecting to vCenter '$($vCenterServer)'..." "INFO"
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
    $vc = Connect-VIServer -Server $vCenterServer -Credential $Credential -ErrorAction Stop
    Write-Log "Connected to vCenter $($vc.Name) (v$($vc.Version))." "INFO"
    
    # Connect to SSO if available
    if (Test-SsoModuleAvailable) {
        Connect-SsoAdmin -Server $vCenterServer -Credential $Credential
    } else {
        Write-Log "SSO Admin module not available. Group existence checks will be skipped." "WARN"
    }
    
    # --- Data Import and Validation ---
    Write-Log "Importing and validating CSV data..." "INFO"
    
    if (-not (Test-Path $AppPermissionsCsvPath)) { 
        throw "Application Permissions CSV not found: $AppPermissionsCsvPath" 
    }
    if (-not (Test-Path $OsMappingCsvPath)) { 
        throw "OS Mapping CSV not found: $OsMappingCsvPath" 
    }
    
    $appPermissionData = Import-Csv -Path $AppPermissionsCsvPath
    $osMappingData = Import-Csv -Path $OsMappingCsvPath
    
    Write-Log "Imported $($appPermissionData.Count) rows from App Permissions CSV." "INFO"
    Write-Log "Imported $($osMappingData.Count) rows from OS Mapping CSV." "INFO"
    
    # Validate CSV data
    $requiredAppColumns = @('TagCategory', 'TagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName')
    $requiredOsColumns = @('GuestOSPattern', 'TargetTagName', 'RoleName', 'SecurityGroupDomain', 'SecurityGroupName')
    
    $appColumns = ($appPermissionData | Get-Member -MemberType NoteProperty).Name
    $osColumns = ($osMappingData | Get-Member -MemberType NoteProperty).Name
    
    foreach ($column in $requiredAppColumns) {
        if ($column -notin $appColumns) {
            throw "Required column '$column' missing from App Permissions CSV"
        }
    }
    
    foreach ($column in $requiredOsColumns) {
        if ($column -notin $osColumns) {
            throw "Required column '$column' missing from OS Mapping CSV"
        }
    }
    
    Write-Log "CSV data validation completed successfully." "INFO"
    
    # --- Ensure Tag Categories Exist ---
    Write-Log "Ensuring tag categories exist..." "INFO"
    
    $appCat = Ensure-TagCategory -CategoryName $AppCategoryName
    $osCat = Ensure-TagCategory -CategoryName $OsCategoryName
    
    # For Function category - use existing instead of creating
    $functionCat = Get-TagCategory -Name $FunctionCategoryName -ErrorAction SilentlyContinue
    if (-not $functionCat) {
        Write-Log "Function category '$($FunctionCategoryName)' not found - this is expected as we reference existing Function tags" "INFO"
    } else {
        Write-Log "Using existing Function category '$($FunctionCategoryName)' for domain controller references" "INFO"
    }
    
    if (-not $appCat) { 
        throw "FATAL: Could not create App category '$($AppCategoryName)'." 
    }
    if (-not $osCat) { 
        throw "FATAL: Could not create OS category '$($OsCategoryName)'." 
    }
    
    Write-Log "Tag categories verified/created successfully." "INFO"
    
    # --- Processing Part 1: Application Permissions ---
    Write-Log "=== Processing Application Permissions from $($AppPermissionsCsvPath) ===" "INFO"
    
    $appRowsProcessed = 0
    $appTagsCreated = 0
    $appPermissionsProcessed = 0
    
    foreach ($row in $appPermissionData) {
        $appRowsProcessed++
        
        if ($row.TagCategory -ieq $AppCategoryName) {
            Write-Log "Processing App row: Tag='$($row.TagName)', Role='$($row.RoleName)'" "DEBUG"
            
            try {
                # Ensure the tag exists
                $tagObj = Ensure-Tag -TagName $row.TagName -Category $appCat
                if (-not $tagObj) { 
                    Write-Log "Failed to create/find tag '$($row.TagName)' - skipping" "ERROR"
                    $script:ExecutionSummary.ErrorsEncountered++
                    continue 
                }
                
                if ($tagObj.Name -eq $row.TagName -and -not (Get-Tag -Name $row.TagName -Category $appCat -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $row.TagName })) {
                    $appTagsCreated++
                    $script:ExecutionSummary.TagsCreated++
                }
                
                # Build principal name
                $principal = "$($row.SecurityGroupDomain)\$($row.SecurityGroupName)"
                
                # Validate security group exists (if SSO is connected)
                if (-not (Test-SsoGroupExistsSimple -Domain $row.SecurityGroupDomain -GroupName $row.SecurityGroupName)) {
                    Write-Log "Skipping permissions for principal '$($principal)' as SSO group was not found." "WARN"
                    continue
                }
                
                # Find VMs with this tag
                $vms = Get-VM -Tag $tagObj -ErrorAction SilentlyContinue | Where-Object Name -notmatch '^(vCLS|VLC|stCtlVM)'
                Write-Log "Found $($vms.Count) VMs with tag '$($row.TagName)'" "DEBUG"
                
                if ($vms.Count -eq 0) { 
                    Write-Log "No VMs found with tag '$($row.TagName)'" "DEBUG"
                    continue 
                }
                
                # Assign permissions to each VM
                foreach ($vm in $vms) {
                    $script:ExecutionSummary.VMsProcessed++
                    $result = Assign-PermissionIfNeeded -VM $vm -Principal $principal -RoleName $row.RoleName
                    Track-PermissionAssignment -Result $result -VM $vm -Source "AppPermissions"
                    
                    switch ($result.Action) {
                        "Created" { 
                            $appPermissionsProcessed++
                            $script:ExecutionSummary.PermissionsAssigned++
                        }
                        "Skipped" { 
                            $script:ExecutionSummary.PermissionsSkipped++
                        }
                        "Failed" { 
                            $script:ExecutionSummary.PermissionsFailed++
                            $script:ExecutionSummary.ErrorsEncountered++
                        }
                    }
                }
            } 
            catch {
                Write-Log "Error processing App row for tag '$($row.TagName)': $_" "ERROR"
                $script:ExecutionSummary.ErrorsEncountered++
            }
        }
        else {
            Write-Log "Skipping row with non-matching category: '$($row.TagCategory)' (expected: '$AppCategoryName')" "DEBUG"
        }
        
        # Progress reporting
        if ($appRowsProcessed % 10 -eq 0) {
            Write-Log "App permissions progress: $appRowsProcessed/$($appPermissionData.Count) rows processed" "INFO"
        }
    }
    
    Write-Log "App Permissions Summary: $appRowsProcessed rows processed, $appTagsCreated tags created, $appPermissionsProcessed permissions assigned" "INFO"
    
    # --- Processing Part 2: OS Tagging and Permissions ---
    Write-Log "=== Processing OS Tagging and Permissions from $($OsMappingCsvPath) ===" "INFO"
    
    # Pre-create all OS tags from mapping file
    Write-Log "Pre-creating all OS tags from mapping file..." "DEBUG"
    $osTagsCreated = 0
    
    foreach ($osMapRow in $osMappingData) {
        $osTagObj = Ensure-Tag -TagName $osMapRow.TargetTagName -Category $osCat
        if ($osTagObj -and -not (Get-Tag -Name $osMapRow.TargetTagName -Category $osCat -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $osMapRow.TargetTagName })) {
            $osTagsCreated++
            $script:ExecutionSummary.TagsCreated++
        }
    }
    
    Write-Log "Pre-created $osTagsCreated OS tags" "INFO"
    
    # Get all VMs for OS processing - excluding system and control VMs
    Write-Log "Getting all VMs for OS processing (excluding vCLS*, VLC*, stCtlVM* VMs)..." "DEBUG"
    $allVms = Get-VM | Where-Object Name -notmatch '^(vCLS|VLC|stCtlVM)'
    Write-Log "Found $($allVms.Count) VMs to check for OS patterns (after filtering system VMs)." "INFO"
    
    $osProcessedCount = 0
    $osSkippedCount = 0
    $osTaggedCount = 0
    $osPermissionCount = 0
    
    foreach ($vm in $allVms) {
        $osProcessedCount++
        
        # Try multiple sources for OS information
        $osToCheck = @()
        
        # 1. First try Guest OS (from VMware Tools - only available when VM is running)
        if (-not [string]::IsNullOrWhiteSpace($vm.Guest.OSFullName)) {
            $osToCheck += @{
                Source = "Guest OS (VMware Tools)"
                OSName = $vm.Guest.OSFullName
            }
            Write-Log "VM '$($vm.Name)' - Guest OS from Tools: '$($vm.Guest.OSFullName)'" "DEBUG"
        }
        
        # 2. Fallback to configured Guest OS (from VM settings - always available)
        if (-not [string]::IsNullOrWhiteSpace($vm.ExtensionData.Config.GuestFullName)) {
            $osToCheck += @{
                Source = "VM Configuration"
                OSName = $vm.ExtensionData.Config.GuestFullName
            }
            Write-Log "VM '$($vm.Name)' - Configured Guest OS: '$($vm.ExtensionData.Config.GuestFullName)'" "DEBUG"
        }
        
        # 3. Last resort - use Guest ID
        if (-not [string]::IsNullOrWhiteSpace($vm.ExtensionData.Config.GuestId)) {
            $osToCheck += @{
                Source = "Guest ID"
                OSName = $vm.ExtensionData.Config.GuestId
            }
            Write-Log "VM '$($vm.Name)' - Guest ID: '$($vm.ExtensionData.Config.GuestId)'" "DEBUG"
        }
        
        # If no OS information available at all
        if ($osToCheck.Count -eq 0) {
            Write-Log "VM '$($vm.Name)' - No OS information available (PowerState: $($vm.PowerState))" "WARN"
            $osSkippedCount++
            $script:ExecutionSummary.VMsSkipped++
            continue
        }
        
        $vmMatched = $false
        
        # Try each OS source against the mapping patterns
        foreach ($osInfo in $osToCheck) {
            if ($vmMatched) { break }
            
            foreach ($osMapRow in $osMappingData) {
                try {
                    if ($osInfo.OSName -match $osMapRow.GuestOSPattern) {
                        Write-Log "VM '$($vm.Name)' matches OS pattern '$($osMapRow.GuestOSPattern)' using $($osInfo.Source): '$($osInfo.OSName)'" "INFO"
                        
                        $targetTagName = $osMapRow.TargetTagName
                        $osTagObj = Get-Tag -Category $osCat -Name $targetTagName -ErrorAction SilentlyContinue
                        if (-not $osTagObj) { 
                            Write-Log "Target tag '$($targetTagName)' not found in category '$($osCat.Name)'" "ERROR"
                            $script:ExecutionSummary.ErrorsEncountered++
                            break 
                        }
                        
                        # Apply OS Tag
                        $existingTag = Get-TagAssignment -Entity $vm -Tag $osTagObj -ErrorAction SilentlyContinue
                        if (-not $existingTag) {
                            Write-Log "Applying OS tag '$($targetTagName)' to VM '$($vm.Name)'" "INFO"
                            try { 
                                New-TagAssignment -Entity $vm -Tag $osTagObj -ErrorAction Stop 
                                $osTaggedCount++
                                $script:ExecutionSummary.TagsAssigned++
                            }
                            catch { 
                                Write-Log "Failed to apply OS tag '$($targetTagName)' to VM '$($vm.Name)': $_" "ERROR" 
                                $script:ExecutionSummary.ErrorsEncountered++
                            }
                        } else {
                            Write-Log "VM '$($vm.Name)' already has OS tag '$($targetTagName)'" "DEBUG"
                        }
                        
                        # Apply Permissions - check for Domain Controller special case
                        $osPrincipal = "$($currentSsoDomain)\$($osMapRow.SecurityGroupName)"
                        $roleToAssign = $osMapRow.RoleName
                        
                        # Check if this is a Domain Controller for Windows OS admin permissions override
                        if ($osMapRow.SecurityGroupName -eq "Windows Server Team") {
                            try {
                                # Check for Domain Controller function tag
                                $functionTags = Get-TagAssignment -Entity $vm -Category $functionCat -ErrorAction SilentlyContinue
                                $isDomainController = $functionTags | Where-Object { $_.Tag.Name -eq "Domain Controller" }
                                
                                if ($isDomainController) {
                                    $roleToAssign = "ReadOnly"
                                    Write-Log "VM '$($vm.Name)' is a Domain Controller - overriding Windows Server Team permissions with ReadOnly role" "INFO"
                                } else {
                                    Write-Log "VM '$($vm.Name)' is a Windows Server (not Domain Controller) - applying full Windows Server Team permissions" "DEBUG"
                                }
                            }
                            catch {
                                Write-Log "Error checking Domain Controller function tag for VM '$($vm.Name)': $_" "WARN"
                                # Continue with original role if function tag check fails
                            }
                        }
                        
                        Write-Log "Processing permissions for VM '$($vm.Name)': Principal='$($osPrincipal)', Role='$($roleToAssign)'" "DEBUG"
                        
                        $result = Assign-PermissionIfNeeded -VM $vm -Principal $osPrincipal -RoleName $roleToAssign
                        Track-PermissionAssignment -Result $result -VM $vm -Source "OSMapping"
                        
                        switch ($result.Action) {
                            "Created" { 
                                $osPermissionCount++
                                $script:ExecutionSummary.PermissionsAssigned++
                            }
                            "Skipped" { 
                                $script:ExecutionSummary.PermissionsSkipped++
                            }
                            "Failed" { 
                                $script:ExecutionSummary.PermissionsFailed++
                                $script:ExecutionSummary.ErrorsEncountered++
                            }
                        }
                        
                        $vmMatched = $true
                        break # Match found, move to the next VM
                    }
                }
                catch {
                    Write-Log "Error processing OS pattern '$($osMapRow.GuestOSPattern)' for VM '$($vm.Name)': $_" "ERROR"
                    $script:ExecutionSummary.ErrorsEncountered++
                }
            }
        }
        
        if (-not $vmMatched) {
            $osNames = ($osToCheck | ForEach-Object { "$($_.Source): '$($_.OSName)'" }) -join "; "
            Write-Log "VM '$($vm.Name)' did not match any OS patterns. Available OS info: $osNames" "WARN"
            $osSkippedCount++
            $script:ExecutionSummary.VMsSkipped++
        }
        
        # Progress reporting
        if ($osProcessedCount % 25 -eq 0) {
            Write-Log "OS processing progress: $osProcessedCount/$($allVms.Count) VMs (Tagged: $osTaggedCount, Permissions: $osPermissionCount, Skipped: $osSkippedCount)" "INFO"
        }
    }
    
    Write-Log "OS Processing Complete - Total VMs: $($allVms.Count), Processed: $osProcessedCount, Tagged: $osTaggedCount, Permissions Assigned: $osPermissionCount, Skipped: $osSkippedCount" "INFO"
    
    # --- Permission Analysis ---
    Write-Log "=== Starting Comprehensive Permission Analysis ===" "INFO"
    $permissionAnalysis = Find-VMsWithoutExplicitPermissions -VMs $allVms -Environment $Environment
    
    # --- Process OS Tags for VMs with Only Inherited Permissions ---
    if ($permissionAnalysis.OnlyInherited.Count -gt 0) {
        Write-Log "=== Processing OS Tags for VMs with Only Inherited Permissions ===" "INFO"
        Write-Log "Found $($permissionAnalysis.OnlyInherited.Count) VMs with only inherited permissions - applying OS tags..." "INFO"
        
        $inheritedVMsOSTagged = 0
        $inheritedVMsOSSkipped = 0
        
        foreach ($vmRecord in $permissionAnalysis.OnlyInherited) {
            try {
                # Get the VM object
                $vm = Get-VM -Name $vmRecord.VMName -ErrorAction SilentlyContinue
                if (-not $vm) {
                    Write-Log "Could not find VM '$($vmRecord.VMName)' for OS tag processing" "WARN"
                    $inheritedVMsOSSkipped++
                    continue
                }
                
                # Try multiple sources for OS information (same logic as main OS processing)
                $osToCheck = @()
                
                # 1. First try Guest OS (from VMware Tools - only available when VM is running)
                if (-not [string]::IsNullOrWhiteSpace($vm.Guest.OSFullName)) {
                    $osToCheck += @{
                        Source = "Guest OS (VMware Tools)"
                        OSName = $vm.Guest.OSFullName
                    }
                    Write-Log "VM '$($vm.Name)' - Guest OS from Tools: '$($vm.Guest.OSFullName)'" "DEBUG"
                }
                
                # 2. Fallback to configured Guest OS (from VM settings - always available)
                if (-not [string]::IsNullOrWhiteSpace($vm.ExtensionData.Config.GuestFullName)) {
                    $osToCheck += @{
                        Source = "VM Configuration"
                        OSName = $vm.ExtensionData.Config.GuestFullName
                    }
                    Write-Log "VM '$($vm.Name)' - Configured Guest OS: '$($vm.ExtensionData.Config.GuestFullName)'" "DEBUG"
                }
                
                # 3. Last resort - use Guest ID
                if (-not [string]::IsNullOrWhiteSpace($vm.ExtensionData.Config.GuestId)) {
                    $osToCheck += @{
                        Source = "Guest ID"
                        OSName = $vm.ExtensionData.Config.GuestId
                    }
                    Write-Log "VM '$($vm.Name)' - Guest ID: '$($vm.ExtensionData.Config.GuestId)'" "DEBUG"
                }
                
                # If no OS information available
                if ($osToCheck.Count -eq 0) {
                    Write-Log "VM '$($vm.Name)' - No OS information available for inherited VM (PowerState: $($vm.PowerState))" "WARN"
                    $inheritedVMsOSSkipped++
                    continue
                }
                
                $vmMatched = $false
                
                # Try each OS source against the mapping patterns
                foreach ($osInfo in $osToCheck) {
                    if ($vmMatched) { break }
                    
                    foreach ($osMapRow in $osMappingData) {
                        try {
                            if ($osInfo.OSName -match $osMapRow.GuestOSPattern) {
                                Write-Log "Inherited VM '$($vm.Name)' matches OS pattern '$($osMapRow.GuestOSPattern)' using $($osInfo.Source): '$($osInfo.OSName)'" "INFO"
                                
                                $targetTagName = $osMapRow.TargetTagName
                                $osTagObj = Get-Tag -Category $osCat -Name $targetTagName -ErrorAction SilentlyContinue
                                if (-not $osTagObj) { 
                                    Write-Log "Target tag '$($targetTagName)' not found in category '$($osCat.Name)' for inherited VM processing" "ERROR"
                                    break 
                                }
                                
                                # Apply OS Tag only (no permissions since they only have inherited)
                                $existingTag = Get-TagAssignment -Entity $vm -Tag $osTagObj -ErrorAction SilentlyContinue
                                if (-not $existingTag) {
                                    Write-Log "Applying OS tag '$($targetTagName)' to inherited VM '$($vm.Name)'" "INFO"
                                    try { 
                                        New-TagAssignment -Entity $vm -Tag $osTagObj -ErrorAction Stop 
                                        $inheritedVMsOSTagged++
                                        $script:ExecutionSummary.TagsAssigned++
                                    }
                                    catch { 
                                        Write-Log "Failed to apply OS tag '$($targetTagName)' to inherited VM '$($vm.Name)': $_" "ERROR" 
                                        $script:ExecutionSummary.ErrorsEncountered++
                                    }
                                } else {
                                    Write-Log "Inherited VM '$($vm.Name)' already has OS tag '$($targetTagName)'" "DEBUG"
                                }
                                
                                $vmMatched = $true
                                break # Match found, move to next VM
                            }
                        }
                        catch {
                            Write-Log "Error processing OS pattern '$($osMapRow.GuestOSPattern)' for inherited VM '$($vm.Name)': $_" "ERROR"
                            $script:ExecutionSummary.ErrorsEncountered++
                        }
                    }
                }
                
                if (-not $vmMatched) {
                    $osNames = ($osToCheck | ForEach-Object { "$($_.Source): '$($_.OSName)'" }) -join "; "
                    Write-Log "Inherited VM '$($vm.Name)' did not match any OS patterns. Available OS info: $osNames" "WARN"
                    $inheritedVMsOSSkipped++
                }
            }
            catch {
                Write-Log "Error processing inherited VM '$($vmRecord.VMName)' for OS tagging: $_" "ERROR"
                $inheritedVMsOSSkipped++
                $script:ExecutionSummary.ErrorsEncountered++
            }
        }
        
        Write-Log "OS tag processing for inherited VMs completed: $inheritedVMsOSTagged tagged, $inheritedVMsOSSkipped skipped" "INFO"
    }
    
    # --- Generate Final Reports and Summary ---
    Write-Log "=== Generating Final Reports and Summary ===" "INFO"
    
    # Calculate final statistics
    $totalPermissionsAssigned = ($script:PermissionResults | Where-Object { $_.Action -eq "Created" }).Count
    $totalPermissionsSkipped = ($script:PermissionResults | Where-Object { $_.Action -eq "Skipped" }).Count
    $totalPermissionsFailed = ($script:PermissionResults | Where-Object { $_.Action -eq "Failed" }).Count
    
    # Generate comprehensive execution summary
    Write-Log "=== FINAL EXECUTION SUMMARY ===" "INFO"
    Write-Log "Environment: $Environment" "INFO"
    Write-Log "vCenter Server: $vCenterServer" "INFO"
    Write-Log "Execution Start Time: $(Get-Date)" "INFO"
    Write-Log "=== CSV Data Processing ===" "INFO"
    Write-Log "App Permission Rows: $($appPermissionData.Count)" "INFO"
    Write-Log "OS Mapping Rows: $($osMappingData.Count)" "INFO"
    Write-Log "=== Tag Management ===" "INFO"
    Write-Log "Tags Created: $($script:ExecutionSummary.TagsCreated)" "INFO"
    Write-Log "Tag Assignments: $($script:ExecutionSummary.TagsAssigned)" "INFO"
    Write-Log "=== VM Processing ===" "INFO"
    Write-Log "Total VMs Found: $($allVms.Count)" "INFO"
    Write-Log "VMs Successfully Processed: $($script:ExecutionSummary.VMsProcessed)" "INFO"
    Write-Log "VMs Skipped: $($script:ExecutionSummary.VMsSkipped)" "INFO"
    Write-Log "=== Permission Assignment Results ===" "INFO"
    Write-Log "Permissions Successfully Assigned: $totalPermissionsAssigned" "INFO"
    Write-Log "Permissions Skipped (duplicates/conflicts): $totalPermissionsSkipped" "INFO"
    Write-Log "Permission Assignment Failures: $totalPermissionsFailed" "INFO"
    Write-Log "=== Permission Analysis Results ===" "INFO"
    Write-Log "VMs with Explicit Permissions: $($permissionAnalysis.WithExplicit.Count)" "INFO"
    Write-Log "VMs with Only Inherited Permissions: $($permissionAnalysis.OnlyInherited.Count)" "INFO"
    Write-Log "VMs with No Permissions: $($permissionAnalysis.WithoutPermissions.Count)" "INFO"
    Write-Log "=== Error Summary ===" "INFO"
    Write-Log "Total Errors Encountered: $($script:ExecutionSummary.ErrorsEncountered)" "INFO"
    
    # Determine overall execution status
    $executionStatus = if ($script:ExecutionSummary.ErrorsEncountered -eq 0) {
        "SUCCESS"
    } elseif ($totalPermissionsAssigned -gt 0) {
        "PARTIAL SUCCESS"
    } else {
        "FAILED"
    }
    
    Write-Log "=== OVERALL STATUS: $executionStatus ===" "INFO"
    
   # --- Save Detailed Reports ---
Write-Log "=== Saving Detailed Reports ===" "INFO"

try {
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    
    # Save permission assignment results
    if ($script:PermissionResults.Count -gt 0) {
        $permissionResultsReport = Join-Path $script:reportsFolder "PermissionAssignmentResults_$($Environment)_$timestamp.csv"
        $script:PermissionResults | Export-Csv -Path $permissionResultsReport -NoTypeInformation
        Write-Log "Permission assignment results saved to: $permissionResultsReport" "INFO"
    }
    
    # Save execution summary
    $summaryReport = Join-Path $script:reportsFolder "ExecutionSummary_$($Environment)_$timestamp.csv"
    $summaryData = [PSCustomObject]@{
        Environment = $Environment
        vCenterServer = $vCenterServer
        ExecutionTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        AppPermissionRows = $appPermissionData.Count
        OSMappingRows = $osMappingData.Count
        TagsCreated = $script:ExecutionSummary.TagsCreated
        TagsAssigned = $script:ExecutionSummary.TagsAssigned
        TotalVMs = $allVms.Count
        VMsProcessed = $script:ExecutionSummary.VMsProcessed
        VMsSkipped = $script:ExecutionSummary.VMsSkipped
        PermissionsAssigned = $totalPermissionsAssigned
        PermissionsSkipped = $totalPermissionsSkipped
        PermissionsFailed = $totalPermissionsFailed
        VMsWithExplicitPermissions = $permissionAnalysis.WithExplicit.Count
        VMsWithOnlyInheritedPermissions = $permissionAnalysis.OnlyInherited.Count
        VMsWithNoPermissions = $permissionAnalysis.WithoutPermissions.Count
        ErrorsEncountered = $script:ExecutionSummary.ErrorsEncountered
        ExecutionStatus = $executionStatus
    }
    
    $summaryData | Export-Csv -Path $summaryReport -NoTypeInformation
    Write-Log "Execution summary saved to: $summaryReport" "INFO"
    
    # Save VMs that need attention (only inherited permissions or no permissions)
    $vmsNeedingAttention = @()
    $vmsNeedingAttention += $permissionAnalysis.OnlyInherited | ForEach-Object { 
        $_ | Add-Member -NotePropertyName "AttentionReason" -NotePropertyValue "Only Inherited Permissions" -PassThru
    }
    $vmsNeedingAttention += $permissionAnalysis.WithoutPermissions | ForEach-Object { 
        $_ | Add-Member -NotePropertyName "AttentionReason" -NotePropertyValue "No Permissions" -PassThru
    }
    
    if ($vmsNeedingAttention.Count -gt 0) {
        $attentionReport = Join-Path $script:reportsFolder "VMsNeedingAttention_$($Environment)_$timestamp.csv"
        $vmsNeedingAttention | Export-Csv -Path $attentionReport -NoTypeInformation
        Write-Log "VMs needing attention saved to: $attentionReport" "INFO"
        Write-Log "ATTENTION: $($vmsNeedingAttention.Count) VMs require manual review for permissions" "WARN"
    }
    
    Write-Log "All reports saved successfully to: $script:logFolder" "INFO"
}
catch {
    Write-Log "Failed to save some reports: $_" "ERROR"
    $script:ExecutionSummary.ErrorsEncountered++
}
    
    # Final status message
    if ($executionStatus -eq "SUCCESS") {
        Write-Log "Script execution completed successfully with no errors." "INFO"
    } elseif ($executionStatus -eq "PARTIAL SUCCESS") {
        Write-Log "Script execution completed with some errors, but permissions were assigned." "WARN"
    } else {
        Write-Log "Script execution completed with significant errors." "ERROR"
    }
}
catch {
    Write-Log "FATAL SCRIPT ERROR: $_" "ERROR"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" "ERROR"
    $script:ExecutionSummary.ErrorsEncountered++
    throw
}
finally {
    # --- Cleanup ---
    Write-Log "=== Starting Cleanup ===" "INFO"
    
    # Save complete execution log
    Save-ExecutionLog
    
    # Disconnect from SSO
    if ($script:ssoConnected) {
        try { 
            Disconnect-SsoAdminServer -Server $vCenterServer -ErrorAction Stop
            Write-Log "SSO disconnected." "INFO" 
        }
        catch { 
            Write-Log "SSO disconnect failed: $_" "WARN" 
        }
    }
    
    # Disconnect from vCenter
    if ($global:DefaultVIServers.Count -gt 0) {
        try { 
            Disconnect-VIServer -Server * -Confirm:$false -Force -ErrorAction Stop
            Write-Log "vCenter disconnected." "INFO" 
        }
        catch { 
            Write-Log "vCenter disconnect failed: $_" "WARN" 
        }
    }
    
    # Reset PowerCLI certificate policy
    try { 
        Set-PowerCLIConfiguration -InvalidCertificateAction Warn -Confirm:$false | Out-Null 
        Write-Log "PowerCLI certificate policy reset to Warn." "INFO"
    }
    catch { 
        Write-Log "Failed to reset certificate policy: $_" "WARN" 
    }
    
    Write-Log "Script execution finished." "INFO"
    Write-Log "Log files saved to: $script:logFolder" "INFO"
    Write-Log "Report files saved to: $script:reportsFolder" "INFO"
    
    # Display quick summary to console
    Write-Host "`n=== QUICK SUMMARY ===" -ForegroundColor Cyan
    Write-Host "Permissions Assigned: $totalPermissionsAssigned" -ForegroundColor Green
    Write-Host "Permissions Skipped: $totalPermissionsSkipped" -ForegroundColor Yellow
    Write-Host "Permission Failures: $totalPermissionsFailed" -ForegroundColor Red
    Write-Host "VMs Needing Attention: $($vmsNeedingAttention.Count)" -ForegroundColor Yellow
    Write-Host "Check logs for detailed results: $script:logFolder" -ForegroundColor White
    Write-Host "Check reports for CSV exports: $script:reportsFolder" -ForegroundColor White
}
#endregion