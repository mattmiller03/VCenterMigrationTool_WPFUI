# Invoke-VMHostConfig.ps1 - Wrapper for VMHostConfigV2.ps1
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Backup", "Restore", "Migrate")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$VMHostName,

    [Parameter(Mandatory = $false)]
    [string]$vCenter,

    [Parameter(Mandatory = $false)]
    [string]$SourceVCenter,

    [Parameter(Mandatory = $false)]
    [string]$TargetVCenter,

    [Parameter(Mandatory = $false)]
    [string]$Credential,  # Format: "username:password"

    [Parameter(Mandatory = $false)]
    [string]$SourceCredential,  # Format: "username:password"

    [Parameter(Mandatory = $false)]
    [string]$TargetCredential,  # Format: "username:password"

    [Parameter(Mandatory = $false)]
    [string]$ESXiHostCredential,  # Format: "username:password"

    [Parameter(Mandatory = $false)]
    [string]$BackupPath = (Get-Location).Path,

    [Parameter(Mandatory = $false)]
    [string]$BackupFile,

    [Parameter(Mandatory = $false)]
    [string]$LogPath = (Get-Location).Path,

    [Parameter(Mandatory = $false)]
    [string]$TargetDatacenterName,

    [Parameter(Mandatory = $false)]
    [string]$TargetClusterName,

    [Parameter(Mandatory = $false)]
    [int]$OperationTimeout = 600,

    [Parameter(Mandatory = $false)]
    [string]$UplinkPortgroupName,

    [Parameter(Mandatory = $false)]
    [switch]$BypassModuleCheck = $false
)

# Function to write structured logs
function Write-ScriptLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Information "[$timestamp] [$Level] $Message" -InformationAction Continue
}

# Function to convert credential string to PSCredential
function ConvertTo-PSCredential {
    param([string]$CredentialString)
    
    if ([string]::IsNullOrEmpty($CredentialString)) {
        return $null
    }
    
    $parts = $CredentialString.Split(':', 2)
    if ($parts.Count -eq 2) {
        $username = $parts[0]
        $password = $parts[1]
        $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
        return New-Object System.Management.Automation.PSCredential($username, $securePassword)
    }
    
    return $null
}

try {
    Write-ScriptLog "Starting VMHost configuration operation: $Action"
    Write-ScriptLog "Target host: $VMHostName"
    Write-ScriptLog "Backup path: $BackupPath"
    
    # OPTIMIZED: Only check PowerCLI if not bypassed
    if (-not $BypassModuleCheck) {
        Write-ScriptLog "Checking PowerCLI module availability..."
        
        $powerCliModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        if (-not $powerCliModule) {
            Write-ScriptLog "PowerCLI module not found" "ERROR"
            Write-Output "ERROR: PowerCLI module not available. Please install VMware.PowerCLI first."
            return
        }

        Write-ScriptLog "PowerCLI module found. Importing..."
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    } else {
        Write-ScriptLog "Bypassing PowerCLI module check (assumed available)"
        # Still try to import silently
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction SilentlyContinue
        } catch {
            # Ignore import errors when bypassing
        }
    }
    
    # Get the directory where this script is located
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $vmHostConfigScript = Join-Path $scriptDir "VMHostConfigV2.ps1"
    
    # Verify VMHostConfigV2.ps1 exists
    if (-not (Test-Path $vmHostConfigScript)) {
        Write-ScriptLog "VMHostConfigV2.ps1 not found at: $vmHostConfigScript" "ERROR"
        Write-Output "ERROR: VMHostConfigV2.ps1 script not found"
        return
    }
    
    Write-ScriptLog "Found VMHostConfigV2.ps1 at: $vmHostConfigScript"
    
    # Build parameters for VMHostConfigV2.ps1
    $vmHostParams = @{
        Action = $Action
        VMHostName = $VMHostName
        BackupPath = $BackupPath
        LogPath = $LogPath
        OperationTimeout = $OperationTimeout
    }
    
    # Add optional parameters based on action
    switch ($Action) {
        "Backup" {
            if ($vCenter) { $vmHostParams['vCenter'] = $vCenter }
            if ($Credential) { 
                $cred = ConvertTo-PSCredential -CredentialString $Credential
                if ($cred) { $vmHostParams['Credential'] = $cred }
            }
        }
        "Restore" {
            if ($vCenter) { $vmHostParams['vCenter'] = $vCenter }
            if ($BackupFile) { $vmHostParams['BackupFile'] = $BackupFile }
            if ($Credential) { 
                $cred = ConvertTo-PSCredential -CredentialString $Credential
                if ($cred) { $vmHostParams['Credential'] = $cred }
            }
        }
        "Migrate" {
            if ($SourceVCenter) { $vmHostParams['SourceVCenter'] = $SourceVCenter }
            if ($TargetVCenter) { $vmHostParams['TargetVCenter'] = $TargetVCenter }
            if ($TargetDatacenterName) { $vmHostParams['TargetDatacenterName'] = $TargetDatacenterName }
            if ($TargetClusterName) { $vmHostParams['TargetClusterName'] = $TargetClusterName }
            
            if ($SourceCredential) { 
                $cred = ConvertTo-PSCredential -CredentialString $SourceCredential
                if ($cred) { $vmHostParams['SourceCredential'] = $cred }
            }
            if ($TargetCredential) { 
                $cred = ConvertTo-PSCredential -CredentialString $TargetCredential
                if ($cred) { $vmHostParams['TargetCredential'] = $cred }
            }
            if ($ESXiHostCredential) { 
                $cred = ConvertTo-PSCredential -CredentialString $ESXiHostCredential
                if ($cred) { $vmHostParams['ESXiHostCredential'] = $cred }
            }
        }
    }
    
    # Add uplink portgroup if specified
    if ($UplinkPortgroupName) {
        $vmHostParams['UplinkPortgroupName'] = $UplinkPortgroupName
    }
    
    Write-ScriptLog "Invoking VMHostConfigV2.ps1 with action: $Action"
    
    # Execute VMHostConfigV2.ps1
    try {
        & $vmHostConfigScript @vmHostParams
        Write-ScriptLog "VMHostConfigV2.ps1 execution completed successfully"
        Write-Output "SUCCESS: Host $Action operation completed for $VMHostName"
    }
    catch {
        Write-ScriptLog "VMHostConfigV2.ps1 execution failed: $($_.Exception.Message)" "ERROR"
        Write-Output "ERROR: Host $Action operation failed for $VMHostName`: $($_.Exception.Message)"
    }
}
catch {
    Write-ScriptLog "Critical error in wrapper script: $($_.Exception.Message)" "ERROR"
    Write-ScriptLog "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    Write-Output "ERROR: Critical failure in host configuration operation: $($_.Exception.Message)"
}
finally {
    Write-ScriptLog "VMHost configuration wrapper script completed"
}