<#
.SYNOPSIS
    Migrates standard network configurations between vCenter environments.
.DESCRIPTION
    Analyzes standard vSwitches and port groups on a source vCenter and recreates them on a target.
    Supports validation-only mode and network name mapping.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)] [string]$SourceVCenter,
    [Parameter(Mandatory=$true)] [string]$TargetVCenter,
    [Parameter()][bool]$MigrateStandardSwitches = $true,
    [Parameter()][bool]$MigratePortGroups = $true,
    [Parameter()][bool]$PreserveVlanIds = $true,
    [Parameter()][bool]$RecreateIfExists = $false,
    [Parameter()][bool]$ValidateOnly = $false,
    [Parameter()][hashtable]$NetworkMappings = @{},
    [Parameter()][bool]$BypassModuleCheck = $false,
    [Parameter()][string]$LogPath,
    [Parameter()][bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Migrate-NetworkConfiguration" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$stats = @{
    "vSwitchesCreated" = 0; "PortGroupsCreated" = 0; "Errors" = 0
}

try {
    Write-LogInfo "Starting network configuration migration..." -Category "Initialization"
    
    # Import and configure PowerCLI
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Setup"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    }
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    
    # Check vCenter connections
    if (-not (Get-PowerCLIConnection -Server $SourceVCenter -ErrorAction SilentlyContinue)) { throw "No active connection to source vCenter: $SourceVCenter" }
    if (-not (Get-PowerCLIConnection -Server $TargetVCenter -ErrorAction SilentlyContinue)) { throw "No active connection to target vCenter: $TargetVCenter" }
    Write-LogSuccess "Verified active connections to source and target vCenters." -Category "Connection"

    # Analyze source network
    Write-LogInfo "Analyzing source network configuration..." -Category "Analysis"
    $sourceHosts = Get-VMHost -Server $SourceVCenter
    $sourceNetworkConfig = @{ StandardSwitches = @(); PortGroups = @() }

    foreach ($vmHost in $sourceHosts) {
        if ($MigrateStandardSwitches) {
            Get-VirtualSwitch -VMHost $vmHost -Standard | ForEach-Object {
                $sourceNetworkConfig.StandardSwitches += @{ HostName = $vmHost.Name; Name = $_.Name; NumPorts = $_.NumPorts }
            }
        }
        if ($MigratePortGroups) {
            Get-VirtualPortGroup -VMHost $vmHost | ForEach-Object {
                $sourceNetworkConfig.PortGroups += @{ HostName = $vmHost.Name; VSwitchName = $_.VirtualSwitch.Name; Name = $_.Name; VLanId = $_.VLanId }
            }
        }
    }
    Write-LogInfo "Source analysis complete. Found $($sourceNetworkConfig.StandardSwitches.Count) standard vSwitches and $($sourceNetworkConfig.PortGroups.Count) port groups." -Category "Analysis"

    if ($ValidateOnly) {
        Write-LogInfo "VALIDATION ONLY mode. No changes will be made." -Category "Mode"
        # ... (Validation logic can be enhanced here) ...
        $scriptSuccess = $true
        $finalSummary = "Validation mode completed. Analyzed source network configuration."
    } else {
        # Start migration
        Write-LogInfo "Starting network configuration migration to target..." -Category "Migration"
        $targetHosts = Get-VMHost -Server $TargetVCenter
        if ($targetHosts.Count -eq 0) { throw "No target hosts found." }

        # Migrate standard vSwitches
        foreach ($switch in $sourceNetworkConfig.StandardSwitches) {
            $targetHost = $targetHosts | Where-Object { $_.Name -eq $switch.HostName } | Select-Object -First 1
            if(-not $targetHost) { $targetHost = $targetHosts[0]; Write-LogWarning "Host $($switch.HostName) not found on target, using $($targetHost.Name) instead."}
            
            if(Get-VirtualSwitch -VMHost $targetHost -Name $switch.Name -Standard -ErrorAction SilentlyContinue) {
                Write-LogInfo "vSwitch '$($switch.Name)' already exists on host '$($targetHost.Name)', skipping." -Category "Migration"
                continue
            }
            Write-LogInfo "Creating vSwitch '$($switch.Name)' on host '$($targetHost.Name)'" -Category "Migration"
            New-VirtualSwitch -VMHost $targetHost -Name $switch.Name -NumPorts $switch.NumPorts -ErrorAction Stop
            $stats.vSwitchesCreated++
        }

        # Migrate port groups
        foreach ($portGroup in $sourceNetworkConfig.PortGroups) {
            $targetPortGroupName = $NetworkMappings[$portGroup.Name] ?? $portGroup.Name
            $targetHost = $targetHosts | Where-Object { $_.Name -eq $portGroup.HostName } | Select-Object -First 1
            if(-not $targetHost) { $targetHost = $targetHosts[0] }
            
            $targetVSwitch = Get-VirtualSwitch -VMHost $targetHost -Name $portGroup.VSwitchName -Standard -ErrorAction SilentlyContinue
            if (-not $targetVSwitch) {
                Write-LogWarning "Target vSwitch '$($portGroup.VSwitchName)' not found on host '$($targetHost.Name)', skipping port group '$($portGroup.Name)'." -Category "Migration"
                continue
            }
            if(Get-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -ErrorAction SilentlyContinue) {
                Write-LogInfo "Port group '$targetPortGroupName' already exists on host '$($targetHost.Name)', skipping." -Category "Migration"
                continue
            }
            Write-LogInfo "Creating port group '$targetPortGroupName' on vSwitch '$($targetVSwitch.Name)'" -Category "Migration"
            $vlanId = if ($PreserveVlanIds) { $portGroup.VLanId } else { 0 }
            New-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -VirtualSwitch $targetVSwitch -VLanId $vlanId -ErrorAction Stop
            $stats.PortGroupsCreated++
        }
        $scriptSuccess = $true
        $finalSummary = "Network migration completed."
    }
} catch {
    $scriptSuccess = $false
    $stats.Errors++
    $finalSummary = "Network migration failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}