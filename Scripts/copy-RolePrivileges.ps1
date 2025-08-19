<#
.SYNOPSIS
    Copies privileges from one vCenter role to another with version compatibility handling.
.DESCRIPTION
    This script retrieves all privileges from a source role and adds them to a target role.
    It logs all actions and handles privilege differences between vCenter versions.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$vCenterServer,
    
    [Parameter(Mandatory = $true)]
    [string]$SourceRoleName,
    
    [Parameter(Mandatory = $true)]
    [string]$TargetRoleName,
    
    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential,
    
    [Parameter(Mandatory = $false)]
    [switch]$ReplaceExisting,

    [Parameter()]
    [string]$LogPath,

    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Copy-RolePrivileges" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$connection = $null
$stats = @{
    "SourceRolePrivileges" = 0
    "ValidPrivileges" = 0
    "SkippedInvalidPrivileges" = 0
    "PrivilegesAdded" = 0
    "PrivilegesFailedToAdd" = 0
    "Mode" = if($ReplaceExisting) { "Replace" } else { "Append" }
}

try {
    # Import PowerCLI module
    Write-LogInfo "Importing PowerCLI module..." -Category "Initialization"
    Import-Module VMware.VimAutomation.Core -ErrorAction Stop
    Write-LogSuccess "PowerCLI module imported successfully." -Category "Initialization"

    # Connect to vCenter
    $connectParams = @{ Server = $vCenterServer; ErrorAction = 'Stop' }
    if ($Credential) { $connectParams.Add('Credential', $Credential) }
    
    Write-LogInfo "Connecting to vCenter server: $vCenterServer" -Category "Connection"
    $connection = Connect-VIServer @connectParams
    Write-LogSuccess "Connected to vCenter: $($connection.Name) (Version: $($connection.Version))" -Category "Connection"
    
    # Get roles
    Write-LogInfo "Retrieving roles '$SourceRoleName' and '$TargetRoleName'..." -Category "Discovery"
    $sourceRole = Get-VIRole -Name $SourceRoleName -ErrorAction Stop
    $targetRole = Get-VIRole -Name $TargetRoleName -ErrorAction Stop
    Write-LogSuccess "Found source role '$($sourceRole.Name)' and target role '$($targetRole.Name)'." -Category "Discovery"

    # Get all available privileges for validation
    Write-LogInfo "Retrieving all available privileges in this vCenter instance..." -Category "Validation"
    $allAvailablePrivileges = Get-VIPrivilege | Select-Object -ExpandProperty Id
    Write-LogSuccess "Found $($allAvailablePrivileges.Count) available privileges." -Category "Validation"
    
    # Get and validate source privileges
    $sourcePrivileges = $sourceRole.PrivilegeList
    $stats.SourceRolePrivileges = $sourcePrivileges.Count
    Write-LogInfo "Found $($stats.SourceRolePrivileges) privileges in source role '$SourceRoleName'." -Category "Processing"

    $validSourcePrivileges = @()
    $invalidPrivileges = @()
    foreach ($privilege in $sourcePrivileges) {
        if ($privilege -in $allAvailablePrivileges) {
            $validSourcePrivileges += $privilege
        } else {
            $invalidPrivileges += $privilege
        }
    }
    
    $stats.ValidPrivileges = $validSourcePrivileges.Count
    $stats.SkippedInvalidPrivileges = $invalidPrivileges.Count

    if ($invalidPrivileges.Count -gt 0) {
        Write-LogWarning "$($invalidPrivileges.Count) privileges don't exist in this vCenter and will be skipped: $($invalidPrivileges -join ', ')" -Category "Compatibility"
    }

    Write-LogInfo "Proceeding with $($stats.ValidPrivileges) valid privileges." -Category "Processing"

    if ($ReplaceExisting) {
        Write-LogInfo "Mode: Replace. Setting target role to have $($stats.ValidPrivileges) privileges." -Category "Update"
        Set-VIRole -Role $targetRole -Privilege $validSourcePrivileges -Confirm:$false -ErrorAction Stop
        Write-LogSuccess "Successfully replaced privileges in target role." -Category "Update"
        $stats.PrivilegesAdded = $stats.ValidPrivileges
    } else {
        Write-LogInfo "Mode: Append. Checking for privileges to add." -Category "Update"
        $targetPrivileges = $targetRole.PrivilegeList
        $privilegesToAdd = $validSourcePrivileges | Where-Object { $_ -notin $targetPrivileges }

        if ($privilegesToAdd.Count -eq 0) {
            Write-LogSuccess "All valid source privileges already exist in target role. No changes needed." -Category "Update"
        } else {
            Write-LogInfo "Adding $($privilegesToAdd.Count) new privileges to target role..." -Category "Update"
            foreach ($privilege in $privilegesToAdd) {
                try {
                    Set-VIRole -Role $targetRole -AddPrivilege $privilege -Confirm:$false -ErrorAction Stop
                    Write-LogDebug "  Added privilege: $privilege" -Category "Update"
                    $stats.PrivilegesAdded++
                } catch {
                    Write-LogWarning "  Failed to add privilege '$($privilege)': $($_.Exception.Message)" -Category "Update"
                    $stats.PrivilegesFailedToAdd++
                }
            }
            Write-LogSuccess "Finished adding privileges. Added: $($stats.PrivilegesAdded), Failed: $($stats.PrivilegesFailedToAdd)." -Category "Update"
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "Role copy operation completed. Mode: $($stats.Mode). Privileges Added/Set: $($stats.PrivilegesAdded), Skipped: $($stats.SkippedInvalidPrivileges), Failed: $($stats.PrivilegesFailedToAdd)."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Script failed with error: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    if ($connection) {
        Write-LogInfo "Disconnecting from vCenter: $($connection.Name)..." -Category "Cleanup"
        Disconnect-VIServer -Server $connection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}