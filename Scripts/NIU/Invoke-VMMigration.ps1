<#
.SYNOPSIS
    Wrapper script for cross-vCenter VM migration integration with the GUI application.

.DESCRIPTION
    This script serves as a wrapper around the CrossVcenterVMmigration_list.ps1 script,
    providing a consistent interface for the C# application to execute VM migrations.
    It handles credential conversion and parameter normalization.

.PARAMETER SourceVCenter
    The hostname or IP address of the source vCenter Server.

.PARAMETER DestVCenter
    The hostname or IP address of the destination vCenter Server.

.PARAMETER VMList
    Array of VM names to migrate.

.PARAMETER SourceVCCredential
    Source vCenter credentials in format "username:password".

.PARAMETER DestVCCredential
    Destination vCenter credentials in format "username:password".

.PARAMETER DestinationCluster
    Name of the destination cluster.

.PARAMETER NameSuffix
    Suffix to append to migrated VM names.

.PARAMETER PreserveMAC
    Whether to preserve MAC addresses.

.PARAMETER DiskFormat
    Disk format for migrated VMs.

.PARAMETER MaxConcurrentMigrations
    Maximum number of concurrent migrations.

.PARAMETER SequentialMode
    Whether to use sequential migration mode.

.PARAMETER EnhancedNetworkHandling
    Whether to use enhanced network handling.

.PARAMETER IgnoreNetworkErrors
    Whether to ignore network configuration errors.

.PARAMETER Validate
    Whether to run in validation mode only.

.PARAMETER NetworkMapping
    Hashtable of network mappings.

.PARAMETER LogFile
    Base name for log files.

.PARAMETER LogLevel
    Logging detail level.

.PARAMETER SkipModuleCheck
    Whether to skip PowerCLI module checks.

.PARAMETER LogPath
    Directory for log output.

.EXAMPLE
    .\Invoke-VMMigration.ps1 -SourceVCenter "source.lab.local" -DestVCenter "dest.lab.local" -VMList @("VM1","VM2") -SourceVCCredential "admin:password" -DestVCCredential "admin:password"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenter,
    
    [Parameter(Mandatory=$true)]
    [string]$DestVCenter,
    
    [Parameter(Mandatory=$true)]
    [string[]]$VMList,
    
    [Parameter(Mandatory=$true)]
    [string]$SourceVCCredential,
    
    [Parameter(Mandatory=$true)]
    [string]$DestVCCredential,
    
    [Parameter()]
    [string]$DestinationCluster = "",
    
    [Parameter()]
    [string]$NameSuffix = "-Imported",
    
    [Parameter()]
    [bool]$PreserveMAC = $false,
    
    [Parameter()]
    [string]$DiskFormat = "Thin",
    
    [Parameter()]
    [int]$MaxConcurrentMigrations = 2,
    
    [Parameter()]
    [bool]$SequentialMode = $false,
    
    [Parameter()]
    [bool]$EnhancedNetworkHandling = $true,
    
    [Parameter()]
    [bool]$IgnoreNetworkErrors = $false,
    
    [Parameter()]
    [bool]$Validate = $false,
    
    [Parameter()]
    [hashtable]$NetworkMapping = @{},
    
    [Parameter()]
    [string]$LogFile = "VMMigration",
    
    [Parameter()]
    [string]$LogLevel = "Normal",
    
    [Parameter()]
    [bool]$SkipModuleCheck = $false,
    
    [Parameter(Mandatory=$false)]
    [string]$LogPath,
    [Parameter(Mandatory=$false)]
    [bool]$SuppressConsoleOutput = $false
)
# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"
# Helper function to convert credential string to PSCredential

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Invoke-VMMigration" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
function ConvertTo-PSCredential {
    param([string]$CredentialString)
    
    if ($CredentialString -contains ":") {
        $parts = $CredentialString -split ":", 2
        $username = $parts[0]
        $password = $parts[1]
        
        $securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
        return New-Object System.Management.Automation.PSCredential($username, $securePassword)
    }
    else {
        throw "Invalid credential format. Expected 'username:password'"
    }
}

try {
Write-LogInfo "Starting VM migration wrapper script..." -Category "Initialization"
    Write-LogInfo "Source vCenter: $SourceVCenter" -Category "Parameters"
    Write-LogInfo "Destination vCenter: $DestVCenter" -Category "Parameters"
    Write-LogInfo "VMs to migrate: $($VMList.Count) ($($VMList -join ', '))" -Category "Parameters"
    
    
    # Convert credential strings
    Write-LogInfo "Converting credential strings to PSCredential objects..." -Category "Security"
    if (-not $SourceVCCredential -or -not $DestVCCredential) {
        throw "Source and destination vCenter credentials must be provided."
    }
    $sourceCred = ConvertTo-PSCredential -CredentialString $SourceVCCredential
    $destCred = ConvertTo-PSCredential -CredentialString $DestVCCredential
    Write-LogSuccess "Credentials converted successfully." -Category "Security"
    # Get the path to the main migration script
    $scriptPath = Join-Path -Path $PSScriptRoot -ChildPath "CrossVcenterVMmigration_list.ps1"
    
    if (-not (Test-Path $scriptPath)) {
        throw "Migration script not found at: $scriptPath"
    }
     Write-LogInfo "Found main migration script at: $scriptPath" -Category "Discovery"
     # Prepare parameters
    $migrationParams = @{
        SourceVCenter = $SourceVCenter; DestVCenter = $DestVCenter; VMList = $VMList
        SourceVCCredential = $sourceCred; DestVCCredential = $destCred
        LogFile = $LogFile; LogLevel = $LogLevel; NameSuffix = $NameSuffix; DiskFormat = $DiskFormat
    }
    # Add optional parameters
    if (-not [string]::IsNullOrEmpty($DestinationCluster)) { $migrationParams.DestinationCluster = $DestinationCluster }
    if ($PreserveMAC) { $migrationParams.PreserveMAC = $true }
    if ($SequentialMode) { $migrationParams.SequentialMode = $true } else { $migrationParams.MaxConcurrentMigrations = $MaxConcurrentMigrations }
    if ($EnhancedNetworkHandling) { $migrationParams.EnhancedNetworkHandling = $true }
    if ($IgnoreNetworkErrors) { $migrationParams.IgnoreNetworkErrors = $true }
    if ($Validate) { $migrationParams.Validate = $true }
    if ($SkipModuleCheck) { $migrationParams.SkipModuleCheck = $true }
    if ($NetworkMapping.Count -gt 0) { $migrationParams.NetworkMapping = $NetworkMapping }
    
    Write-LogInfo "Executing main migration script..." -Category "Execution"
    
    # Execute the main migration script
    & $scriptPath @migrationParams
    
    $exitCode = $LASTEXITCODE
    # Check for errors from the child script
    if ($LASTEXITCODE -ne 0 -or -not $?) {
         throw "The main migration script (CrossVcenterVMmigration_list.ps1) reported an error. Please check its log file for details."
    }
     Write-LogSuccess "Main migration script completed successfully." -Category "Execution"
    $scriptSuccess = $true
    $finalSummary = "Migration wrapper completed successfully. Handed off $($VMList.Count) VMs to the main migration script."
}
catch {
    $scriptSuccess = $false
    $finalSummary = "Error in migration wrapper: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    exit 1 # Exit with a non-zero code to indicate failure to the calling application
}
finally {
    $finalStats = @{
        "SourceVCenter" = $SourceVCenter
        "DestinationVCenter" = $DestVCenter
        "VMCount" = $VMList.Count
        "ValidationOnly" = $Validate
    }
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
}