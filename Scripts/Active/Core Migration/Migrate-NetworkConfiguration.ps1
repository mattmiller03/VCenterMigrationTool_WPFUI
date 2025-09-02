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
    [Parameter()][bool]$MigrateDistributedSwitches = $false,
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
    "StandardSwitchesCreated" = 0; "DistributedSwitchesCreated" = 0; "PortGroupsCreated" = 0; "Errors" = 0
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
    $sourceNetworkConfig = @{ StandardSwitches = @(); DistributedSwitches = @(); PortGroups = @() }

    foreach ($vmHost in $sourceHosts) {
        if ($MigrateStandardSwitches) {
            Get-VirtualSwitch -VMHost $vmHost -Standard | ForEach-Object {
                $sourceNetworkConfig.StandardSwitches += @{ 
                    HostName = $vmHost.Name; Name = $_.Name; NumPorts = $_.NumPorts; Mtu = $_.Mtu 
                }
            }
        }
        if ($MigratePortGroups) {
            Get-VirtualPortGroup -VMHost $vmHost | ForEach-Object {
                $sourceNetworkConfig.PortGroups += @{ 
                    HostName = $vmHost.Name; VSwitchName = $_.VirtualSwitch.Name; Name = $_.Name; 
                    VLanId = $_.VLanId; SwitchType = "Standard" 
                }
            }
        }
    }
    
    # Analyze distributed switches (only once per vCenter, not per host)
    if ($MigrateDistributedSwitches) {
        Get-VDSwitch -Server $SourceVCenter | ForEach-Object {
            $vds = $_
            $sourceNetworkConfig.DistributedSwitches += @{
                Name = $vds.Name
                Version = $vds.Version
                Vendor = $vds.ExtensionData.Summary.ProductInfo.Vendor
                Mtu = $vds.Mtu
                MaxPorts = $vds.MaxPorts
                NumStandalonePorts = $vds.NumStandalonePorts
                LinkDiscoveryProtocol = $vds.ExtensionData.Config.LinkDiscoveryProtocolConfig.Protocol
                ContactInfo = $vds.ExtensionData.Config.Contact.Contact
                ContactDetails = $vds.ExtensionData.Config.Contact.Name
            }
            
            if ($MigratePortGroups) {
                Get-VDPortgroup -VDSwitch $vds | ForEach-Object {
                    $vlanConfig = $_.ExtensionData.Config.DefaultPortConfig.Vlan
                    $vlanId = 0
                    if ($vlanConfig.VlanId) { $vlanId = $vlanConfig.VlanId }
                    elseif ($vlanConfig.PvlanId) { $vlanId = $vlanConfig.PvlanId }
                    
                    $sourceNetworkConfig.PortGroups += @{
                        HostName = "N/A"; VSwitchName = $vds.Name; Name = $_.Name;
                        VLanId = $vlanId; SwitchType = "Distributed"; NumPorts = $_.NumPorts;
                        PortgroupKey = $_.Key
                    }
                }
            }
        }
    }
    
    Write-LogInfo "Source analysis complete. Found $($sourceNetworkConfig.StandardSwitches.Count) standard vSwitches, $($sourceNetworkConfig.DistributedSwitches.Count) distributed vSwitches, and $($sourceNetworkConfig.PortGroups.Count) port groups." -Category "Analysis"

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
        if ($MigrateStandardSwitches) {
            foreach ($switch in $sourceNetworkConfig.StandardSwitches) {
                $targetHost = $targetHosts | Where-Object { $_.Name -eq $switch.HostName } | Select-Object -First 1
                if(-not $targetHost) { $targetHost = $targetHosts[0]; Write-LogWarning "Host $($switch.HostName) not found on target, using $($targetHost.Name) instead."}
                
                if(Get-VirtualSwitch -VMHost $targetHost -Name $switch.Name -Standard -ErrorAction SilentlyContinue) {
                    if ($RecreateIfExists) {
                        Write-LogInfo "Removing existing vSwitch '$($switch.Name)' on host '$($targetHost.Name)' before recreating." -Category "Migration"
                        Remove-VirtualSwitch -VirtualSwitch (Get-VirtualSwitch -VMHost $targetHost -Name $switch.Name -Standard) -Confirm:$false
                    } else {
                        Write-LogInfo "vSwitch '$($switch.Name)' already exists on host '$($targetHost.Name)', skipping." -Category "Migration"
                        continue
                    }
                }
                Write-LogInfo "Creating standard vSwitch '$($switch.Name)' on host '$($targetHost.Name)'" -Category "Migration"
                New-VirtualSwitch -VMHost $targetHost -Name $switch.Name -NumPorts $switch.NumPorts -Mtu $switch.Mtu -ErrorAction Stop
                $stats.StandardSwitchesCreated++
            }
        }
        
        # Migrate distributed vSwitches
        if ($MigrateDistributedSwitches) {
            foreach ($vds in $sourceNetworkConfig.DistributedSwitches) {
                if(Get-VDSwitch -Server $TargetVCenter -Name $vds.Name -ErrorAction SilentlyContinue) {
                    if ($RecreateIfExists) {
                        Write-LogInfo "Removing existing distributed vSwitch '$($vds.Name)' before recreating." -Category "Migration"
                        Remove-VDSwitch -VDSwitch (Get-VDSwitch -Server $TargetVCenter -Name $vds.Name) -Confirm:$false
                    } else {
                        Write-LogInfo "Distributed vSwitch '$($vds.Name)' already exists, skipping." -Category "Migration"
                        continue
                    }
                }
                
                Write-LogInfo "Creating distributed vSwitch '$($vds.Name)' with version '$($vds.Version)'" -Category "Migration"
                
                # Create the distributed switch
                $newVds = New-VDSwitch -Server $TargetVCenter -Name $vds.Name -Location (Get-Datacenter -Server $TargetVCenter | Select-Object -First 1)
                
                # Configure VDS settings
                if ($vds.MaxPorts -and $vds.MaxPorts -gt 0) {
                    Set-VDSwitch -VDSwitch $newVds -NumStandalonePorts $vds.NumStandalonePorts -MaxPorts $vds.MaxPorts -Mtu $vds.Mtu -ErrorAction SilentlyContinue
                }
                
                # Add hosts to the distributed switch
                foreach ($targetHost in $targetHosts) {
                    try {
                        Write-LogInfo "Adding host '$($targetHost.Name)' to distributed vSwitch '$($vds.Name)'" -Category "Migration"
                        Add-VDSwitchVMHost -VDSwitch $newVds -VMHost $targetHost -ErrorAction Stop
                    } catch {
                        Write-LogWarning "Failed to add host '$($targetHost.Name)' to VDS '$($vds.Name)': $($_.Exception.Message)" -Category "Migration"
                    }
                }
                
                $stats.DistributedSwitchesCreated++
            }
        }

        # Migrate port groups
        if ($MigratePortGroups) {
            foreach ($portGroup in $sourceNetworkConfig.PortGroups) {
                $targetPortGroupName = $NetworkMappings[$portGroup.Name] ?? $portGroup.Name
                
                if ($portGroup.SwitchType -eq "Standard") {
                    # Standard port group migration
                    $targetHost = $targetHosts | Where-Object { $_.Name -eq $portGroup.HostName } | Select-Object -First 1
                    if(-not $targetHost) { $targetHost = $targetHosts[0] }
                    
                    $targetVSwitch = Get-VirtualSwitch -VMHost $targetHost -Name $portGroup.VSwitchName -Standard -ErrorAction SilentlyContinue
                    if (-not $targetVSwitch) {
                        Write-LogWarning "Target standard vSwitch '$($portGroup.VSwitchName)' not found on host '$($targetHost.Name)', skipping port group '$($portGroup.Name)'." -Category "Migration"
                        continue
                    }
                    
                    if(Get-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -ErrorAction SilentlyContinue) {
                        if ($RecreateIfExists) {
                            Write-LogInfo "Removing existing port group '$targetPortGroupName' before recreating." -Category "Migration"
                            Remove-VirtualPortGroup -VirtualPortGroup (Get-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName) -Confirm:$false
                        } else {
                            Write-LogInfo "Port group '$targetPortGroupName' already exists on host '$($targetHost.Name)', skipping." -Category "Migration"
                            continue
                        }
                    }
                    
                    Write-LogInfo "Creating standard port group '$targetPortGroupName' on vSwitch '$($targetVSwitch.Name)'" -Category "Migration"
                    $vlanId = if ($PreserveVlanIds) { $portGroup.VLanId } else { 0 }
                    New-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -VirtualSwitch $targetVSwitch -VLanId $vlanId -ErrorAction Stop
                    $stats.PortGroupsCreated++
                    
                } elseif ($portGroup.SwitchType -eq "Distributed") {
                    # Distributed port group migration
                    $targetVDSwitch = Get-VDSwitch -Server $TargetVCenter -Name $portGroup.VSwitchName -ErrorAction SilentlyContinue
                    if (-not $targetVDSwitch) {
                        Write-LogWarning "Target distributed vSwitch '$($portGroup.VSwitchName)' not found, skipping distributed port group '$($portGroup.Name)'." -Category "Migration"
                        continue
                    }
                    
                    if(Get-VDPortgroup -VDSwitch $targetVDSwitch -Name $targetPortGroupName -ErrorAction SilentlyContinue) {
                        if ($RecreateIfExists) {
                            Write-LogInfo "Removing existing distributed port group '$targetPortGroupName' before recreating." -Category "Migration"
                            Remove-VDPortgroup -VDPortgroup (Get-VDPortgroup -VDSwitch $targetVDSwitch -Name $targetPortGroupName) -Confirm:$false
                        } else {
                            Write-LogInfo "Distributed port group '$targetPortGroupName' already exists, skipping." -Category "Migration"
                            continue
                        }
                    }
                    
                    Write-LogInfo "Creating distributed port group '$targetPortGroupName' on vDSwitch '$($targetVDSwitch.Name)'" -Category "Migration"
                    $vlanId = if ($PreserveVlanIds) { $portGroup.VLanId } else { 0 }
                    $numPorts = if ($portGroup.NumPorts -and $portGroup.NumPorts -gt 0) { $portGroup.NumPorts } else { 128 }
                    
                    New-VDPortgroup -VDSwitch $targetVDSwitch -Name $targetPortGroupName -NumPorts $numPorts -VlanId $vlanId -ErrorAction Stop
                    $stats.PortGroupsCreated++
                }
            }
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