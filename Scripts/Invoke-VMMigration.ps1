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
    
    [Parameter()]
    [string]$LogPath = "Logs"
)

# Helper function to convert credential string to PSCredential
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
    Write-Host "Starting VM migration wrapper script..."
    Write-Host "Source vCenter: $SourceVCenter"
    Write-Host "Destination vCenter: $DestVCenter"
    Write-Host "VMs to migrate: $($VMList.Count)"
    
    # Convert credential strings to PSCredential objects
    $sourceCred = ConvertTo-PSCredential -CredentialString $SourceVCCredential
    $destCred = ConvertTo-PSCredential -CredentialString $DestVCCredential
    
    # Get the path to the main migration script
    $scriptPath = Join-Path -Path $PSScriptRoot -ChildPath "CrossVcenterVMmigration_list.ps1"
    
    if (-not (Test-Path $scriptPath)) {
        throw "Migration script not found at: $scriptPath"
    }
    
    # Prepare parameters for the main migration script
    $migrationParams = @{
        SourceVCenter = $SourceVCenter
        DestVCenter = $DestVCenter
        VMList = $VMList
        SourceVCCredential = $sourceCred
        DestVCCredential = $destCred
        LogFile = $LogFile
        LogLevel = $LogLevel
        NameSuffix = $NameSuffix
        DiskFormat = $DiskFormat
    }
    
    # Add optional parameters if specified
    if (-not [string]::IsNullOrEmpty($DestinationCluster)) {
        $migrationParams.DestinationCluster = $DestinationCluster
    }
    
    if ($PreserveMAC) {
        $migrationParams.PreserveMAC = $true
    }
    
    if ($SequentialMode) {
        $migrationParams.SequentialMode = $true
    } else {
        $migrationParams.MaxConcurrentMigrations = $MaxConcurrentMigrations
    }
    
    if ($EnhancedNetworkHandling) {
        $migrationParams.EnhancedNetworkHandling = $true
    }
    
    if ($IgnoreNetworkErrors) {
        $migrationParams.IgnoreNetworkErrors = $true
    }
    
    if ($Validate) {
        $migrationParams.Validate = $true
    }
    
    if ($SkipModuleCheck) {
        $migrationParams.SkipModuleCheck = $true
    }
    
    if ($NetworkMapping.Count -gt 0) {
        $migrationParams.NetworkMapping = $NetworkMapping
    }
    
    Write-Host "Executing main migration script..."
    
    # Execute the main migration script
    & $scriptPath @migrationParams
    
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -or $null -eq $exitCode) {
        Write-Host "Migration script completed successfully"
    } else {
        Write-Error "Migration script failed with exit code: $exitCode"
    }
    
}
catch {
    Write-Error "Error in migration wrapper: $_"
    Write-Host "STDERR: $($_.Exception.Message)"
    exit 1
}
finally {
    Write-Host "Migration wrapper script completed"
}