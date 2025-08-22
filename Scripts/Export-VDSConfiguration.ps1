<#
.SYNOPSIS
    Exports Virtual Distributed Switch (VDS) configurations to JSON or CSV format.
.DESCRIPTION
    Connects to vCenter and exports detailed VDS configurations including switches,
    port groups, VLAN settings, and other network configuration details.
    Supports both JSON and CSV export formats.
.NOTES
    Version: 1.0 - Initial VDS export implementation
#>
param(
    [Parameter(Mandatory=$true)] [string]$VCenterServer,
    [Parameter(Mandatory=$true)] [System.Management.Automation.PSCredential]$Credentials,
    [Parameter(Mandatory=$true)] [string]$ExportFilePath,
    [Parameter()][ValidateSet("JSON", "CSV")] [string]$ExportFormat = "JSON",
    [Parameter()][bool]$IncludeStandardSwitches = $false,
    [Parameter()][bool]$BypassModuleCheck = $false,
    [Parameter()][string]$LogPath,
    [Parameter()][bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Export-VDSConfiguration" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$exportData = @()
$stats = @{ "VDSSwitchesExported" = 0; "StandardSwitchesExported" = 0; "PortGroupsExported" = 0 }

try {
    Write-LogInfo "Starting VDS configuration export..." -Category "Initialization"
    
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
    
    # Export Distributed Switches
    Write-LogInfo "Exporting distributed vSwitches..." -Category "Export"
    $vdSwitches = Get-VDSwitch
    
    foreach ($vds in $vdSwitches) {
        $stats.VDSSwitchesExported++
        Write-LogInfo "Processing VDS: $($vds.Name)" -Category "Export"
        
        # Get distributed port groups
        $distributedPortGroups = @()
        Get-VDPortgroup -VDSwitch $vds | ForEach-Object {
            $stats.PortGroupsExported++
            $vlanConfig = $_.ExtensionData.Config.DefaultPortConfig.Vlan
            $vlanId = 0
            $vlanType = "None"
            
            if ($vlanConfig.VlanId) { 
                $vlanId = $vlanConfig.VlanId 
                $vlanType = "VLAN"
            }
            elseif ($vlanConfig.PvlanId) { 
                $vlanId = $vlanConfig.PvlanId 
                $vlanType = "PVLAN"
            }
            elseif ($vlanConfig.VlanRange) {
                $vlanType = "VLANRange"
                $vlanId = "$($vlanConfig.VlanRange[0].Start)-$($vlanConfig.VlanRange[0].End)"
            }
            
            $distributedPortGroups += @{
                Name = $_.Name
                Key = $_.Key
                VlanId = $vlanId
                VlanType = $vlanType
                NumPorts = $_.NumPorts
                PortBinding = $_.ExtensionData.Config.Type
                AutoExpand = $_.ExtensionData.Config.AutoExpand
            }
        }
        
        $exportData += @{
            Type = "DistributedSwitch"
            Name = $vds.Name
            Uuid = $vds.ExtensionData.Uuid
            Version = $vds.Version
            Vendor = $vds.ExtensionData.Summary.ProductInfo.Vendor
            Build = $vds.ExtensionData.Summary.ProductInfo.Build
            Mtu = $vds.Mtu
            MaxPorts = $vds.MaxPorts
            NumStandalonePorts = $vds.NumStandalonePorts
            LinkDiscoveryProtocol = $vds.ExtensionData.Config.LinkDiscoveryProtocolConfig.Protocol
            ContactInfo = $vds.ExtensionData.Config.Contact.Contact
            ContactDetails = $vds.ExtensionData.Config.Contact.Name
            Description = $vds.ExtensionData.Config.Description
            PortGroups = $distributedPortGroups
            # Host membership will be determined during import
            NumUplinkPorts = $vds.ExtensionData.Config.NumUplinkPorts
            UplinkPortNames = $vds.ExtensionData.Config.UplinkPortPolicy.UplinkPortName
        }
    }
    
    # Export Standard Switches if requested
    if ($IncludeStandardSwitches) {
        Write-LogInfo "Including standard vSwitches in export..." -Category "Export"
        $vmHosts = Get-VMHost
        
        foreach ($vmHost in $vmHosts) {
            Get-VirtualSwitch -VMHost $vmHost -Standard | ForEach-Object {
                $stats.StandardSwitchesExported++
                $standardPortGroups = @()
                
                Get-VirtualPortGroup -VMHost $vmHost -VirtualSwitch $_ | ForEach-Object {
                    $stats.PortGroupsExported++
                    $standardPortGroups += @{
                        Name = $_.Name
                        VlanId = $_.VLanId
                        VlanType = "VLAN"
                    }
                }
                
                $exportData += @{
                    Type = "StandardSwitch"
                    HostName = $vmHost.Name
                    Name = $_.Name
                    NumPorts = $_.NumPorts
                    Mtu = $_.Mtu
                    PortGroups = $standardPortGroups
                }
            }
        }
    }
    
    # Export to file
    Write-LogInfo "Exporting configuration to: $ExportFilePath" -Category "Export"
    
    if ($ExportFormat -eq "JSON") {
        $exportData | ConvertTo-Json -Depth 10 | Out-File -FilePath $ExportFilePath -Encoding UTF8
    } elseif ($ExportFormat -eq "CSV") {
        # Flatten the data for CSV export
        $flatData = @()
        foreach ($switch in $exportData) {
            foreach ($portGroup in $switch.PortGroups) {
                $flatData += [PSCustomObject]@{
                    SwitchType = $switch.Type
                    SwitchName = $switch.Name
                    HostName = $switch.HostName
                    PortGroupName = $portGroup.Name
                    VlanId = $portGroup.VlanId
                    VlanType = $portGroup.VlanType
                    NumPorts = $portGroup.NumPorts
                    Version = $switch.Version
                    Mtu = $switch.Mtu
                }
            }
        }
        $flatData | Export-Csv -Path $ExportFilePath -NoTypeInformation -Encoding UTF8
    }
    
    $scriptSuccess = $true
    $finalSummary = "VDS configuration export completed. Exported $($stats.VDSSwitchesExported) VDS, $($stats.StandardSwitchesExported) standard switches, and $($stats.PortGroupsExported) port groups to $ExportFilePath"

} catch {
    $scriptSuccess = $false
    $finalSummary = "VDS configuration export failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter server..." -Category "Cleanup"
    Disconnect-VIServer -Server $VCenterServer -Confirm:$false -ErrorAction SilentlyContinue
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}

# Output export summary
Write-Output "Export completed: $($stats.VDSSwitchesExported) VDS, $($stats.PortGroupsExported) port groups exported to $ExportFilePath"