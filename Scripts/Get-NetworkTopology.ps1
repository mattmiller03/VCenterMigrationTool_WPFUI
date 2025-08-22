<#
.SYNOPSIS
    Discovers and exports the full network topology from a vCenter.
.DESCRIPTION
    Connects to vCenter, iterates through each ESXi host, and documents its standard vSwitches,
    distributed vSwitches, port groups, and VMkernel adapters.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [Parameter(Mandatory=$true)] [string]$VCenterServer,
    [Parameter(Mandatory=$true)] [System.Management.Automation.PSCredential]$Credentials,
    [Parameter()][bool]$BypassModuleCheck = $false,
    [Parameter()][string]$LogPath,
    [Parameter()][bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-NetworkTopology" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$jsonOutput = "[]"
$stats = @{ "HostsProcessed" = 0; "TotalVSwitches" = 0; "TotalVmKernelPorts" = 0 }

try {
    Write-LogInfo "Starting network topology discovery..." -Category "Initialization"
    
    # Import PowerCLI
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Setup"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    }
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter." -Category "Connection"
    
    # Get all ESXi hosts
    $vmHosts = Get-VMHost
    $networkTopology = @()
    
    foreach ($vmHost in $vmHosts) {
        $stats.HostsProcessed++
        Write-LogInfo "Processing network topology for host: $($vmHost.Name)" -Category "Discovery"
        $hostNetworkConfig = @{ Name = $vmHost.Name; VSwitches = @(); VmKernelPorts = @() }

        # Get standard switches
        $standardSwitches = Get-VirtualSwitch -VMHost $vmHost -Standard -ErrorAction SilentlyContinue
        foreach($switch in $standardSwitches) {
            $portGroups = @()
            Get-VirtualPortGroup -VMHost $vmHost -VirtualSwitch $switch -ErrorAction SilentlyContinue | ForEach-Object {
                $portGroups += @{
                    Name = $_.Name
                    VlanId = $_.VLanId
                    IsSelected = $false
                }
            }
            
            $switchConfig = @{
                Name = $switch.Name
                Type = "StandardSwitch"
                NumPorts = $switch.NumPorts
                Mtu = $switch.Mtu
                PortGroups = $portGroups
                IsSelected = $false
            }
            $hostNetworkConfig.VSwitches += $switchConfig
            $stats.TotalVSwitches++
        }
        
        # Get distributed switches associated with this host
        $distributedSwitches = Get-VDSwitch -ErrorAction SilentlyContinue | Where-Object { 
            $_.ExtensionData.Summary.HostMember -contains $vmHost.ExtensionData.MoRef 
        }
        foreach($vds in $distributedSwitches) {
            $portGroups = @()
            Get-VDPortgroup -VDSwitch $vds -ErrorAction SilentlyContinue | ForEach-Object {
                $vlanConfig = $_.ExtensionData.Config.DefaultPortConfig.Vlan
                $vlanId = 0
                if ($vlanConfig.VlanId) { $vlanId = $vlanConfig.VlanId }
                elseif ($vlanConfig.PvlanId) { $vlanId = $vlanConfig.PvlanId }
                
                $portGroups += @{
                    Name = $_.Name
                    VlanId = $vlanId
                    Key = $_.Key
                    NumPorts = $_.NumPorts
                    IsSelected = $false
                }
            }
            
            $switchConfig = @{
                Name = $vds.Name
                Type = "DistributedSwitch"
                Version = $vds.Version
                Uuid = $vds.ExtensionData.Uuid
                Vendor = $vds.ExtensionData.Summary.ProductInfo.Vendor
                Mtu = $vds.Mtu
                MaxPorts = $vds.MaxPorts
                NumStandalonePorts = $vds.NumStandalonePorts
                PortGroups = $portGroups
                IsSelected = $false
                # Export key configuration for recreation
                Config = @{
                    Version = $vds.Version
                    MaxPorts = $vds.MaxPorts
                    NumStandalonePorts = $vds.NumStandalonePorts
                    Mtu = $vds.Mtu
                    LinkDiscoveryProtocol = $vds.ExtensionData.Config.LinkDiscoveryProtocolConfig.Protocol
                    ContactInfo = $vds.ExtensionData.Config.Contact.Contact
                    ContactDetails = $vds.ExtensionData.Config.Contact.Name
                }
            }
            $hostNetworkConfig.VSwitches += $switchConfig
            $stats.TotalVSwitches++
        }
        
        # Get VMkernel ports
        $vmkPorts = Get-VMHostNetworkAdapter -VMHost $vmHost -VMKernel -ErrorAction SilentlyContinue
        foreach ($vmkPort in $vmkPorts) {
            $vmkConfig = @{ Name = $vmkPort.Name; IpAddress = $vmkPort.IP }
            # ... add other vmk properties as needed ...
            $hostNetworkConfig.VmKernelPorts += $vmkConfig
            $stats.TotalVmKernelPorts++
        }
        
        $networkTopology += $hostNetworkConfig
        Write-LogDebug "Host '$($vmHost.Name)' summary: $($hostNetworkConfig.VSwitches.Count) vSwitches, $($hostNetworkConfig.VmKernelPorts.Count) VMkernel ports." -Category "Discovery"
    }
    
    $jsonOutput = $networkTopology | ConvertTo-Json -Depth 10
    
    $scriptSuccess = $true
    $finalSummary = "Network topology discovery completed successfully. Processed $($stats.HostsProcessed) hosts."

} catch {
    $scriptSuccess = $false
    $finalSummary = "Network topology discovery failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter server..." -Category "Cleanup"
    Disconnect-VIServer -Server $VCenterServer -Confirm:$false -ErrorAction SilentlyContinue
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}

# Final output
Write-Output $jsonOutput