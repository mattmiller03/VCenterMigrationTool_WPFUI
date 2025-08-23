<#
.SYNOPSIS
    Exports Virtual Distributed Switch (vDS) and Port Groups using PowerCLI 13.x
.DESCRIPTION
    Connects to vCenter and exports all vDS switches with their port groups,
    including complete configuration for migration purposes.
.NOTES
    Version: 1.0 - PowerCLI 13.x optimized
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [System.Management.Automation.PSCredential]$Credentials,
    
    [Parameter(Mandatory=$true)]
    [string]$ExportPath,
    
    [Parameter()]
    [bool]$BypassModuleCheck = $false,
    
    [Parameter()]
    [string]$LogPath,
    
    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Export-VDS" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$exportData = @{
    ExportDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SourceVCenter = $VCenterServer
    VDSSwitches = @()
    TotalSwitches = 0
    TotalPortGroups = 0
}

try {
    Write-LogInfo "Starting vDS export process" -Category "Initialization"
    
    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
        Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
    }
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Get all distributed virtual switches
    Write-LogInfo "Retrieving Virtual Distributed Switches..." -Category "Discovery"
    $vdSwitches = Get-VDSwitch -ErrorAction SilentlyContinue
    
    if (-not $vdSwitches) {
        Write-LogWarning "No Virtual Distributed Switches found in vCenter" -Category "Discovery"
    }
    else {
        Write-LogInfo "Found $($vdSwitches.Count) Virtual Distributed Switches" -Category "Discovery"
        
        foreach ($vds in $vdSwitches) {
            Write-LogInfo "Processing vDS: $($vds.Name)" -Category "Export"
            
            # Create vDS export object with comprehensive configuration
            $vdsExport = @{
                Name = $vds.Name
                Uuid = $vds.ExtensionData.Uuid
                Version = $vds.Version
                Vendor = $vds.ExtensionData.Summary.ProductInfo.Vendor
                MaxPorts = $vds.MaxPorts
                Mtu = $vds.Mtu
                NumPorts = $vds.NumPorts
                NumUplinkPorts = $vds.ExtensionData.Config.NumUplinkPorts
                UplinkPortNames = $vds.ExtensionData.Config.UplinkPortPolicy.UplinkPortName
                Notes = $vds.Notes
                ContactName = $vds.ContactName
                ContactDetails = $vds.ContactDetails
                NetworkIOControlEnabled = $vds.ExtensionData.Config.NetworkResourceManagementEnabled
                LinkDiscoveryProtocol = $vds.LinkDiscoveryProtocol
                LinkDiscoveryProtocolOperation = $vds.LinkDiscoveryProtocolOperation
                PortGroups = @()
            }
            
            # Get all port groups for this vDS
            Write-LogInfo "Retrieving port groups for vDS: $($vds.Name)" -Category "Export"
            $portGroups = Get-VDPortgroup -VDSwitch $vds
            
            foreach ($pg in $portGroups) {
                # Skip uplink port groups
                if ($pg.IsUplink) {
                    Write-LogDebug "Skipping uplink port group: $($pg.Name)" -Category "Export"
                    continue
                }
                
                Write-LogDebug "Processing port group: $($pg.Name)" -Category "Export"
                
                # Extract VLAN configuration
                $vlanConfig = @{
                    Type = "None"
                    VlanId = 0
                    VlanTrunkRange = @()
                    PrivateVlanId = 0
                }
                
                if ($pg.VlanConfiguration) {
                    if ($pg.VlanConfiguration.VlanId) {
                        $vlanConfig.Type = "VLAN"
                        $vlanConfig.VlanId = $pg.VlanConfiguration.VlanId
                    }
                    elseif ($pg.VlanConfiguration.VlanType -eq "Trunk") {
                        $vlanConfig.Type = "Trunk"
                        $vlanConfig.VlanTrunkRange = $pg.VlanConfiguration.Ranges
                    }
                    elseif ($pg.VlanConfiguration.PrivateVlanId) {
                        $vlanConfig.Type = "PrivateVLAN"
                        $vlanConfig.PrivateVlanId = $pg.VlanConfiguration.PrivateVlanId
                    }
                }
                
                # Create port group export object
                $pgExport = @{
                    Name = $pg.Name
                    Key = $pg.Key
                    NumPorts = $pg.NumPorts
                    PortBinding = $pg.PortBinding
                    Notes = $pg.Notes
                    VlanConfiguration = $vlanConfig
                    AutoExpand = $pg.ExtensionData.Config.AutoExpand
                    ConfigVersion = $pg.ExtensionData.Config.ConfigVersion
                    Type = $pg.ExtensionData.Config.Type
                    BackingType = if ($pg.ExtensionData.Config.BackingType) { $pg.ExtensionData.Config.BackingType } else { "standard" }
                    
                    # Security Policy
                    SecurityPolicy = @{
                        AllowPromiscuous = $pg.ExtensionData.Config.DefaultPortConfig.SecurityPolicy.AllowPromiscuous.Value
                        ForgedTransmits = $pg.ExtensionData.Config.DefaultPortConfig.SecurityPolicy.ForgedTransmits.Value
                        MacChanges = $pg.ExtensionData.Config.DefaultPortConfig.SecurityPolicy.MacChanges.Value
                    }
                    
                    # Teaming Policy
                    TeamingPolicy = @{
                        Policy = if ($pg.ExtensionData.Config.DefaultPortConfig.UplinkTeamingPolicy.Policy.Value) {
                            $pg.ExtensionData.Config.DefaultPortConfig.UplinkTeamingPolicy.Policy.Value
                        } else { "loadbalance_srcid" }
                        ReversePolicy = $pg.ExtensionData.Config.DefaultPortConfig.UplinkTeamingPolicy.ReversePolicy.Value
                        NotifySwitches = $pg.ExtensionData.Config.DefaultPortConfig.UplinkTeamingPolicy.NotifySwitches.Value
                        RollingOrder = $pg.ExtensionData.Config.DefaultPortConfig.UplinkTeamingPolicy.RollingOrder.Value
                    }
                    
                    # Traffic Shaping (if configured)
                    TrafficShaping = @{
                        Enabled = $pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.Enabled.Value
                        AverageBandwidth = if ($pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.AverageBandwidth) {
                            $pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.AverageBandwidth.Value
                        } else { 0 }
                        PeakBandwidth = if ($pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.PeakBandwidth) {
                            $pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.PeakBandwidth.Value
                        } else { 0 }
                        BurstSize = if ($pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.BurstSize) {
                            $pg.ExtensionData.Config.DefaultPortConfig.InShapingPolicy.BurstSize.Value
                        } else { 0 }
                    }
                }
                
                $vdsExport.PortGroups += $pgExport
                $exportData.TotalPortGroups++
            }
            
            $exportData.VDSSwitches += $vdsExport
            $exportData.TotalSwitches++
            
            Write-LogSuccess "Exported vDS '$($vds.Name)' with $($vdsExport.PortGroups.Count) port groups" -Category "Export"
        }
    }
    
    # Ensure export directory exists
    $exportDir = Split-Path -Parent $ExportPath
    if (-not (Test-Path $exportDir)) {
        New-Item -ItemType Directory -Path $exportDir -Force | Out-Null
        Write-LogInfo "Created export directory: $exportDir" -Category "Export"
    }
    
    # Export to JSON file
    Write-LogInfo "Writing export data to: $ExportPath" -Category "Export"
    $exportData | ConvertTo-Json -Depth 10 | Out-File -FilePath $ExportPath -Encoding UTF8
    
    # Verify the export file was created
    if (Test-Path $ExportPath) {
        $fileSize = (Get-Item $ExportPath).Length
        Write-LogSuccess "Export file created successfully (Size: $($fileSize / 1KB) KB)" -Category "Export"
    }
    else {
        throw "Export file was not created at: $ExportPath"
    }
    
    $scriptSuccess = $true
    $finalSummary = "Successfully exported $($exportData.TotalSwitches) vDS switches with $($exportData.TotalPortGroups) port groups"
    
    # Output summary for the application
    Write-Output "SUCCESS: Exported $($exportData.TotalSwitches) vDS switches and $($exportData.TotalPortGroups) port groups to $ExportPath"
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Export failed: $($_.Exception.Message)"
    Write-LogError "Export failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary
}