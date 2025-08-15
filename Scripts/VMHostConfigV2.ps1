<#
.SYNOPSIS
    Manage VMware ESXi host configurations when migrating between vCenter servers. Version 2 - Modularized, Parameter Sets, WhatIf, and Help.

.DESCRIPTION
    This script allows you to backup, restore, and migrate VMware ESXi host configurations. It provides a modular design, uses parameter sets for clear parameter requirements, supports WhatIf for testing, and includes comprehensive error handling and logging. The script is designed to simplify the process of managing ESXi host configurations, especially when migrating hosts between vCenter servers.

.PARAMETER Action
    Specifies the action to perform. Valid values are "Backup", "Restore", and "Migrate".

.PARAMETER vCenter
    Specifies the name or IP address of the vCenter server. This parameter is mandatory for Backup and Restore actions.

.PARAMETER VMHostName
    Specifies the name of the ESXi host. This parameter is mandatory for Backup and Restore actions.

.PARAMETER BackupPath
    Specifies the path to the directory where backup files will be stored. If not specified, the current directory will be used.

.PARAMETER BackupFile
    Specifies the path to the backup file to use for the Restore action. This parameter is mandatory for the Restore action.

.PARAMETER Credential
    Specifies the credentials to use to connect to the vCenter server. This parameter is mandatory for Backup and Restore actions.

.PARAMETER LogPath
    Specifies the path to the directory where log files will be stored. If not specified, the current directory will be used.

.PARAMETER TargetVCenter
    Specifies the name or IP address of the target vCenter server. This parameter is mandatory for the Migrate action.

.PARAMETER SourceVCenter
    Specifies the name or IP address of the source vCenter server. This parameter is mandatory for the Migrate action.

.PARAMETER SourceCredential
    Specifies the credentials to use to connect to the source vCenter server. This parameter is optional for the Migrate action.

.PARAMETER TargetCredential
    Specifies the credentials to use to connect to the target vCenter server. This parameter is optional for the Migrate action.

.PARAMETER ESXiHostCredential
    Specifies the credentials to use to connect directly to the ESXi host. This parameter is mandatory for the Migrate action.

.PARAMETER TargetDatacenterName
    Specifies the name of the datacenter in the target vCenter server where the host will be added. If not specified, the first datacenter found will be used.

.PARAMETER TargetClusterName
    Specifies the name of the cluster in the target vCenter server where the host will be added. If not specified, the host will be added to the datacenter directly.

.PARAMETER OperationTimeout
    Specifies the timeout, in seconds, for long-running operations. The default value is 600 seconds.

.PARAMETER UplinkPortgroupName
    Specifies the name of the uplink portgroup on the distributed switch. This parameter is optional and can be used to explicitly specify the uplink portgroup if automatic discovery fails.

.EXAMPLE
    .\VMHostConfigV2.ps1 -Action Backup -vCenter "your_vcenter.example.com" -VMHostName "esxi_host.example.com" -Credential (Get-Credential) -BackupPath "C:\Backups"

    This example backs up the configuration of the ESXi host "esxi_host.example.com" to the directory "C:\Backups", using the specified credentials to connect to the vCenter server "your_vcenter.example.com".

.EXAMPLE
    .\VMHostConfigV2.ps1 -Action Restore -vCenter "your_vcenter.example.com" -VMHostName "esxi_host.example.com" -Credential (Get-Credential) -BackupFile "C:\Backups\VMHost_esxi_host_20231027_100000.json"

    This example restores the configuration of the ESXi host "esxi_host.example.com" from the backup file "C:\Backups\VMHost_esxi_host_20231027_100000.json", using the specified credentials to connect to the vCenter server "your_vcenter.example.com".

.EXAMPLE
    .\VMHostConfigV2.ps1 -Action Migrate -SourceVCenter "source_vcenter.example.com" -TargetVCenter "target_vcenter.example.com" -VMHostName "esxi_host.example.com" -ESXiHostCredential (Get-Credential) -SourceCredential (Get-Credential) -TargetCredential (Get-Credential) -TargetDatacenterName "TargetDatacenter" -TargetClusterName "TargetCluster" -BackupPath "C:\Backups" -UplinkPortgroupName "Uplink1"

    This example migrates the ESXi host "esxi_host.example.com" from the source vCenter server "source_vcenter.example.com" to the target vCenter server "target_vcenter.example.com", using the specified credentials. The host will be added to the "TargetDatacenter" datacenter and the "TargetCluster" cluster in the target vCenter server. The UplinkPortgroupName is explicitly set to Uplink1

.NOTES
    This script requires the VMware PowerCLI module to be installed. It also requires the user to have the necessary permissions to perform the specified actions on the ESXi host and vCenter server.
    The script has been tested with PowerCLI version 13.x and ESXi version 7.x/8.x.
    The script may not work correctly in all environments. It is recommended to test the script in a lab environment before using it in production.
    The script relies on certain naming conventions for distributed switch uplink portgroups. If the automatic discovery of the uplink portgroup fails, you may need to specify the UplinkPortgroupName parameter explicitly.
    The -WhatIf parameter provides an overview of the actions that would be performed but does not guarantee that all actions will succeed when run without -WhatIf.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ParameterSetName = "Backup")]
    [Parameter(Mandatory = $true, ParameterSetName = "Restore")]
    [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
    [ValidateSet("Backup", "Restore", "Migrate")]
    [string]$Action,

    [Parameter(Mandatory = $true, ParameterSetName = "Backup")]
    [Parameter(Mandatory = $true, ParameterSetName = "Restore")]
    [ValidateNotNullOrEmpty()]
    [string]$vCenter,

    [Parameter(Mandatory = $true, ParameterSetName = "Backup")]
    [Parameter(Mandatory = $true, ParameterSetName = "Restore")]
    [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
    [ValidateNotNullOrEmpty()]
    [string]$VMHostName,

    [Parameter(Mandatory = $false, ParameterSetName = "Backup")]
    [string]$BackupPath = (Get-Location).Path,

    [Parameter(Mandatory = $false, ParameterSetName = "Restore")]
    [string]$BackupFile,

    [Parameter(Mandatory = $true, ParameterSetName = "Backup")]
    [Parameter(Mandatory = $true, ParameterSetName = "Restore")]
    [System.Management.Automation.PSCredential]$Credential,

    [Parameter(Mandatory = $false, ParameterSetName = "Backup")]
    [Parameter(Mandatory = $false, ParameterSetName = "Restore")]
    [Parameter(Mandatory = $false, ParameterSetName = "Migrate")]
    [string]$LogPath = (Get-Location).Path,

    [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
    [ValidateNotNullOrEmpty()]
    [string]$SourceVCenter,

    [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
    [ValidateNotNullOrEmpty()]
    [string]$TargetVCenter,

    [Parameter(Mandatory = $false, ParameterSetName = "Migrate")]
    [System.Management.Automation.PSCredential]$SourceCredential,

    [Parameter(Mandatory = $false, ParameterSetName = "Migrate")]
    [System.Management.Automation.PSCredential]$TargetCredential,

    [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
    [System.Management.Automation.PSCredential]$ESXiHostCredential,

    [Parameter(Mandatory = $false)]
    [string]$TargetDatacenterName,

    [Parameter(Mandatory = $false)]
    [string]$TargetClusterName,

    [Parameter(Mandatory = $false)]
    [int]$OperationTimeout = 600,

    [Parameter(Mandatory = $false)]
    [string]$UplinkPortgroupName
)

#region Enhanced logging initialization
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path -Path $LogPath -ChildPath "VMHostConfig_$($VMHostName.Split('.')[0])_$($timestamp).log"

function Write-Log {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter(Mandatory = $false)]
        [ValidateSet("INFO", "WARNING", "ERROR", "DEBUG", "SUCCESS")]
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"

    # Enhanced console output with colors
    switch ($Level) {
        "INFO" { Write-Host $logEntry -ForegroundColor White }
        "WARNING" { Write-Host $logEntry -ForegroundColor Yellow }
        "ERROR" { Write-Host $logEntry -ForegroundColor Red }
        "DEBUG" { Write-Host $logEntry -ForegroundColor Cyan }
        "SUCCESS" { Write-Host $logEntry -ForegroundColor Green }
    }

    # Write to log file with error handling
    try {
        Add-Content -Path $logFile -Value $logEntry -ErrorAction Stop
    }
    catch {
        Write-Host "[$timestamp] [ERROR] Failed to write to log file: $($_)" -ForegroundColor Red
    }
}
#endregion

#region vCenter Connection Functions
function Disconnect-AllVIServers {
    [CmdletBinding()]
    param()

    Write-Log "Checking for existing vCenter connections..." -Level INFO
    try {
        if ($global:DefaultVIServers) {
            foreach ($server in $global:DefaultVIServers) {
                if ($server.IsConnected) {
                    Write-Log "Disconnecting from $($server.Name)..." -Level INFO
                    try {
                        if ($PSCmdlet.ShouldProcess($server.Name, "Disconnecting from vCenter")) {
                            Disconnect-VIServer -Server $server -Confirm:$false -ErrorAction Stop
                            Write-Log "Successfully disconnected from $($server.Name)." -Level INFO
                        }
                    }
                    catch {
                        Write-Log "Error disconnecting from $($server.Name): $($_)" -Level WARNING
                    }
                }
            }
        }
        else {
            Write-Log "No existing vCenter connections found." -Level INFO
        }
        Write-Log "Finished checking and disconnecting existing vCenter connections." -Level INFO
    }
    catch {
        Write-Log "Error checking/disconnecting existing vCenter connections: $($_)" -Level WARNING
    }
}

function Connect-ToVIServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Server,
        [Parameter(Mandatory = $false)]
        [System.Management.Automation.PSCredential]$Credential
    )

    Write-Log "Connecting to vCenter $($Server)..." -Level INFO
    try {
        if ($PSCmdlet.ShouldProcess($Server, "Connecting to vCenter")) {
            if ($Credential) {
                $vc = Connect-VIServer -Server $Server -Credential $Credential -ErrorAction Stop
            }
            else {
                $vc = Connect-VIServer -Server $Server -ErrorAction Stop
            }
            Write-Log "Successfully connected to vCenter $($Server)" -Level SUCCESS
            return $vc
        }
    }
    catch {
        Write-Log "Failed to connect to vCenter $($Server): $($_)" -Level ERROR
        throw $_
    }
}
#endregion

#region Host Status Functions
function Test-VMHostConnection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost)

    try {
        $connectionState = $VMHost.ConnectionState
        $powerState      = $VMHost.PowerState

        if ($connectionState -ne 'Maintenance') {
            Write-Log "Host $($VMHost.Name) is in $($connectionState) state" -Level WARNING
            return $false
        }

        return $true
    }
    catch {
        Write-Log "Error checking host connection state: $($_)" -Level ERROR
        return $false
    }   
}
#endregion

#region Lockdown Mode Functions
function Test-ESXiHostLockdownMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ESXiHostName,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    
    Write-Log "Checking lockdown mode status for host $ESXiHostName" -Level INFO
    
    try {
        # Connect directly to the ESXi host to check lockdown mode
        $directHostConnection = Connect-VIServer -Server $ESXiHostName -Credential $Credential -ErrorAction Stop
        Write-Log "Connected directly to ESXi host to check lockdown mode" -Level DEBUG
        
        try {
            # Get the host object
            $vmHost = Get-VMHost -Server $directHostConnection -ErrorAction Stop
            
            # Check lockdown mode status
            $lockdownMode = $vmHost.ExtensionData.Config.LockdownMode
            
            Write-Log "Host lockdown mode status: $lockdownMode" -Level INFO
            
            # Return true if host is in any lockdown mode
            $isInLockdown = ($lockdownMode -ne "lockdownDisabled")
            
            if ($isInLockdown) {
                Write-Log "Host $ESXiHostName is in lockdown mode: $lockdownMode" -Level WARNING
            } else {
                Write-Log "Host $ESXiHostName is not in lockdown mode" -Level SUCCESS
            }
            
            return $isInLockdown
        }
        finally {
            # Always disconnect from the direct host connection
            Disconnect-VIServer -Server $directHostConnection -Confirm:$false -Force -ErrorAction SilentlyContinue
            Write-Log "Disconnected from direct ESXi host connection" -Level DEBUG
        }
    }
    catch {
        Write-Log "Error checking lockdown mode for host $ESXiHostName`: $($_)" -Level ERROR
        # If we can't check, assume it might be in lockdown mode for safety
        return $true
    }
}

function Disable-ESXiHostLockdownMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ESXiHostName,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    
    Write-Log "Attempting to disable lockdown mode for host $ESXiHostName" -Level INFO
    
    try {
        # Connect directly to the ESXi host
        $directHostConnection = Connect-VIServer -Server $ESXiHostName -Credential $Credential -ErrorAction Stop
        Write-Log "Connected directly to ESXi host to disable lockdown mode" -Level DEBUG
        
        try {
            # Get the host object
            $vmHost = Get-VMHost -Server $directHostConnection -ErrorAction Stop
            
            # Get current lockdown mode
            $currentLockdownMode = $vmHost.ExtensionData.Config.LockdownMode
            Write-Log "Current lockdown mode: $currentLockdownMode" -Level INFO
            
            if ($currentLockdownMode -ne "lockdownDisabled") {
                Write-Log "Disabling lockdown mode..." -Level INFO
                
                # Get the host configuration manager
                $hostConfigManager = Get-View -Id $vmHost.ExtensionData.ConfigManager.HostAccessManager -Server $directHostConnection
                
                # Disable lockdown mode
                $hostConfigManager.ChangeLockdownMode("lockdownDisabled")
                
                # Wait a moment for the change to take effect
                Start-Sleep -Seconds 5
                
                # Verify the change
                $vmHost = Get-VMHost -Server $directHostConnection -ErrorAction Stop
                $newLockdownMode = $vmHost.ExtensionData.Config.LockdownMode
                
                if ($newLockdownMode -eq "lockdownDisabled") {
                    Write-Log "Successfully disabled lockdown mode on host $ESXiHostName" -Level SUCCESS
                    return $true
                } else {
                    Write-Log "Failed to disable lockdown mode. Current mode: $newLockdownMode" -Level ERROR
                    return $false
                }
            } else {
                Write-Log "Host $ESXiHostName is not in lockdown mode" -Level INFO
                return $true
            }
        }
        finally {
            # Always disconnect from the direct host connection
            Disconnect-VIServer -Server $directHostConnection -Confirm:$false -Force -ErrorAction SilentlyContinue
            Write-Log "Disconnected from direct ESXi host connection" -Level DEBUG
        }
    }
    catch {
        Write-Log "Error disabling lockdown mode for host $ESXiHostName`: $($_)" -Level ERROR
        return $false
    }
}

function Enable-ESXiHostLockdownMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ESXiHostName,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential,
        [Parameter(Mandatory = $true)]
        [string]$LockdownMode  # "lockdownNormal" or "lockdownStrict"
    )
    
    Write-Log "Attempting to enable lockdown mode ($LockdownMode) for host $ESXiHostName" -Level INFO
    
    try {
        # Connect directly to the ESXi host
        $directHostConnection = Connect-VIServer -Server $ESXiHostName -Credential $Credential -ErrorAction Stop
        Write-Log "Connected directly to ESXi host to enable lockdown mode" -Level DEBUG
        
        try {
            # Get the host object
            $vmHost = Get-VMHost -Server $directHostConnection -ErrorAction Stop
            
            Write-Log "Enabling lockdown mode: $LockdownMode" -Level INFO
            
            # Get the host configuration manager
            $hostConfigManager = Get-View -Id $vmHost.ExtensionData.ConfigManager.HostAccessManager -Server $directHostConnection
            
            # Enable lockdown mode
            $hostConfigManager.ChangeLockdownMode($LockdownMode)
            
            # Wait a moment for the change to take effect
            Start-Sleep -Seconds 5
            
            # Verify the change
            $vmHost = Get-VMHost -Server $directHostConnection -ErrorAction Stop
            $newLockdownMode = $vmHost.ExtensionData.Config.LockdownMode
            
            if ($newLockdownMode -eq $LockdownMode) {
                Write-Log "Successfully enabled lockdown mode ($LockdownMode) on host $ESXiHostName" -Level SUCCESS
                return $true
            } else {
                Write-Log "Failed to enable lockdown mode. Current mode: $newLockdownMode" -Level ERROR
                return $false
            }
        }
        finally {
            # Always disconnect from the direct host connection
            Disconnect-VIServer -Server $directHostConnection -Confirm:$false -Force -ErrorAction SilentlyContinue
            Write-Log "Disconnected from direct ESXi host connection" -Level DEBUG
        }
    }
    catch {
        Write-Log "Error enabling lockdown mode for host $ESXiHostName`: $($_)" -Level ERROR
        return $false
    }
}
#endregion

#region Backup Functions
function Backup-VMHostConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$Server,

        [Parameter(Mandatory = $true)]
        [string]$BackupPath
    )

    Write-Log "Starting backup of host configuration for $($Server.Name)" -Level INFO

    try {
        # Enhanced host connection validation
        $hostIsOk = Test-VMHostConnection -VMHost $Server
        if (-not $hostIsOk) {
            throw "Host $($Server.Name) is not in a proper state for backup (Conn: $($Server.ConnectionState), Power: $($Server.PowerState))"
        }

        # Enhanced configuration collection with progress tracking
        $hostConfig = [ordered]@{
            Metadata = [ordered]@{
                BackupDate = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
                ScriptVersion = "2.1"
                HostName = $Server.Name
                vCenterServer = $global:DefaultVIServer.Name
            }
            HostConfig = [ordered]@{}
        }

        # Collect basic host information with error handling
        $hostConfig.HostConfig["BasicInfo"] = [ordered]@{
            Name      = $Server.Name
            Version   = $Server.Version
            Build     = $Server.Build
            Cluster   = $Server.Parent.Name
            Timezone  = (Get-VMHostService -VMHost $Server | Where-Object { $_.Key -eq "time" }).Timezone
        }

        # Get datacenter information with fallback
        try {
            $datacenter = Get-Datacenter -VMHost $Server -ErrorAction Stop
            $hostConfig.HostConfig["Datacenter"] = $datacenter.Name
        }
        catch {
            Write-Log "Warning: Could not determine datacenter - $($_)" -Level WARNING
            $hostConfig.HostConfig["Datacenter"] = "Unknown"
        }

        # Collect Network Configuration
        $hostConfig.HostConfig["Network"] = Get-VMHostNetworkConfiguration -VMHost $Server

        # Enhanced storage configuration
        $hostConfig.HostConfig["Storage"] = Get-VMHostStorageConfiguration -VMHost $Server

        # Enhanced services configuration
        $hostConfig.HostConfig["Services"] = Get-VMHostServicesConfiguration -VMHost $Server

        # Enhanced firewall configuration
        $hostConfig.HostConfig["Firewall"] = Get-VMHostFirewallConfiguration -VMHost $Server

        # Enhanced advanced settings
        $hostConfig.HostConfig["AdvancedSettings"] = Get-VMHostAdvancedSettings -VMHost $Server

        # Enhanced time configuration
        $hostConfig.HostConfig["TimeConfig"] = Get-VMHostTimeConfiguration -VMHost $Server

        # Enhanced DNS configuration
        $hostConfig.HostConfig["DNSConfig"] = Get-VMHostDNSConfiguration -VMHost $Server

        # Enhanced syslog configuration
        $hostConfig.HostConfig["SyslogConfig"] = Get-VMHostSysLogConfiguration -VMHost $Server

        # Enhanced power management configuration
        $hostConfig.HostConfig["PowerConfig"] = Get-VMHostPowerConfiguration -VMHost $Server

        # Create backup directory if it doesn't exist
        if (-not (Test-Path -Path $BackupPath)) {
            New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
        }

        # Create backup filename with hostname and timestamp
        $backupFile = Join-Path -Path $BackupPath -ChildPath "VMHost_$($Server.Name.Split('.')[0])_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"

        # Export configuration to JSON with increased depth and formatting
        $hostConfig | ConvertTo-Json -Depth 10 | Out-File -FilePath $backupFile -Force
        Write-Log "Host configuration successfully backed up to: $($backupFile)" -Level SUCCESS
        
        return $backupFile
    }
    catch {
        Write-Log "Critical error during backup process: $($_)" -Level ERROR
        Write-Log "Error details: $($_.Exception.Message)" -Level ERROR
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level DEBUG
        throw "Backup failed: $($_)"
    }
}
#endregion

#region Sub-Configuration Backup Functions
function Get-VMHostNetworkConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting network configuration for $($VMHost.Name)" -Level DEBUG
    $networkConfig = [ordered]@{}

    # VMkernel adapters with enhanced properties
    try {
        $vmkAdapters = @()
        Get-VMHostNetworkAdapter -VMHost $VMHost -VMKernel -ErrorAction Stop | ForEach-Object {
            $vmkAdapters += [ordered]@{
                Name = $_.Name
                IP = $_.IP
                SubnetMask = $_.SubnetMask
                Mac = $_.Mac
                PortGroupName = $_.PortGroupName
                VMotionEnabled = $_.VMotionEnabled
                FaultToleranceLoggingEnabled = $_.FaultToleranceLoggingEnabled
                Mtu = $_.Mtu
                VsanTrafficEnabled = $_.VsanTrafficEnabled
                ManagementTrafficEnabled = $_.ManagementTrafficEnabled
                DistributedSwitch = ($_.VirtualSwitch -is [VMware.Vim.DistributedVirtualSwitch])
            }
        }
        $networkConfig["VMKernelAdapters"] = $vmkAdapters
    }
    catch {
        Write-Log "Error collecting VMkernel adapters: $($_)" -Level WARNING
        $networkConfig["VMKernelAdapters"] = @()
    }

    # Physical adapters with more details including PCI information
    try {
        $physicalAdapters = @()
        Get-VMHostNetworkAdapter -VMHost $VMHost -Physical -ErrorAction Stop | ForEach-Object {
            $pnicInfo = $_.ExtensionData
            $physicalAdapters += [ordered]@{
                Name = $_.Name
                Mac = $_.Mac
                BitRatePerSec = $_.BitRatePerSec
                FullDuplex = $_.FullDuplex
                Mtu = $_.Mtu
                Driver = $pnicInfo.Driver
                PCI = $pnicInfo.Pci
                WakeOnLanSupported = $_.WakeOnLanSupported
                Active = $_.LinkSpeed
                ConfiguredSpeed = $_.Spec.LinkSpeed
                UplinkFor = @()
            }
        }
        $networkConfig["PhysicalAdapters"] = $physicalAdapters
    }
    catch {
        Write-Log "Error collecting physical adapters: $($_)" -Level WARNING
        $networkConfig["PhysicalAdapters"] = @()
    }

    # Standard virtual switches with port groups
    try {
        $vSwitches = @()
        Get-VirtualSwitch -VMHost $VMHost -Standard -ErrorAction Stop | ForEach-Object {
            $switchInfo = [ordered]@{
                Name = $_.Name
                Nic = $_.Nic
                Mtu = $_.Mtu
                NumPorts = $_.NumPorts
                PortGroups = @()
            }

            # Get associated port groups
            Get-VirtualPortGroup -VirtualSwitch $_ -ErrorAction SilentlyContinue | ForEach-Object {
                $switchInfo.PortGroups += [ordered]@{
                    Name = $_.Name
                    VLanId = $_.VLanId
                    SecurityPolicy = if ($_.ExtensionData.Spec.Policy.Security) {
                        [ordered]@{
                            AllowPromiscuous = $_.ExtensionData.Spec.Policy.Security.AllowPromiscuous
                            MacChanges = $_.ExtensionData.Spec.Policy.Security.MacChanges
                            ForgedTransmits = $_.ExtensionData.Spec.Policy.Security.ForgedTransmits
                        }
                    }
                }
            }

            $vSwitches += $switchInfo
        }
        $networkConfig["StandardSwitches"] = $vSwitches
    }
    catch {
        Write-Log "Error collecting standard switches: $($_)" -Level WARNING
        $networkConfig["StandardSwitches"] = @()
    }

    # Enhanced Distributed Switch configuration with uplink mapping
    try {
        $distributedSwitches = @()
        Get-VDSwitch -VMHost $VMHost -ErrorAction SilentlyContinue | ForEach-Object {
            $vds = $_
            $vdsConfig = [ordered]@{
                Name = $vds.Name
                MTU = $vds.Mtu
                NumPorts = $vds.NumPorts
                Version = $vds.Version
                PortGroups = @()
                VMkernelAdapters = @()
                Uplinks = @()
                UplinkPortgroup = ""
            }

            # Get VDS port groups
            Get-VDPortgroup -VDSwitch $vds -ErrorAction SilentlyContinue | ForEach-Object {
                $vdsConfig.PortGroups += [ordered]@{
                    Name = $_.Name
                    VLANId = $_.VlanConfiguration.VlanId
                    Type = $_.PortBinding
                    NumPorts = $_.NumPorts
                    IsUplink = $_.IsUplink
                }
            }

            # Get VMkernel adapters on this VDS
            Get-VMHostNetworkAdapter -VMHost $VMHost -VirtualSwitch $vds -ErrorAction SilentlyContinue | ForEach-Object {
                $vdsConfig.VMkernelAdapters += [ordered]@{
                    Name = $_.Name
                    PortGroupName = $_.PortGroupName
                    IP = $_.IP
                    SubnetMask = $_.SubnetMask
                    Mtu = $_.Mtu
                    TrafficTypes = @(
                        if ($_.ManagementTrafficEnabled) { "Management" }
                        if ($_.VMotionEnabled) { "VMotion" }
                        if ($_.VsanTrafficEnabled) { "VSAN" }
                        if ($_.FaultToleranceLoggingEnabled) { "FaultTolerance" }
                    )
                }
            }

            # Get uplink portgroup using the improved method
            $uplinkPg = Find-VDSwitchUplinkPortgroup -VDSwitch $vds -ErrorAction SilentlyContinue

            if ($uplinkPg) {
                $vdsConfig.UplinkPortgroup = $uplinkPg.Name
                Write-Log "Found uplink portgroup for $($vds.Name): $($uplinkPg.Name)" -Level DEBUG

                # Get physical NICs connected to this VDS using improved method
                $connectedPnics = Get-VDSConnectedPhysicalNics -VMHost $VMHost -VDSwitch $vds

                if ($connectedPnics -and $connectedPnics.Count -gt 0) {
                    Write-Log "Found $($connectedPnics.Count) physical NICs connected to VDS: $($vds.Name)" -Level DEBUG

                    foreach ($pnic in $connectedPnics) {
                        $uplinkInfo = [ordered]@{
                            UplinkName = "uplink" # Default name
                            UplinkPortgroup = $uplinkPg.Name
                            PhysicalNic = $pnic.Name
                            MacAddress = $pnic.Mac
                            PCI = $pnic.ExtensionData.Pci
                            DeviceName = $pnic.DeviceName
                        }

                        # Try to determine the specific uplink name
                        try {
                            $pnicSpec = $pnic.ExtensionData.Spec.DistributedVirtualPort
                            if ($pnicSpec -and $pnicSpec.PortKey) {
                                $portKey = $pnicSpec.PortKey
                                $vdsView = Get-View $($vds.Id)
                                $portCriteria = New-Object VMware.Vim.DistributedVirtualSwitchPortCriteria
                                $portCriteria.PortKey = @($portKey)
                                $ports = $vdsView.FetchPorts($portCriteria)

                                if ($ports -and $ports.Count -gt 0) {
                                    $portConnectee = $ports[0].Connectee
                                    if ($portConnectee -and $portConnectee.NicKey) {
                                        $uplinkInfo.UplinkName = $portConnectee.NicKey
                                    }
                                }
                            }
                        }
                        catch {
                            Write-Log "Error getting uplink name for $($pnic.Name): $($_)" -Level DEBUG
                        }

                        $vdsConfig.Uplinks += $uplinkInfo

                        # Update the physical adapters list with uplink information
                        $pnicIndex = $networkConfig.PhysicalAdapters | Where-Object { $_.Name -eq $pnic.Name }
                        if ($pnicIndex) {
                            $pnicIndex.UplinkFor += "$($vds.Name)/$($uplinkInfo.UplinkName)"
                        }
                    }
                }
                else {
                    Write-Log "No physical NICs found connected to VDS: $($vds.Name)" -Level WARNING
                }
            }
            else {
                Write-Log "No uplink portgroup found for VDS: $($vds.Name)" -Level WARNING
            }

            $distributedSwitches += $vdsConfig
        }
        $networkConfig["DistributedSwitches"] = $distributedSwitches
    }
    catch {
        Write-Log "Error collecting distributed switches: $($_)" -Level WARNING
        $networkConfig["DistributedSwitches"] = @()
    }

    return $networkConfig
}

function Get-VDSConnectedPhysicalNics {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost,
        
        [Parameter(Mandatory = $true)]
        $VDSwitch
    )
    
    try {
        Write-Log "Searching for physical NICs connected to VDS: $($VDSwitch.Name)" -Level DEBUG
        
        # Get all physical NICs on the host
        $allPhysicalNics = Get-VMHostNetworkAdapter -VMHost $VMHost -Physical -ErrorAction Stop
        
        # Method 1: Check via DistributedVirtualPort specification
        $connectedNics = @()
        foreach ($pnic in $allPhysicalNics) {
            try {
                $pnicSpec = $pnic.ExtensionData.Spec.DistributedVirtualPort
                if ($pnicSpec -and $pnicSpec.SwitchUuid -eq $VDSwitch.ExtensionData.Uuid) {
                    $connectedNics += $pnic
                    Write-Log "Found connected NIC via DistributedVirtualPort: $($pnic.Name) (MAC: $($pnic.Mac))" -Level DEBUG
                }
            }
            catch {
                Write-Log "Error checking NIC $($pnic.Name) via DistributedVirtualPort: $($_)" -Level DEBUG
            }
        }
        
        # Method 2: If no NICs found via Method 1, try checking via VDS host configuration
        if ($connectedNics.Count -eq 0) {
            Write-Log "No NICs found via DistributedVirtualPort, trying VDS host configuration method" -Level DEBUG
            
            try {
                # Get the VDS view and check its host configuration
                $vdsView = Get-View -Id $VDSwitch.ExtensionData.MoRef -ErrorAction Stop
                
                # Find this host in the VDS configuration
                $hostConfig = $vdsView.Config.Host | Where-Object { 
                    $hostMoRef = Get-View -Id $_.Host -ErrorAction SilentlyContinue
                    $hostMoRef -and $hostMoRef.Name -eq $VMHost.Name 
                }
                
                if ($hostConfig) {
                    Write-Log "Found host configuration in VDS" -Level DEBUG
                    
                    # Check the backing configuration for physical NICs
                    if ($hostConfig.Config -and $hostConfig.Config.Backing) {
                        foreach ($backing in $hostConfig.Config.Backing) {
                            if ($backing -is [VMware.Vim.DistributedVirtualSwitchHostMemberPnicBacking]) {
                                $pnicDevice = $backing.PnicSpec.PnicDevice
                                Write-Log "Found backing for device: $pnicDevice" -Level DEBUG
                                
                                # Find the corresponding physical NIC
                                $matchingPnic = $allPhysicalNics | Where-Object { 
                                    $_.Name -eq $pnicDevice -or $_.DeviceName -eq $pnicDevice 
                                }
                                
                                if ($matchingPnic) {
                                    if ($connectedNics -notcontains $matchingPnic) {
                                        $connectedNics += $matchingPnic
                                        Write-Log "Found connected NIC via VDS backing: $($matchingPnic.Name) (MAC: $($matchingPnic.Mac))" -Level DEBUG
                                    }
                                } else {
                                    Write-Log "Could not find physical NIC for device: $pnicDevice" -Level WARNING
                                }
                            }
                        }
                    }
                }
            }
            catch {
                Write-Log "Error checking VDS host configuration: $($_)" -Level DEBUG
            }
        }
        
        # Method 3: If still no NICs found, try using the host's network system
        if ($connectedNics.Count -eq 0) {
            Write-Log "No NICs found via VDS configuration, trying host network system method" -Level DEBUG
            
            try {
                $hostNetworkSystem = Get-View -Id $VMHost.ExtensionData.ConfigManager.NetworkSystem -ErrorAction Stop
                $networkConfig = $hostNetworkSystem.NetworkConfig
                
                # Check proxy switches for this VDS
                if ($networkConfig.ProxySwitch) {
                    $proxySwitch = $networkConfig.ProxySwitch | Where-Object { 
                        $_.Uuid -eq $VDSwitch.ExtensionData.Uuid 
                    }
                    
                    if ($proxySwitch) {
                        Write-Log "Found proxy switch configuration" -Level DEBUG
                        
                        # Check uplink ports
                        if ($proxySwitch.Spec -and $proxySwitch.Spec.UplinkPort) {
                            foreach ($uplinkPort in $proxySwitch.Spec.UplinkPort) {
                                if ($uplinkPort.UplinkPortKey) {
                                    Write-Log "Found uplink port: $($uplinkPort.UplinkPortKey)" -Level DEBUG
                                }
                            }
                        }
                    }
                }
                
                # Check physical NIC configurations
                if ($networkConfig.Pnic) {
                    foreach ($pnicConfig in $networkConfig.Pnic) {
                        if ($pnicConfig.Spec -and $pnicConfig.Spec.DistributedVirtualPort) {
                            $dvPort = $pnicConfig.Spec.DistributedVirtualPort
                            if ($dvPort.SwitchUuid -eq $VDSwitch.ExtensionData.Uuid) {
                                $pnicDevice = $pnicConfig.Device
                                Write-Log "Found physical NIC in network config: $pnicDevice" -Level DEBUG
                                
                                $matchingPnic = $allPhysicalNics | Where-Object { 
                                    $_.Name -eq $pnicDevice -or $_.DeviceName -eq $pnicDevice 
                                }
                                
                                if ($matchingPnic -and $connectedNics -notcontains $matchingPnic) {
                                    $connectedNics += $matchingPnic
                                    Write-Log "Found connected NIC via network config: $($matchingPnic.Name) (MAC: $($matchingPnic.Mac))" -Level DEBUG
                                }
                            }
                        }
                    }
                }
            }
            catch {
                Write-Log "Error checking host network system: $($_)" -Level DEBUG
            }
        }
        
        # Method 4: Last resort - check via PowerCLI cmdlets with VDS filter
        if ($connectedNics.Count -eq 0) {
            Write-Log "No NICs found via previous methods, trying PowerCLI VDS filter method" -Level DEBUG
            
            try {
                # Try to get physical NICs associated with this VDS directly
                $vdsPhysicalNics = Get-VMHostNetworkAdapter -VMHost $VMHost -Physical -VirtualSwitch $VDSwitch -ErrorAction SilentlyContinue
                
                if ($vdsPhysicalNics) {
                    foreach ($pnic in $vdsPhysicalNics) {
                        if ($connectedNics -notcontains $pnic) {
                            $connectedNics += $pnic
                            Write-Log "Found connected NIC via PowerCLI VDS filter: $($pnic.Name) (MAC: $($pnic.Mac))" -Level DEBUG
                        }
                    }
                }
            }
            catch {
                Write-Log "Error using PowerCLI VDS filter method: $($_)" -Level DEBUG
            }
        }
        
        # Final verification and logging
        if ($connectedNics.Count -gt 0) {
            Write-Log "Total NICs found connected to VDS $($VDSwitch.Name): $($connectedNics.Count)" -Level DEBUG
            foreach ($nic in $connectedNics) {
                Write-Log "  - Connected NIC: $($nic.Name) (MAC: $($nic.Mac), Device: $($nic.DeviceName))" -Level DEBUG
            }
        } else {
            Write-Log "No physical NICs found connected to VDS: $($VDSwitch.Name)" -Level WARNING
            
            # Debug information - list all physical NICs for troubleshooting
            Write-Log "Available physical NICs on host:" -Level DEBUG
            foreach ($nic in $allPhysicalNics) {
                $dvPortInfo = "None"
                try {
                    if ($nic.ExtensionData.Spec.DistributedVirtualPort) {
                        $dvPort = $nic.ExtensionData.Spec.DistributedVirtualPort
                        $dvPortInfo = "Switch: $($dvPort.SwitchUuid), Port: $($dvPort.PortKey)"
                    }
                }
                catch {
                    $dvPortInfo = "Error reading DV port info"
                }
                Write-Log "  - Available NIC: $($nic.Name) (MAC: $($nic.Mac), DV Port: $dvPortInfo)" -Level DEBUG
            }
        }
        
        return $connectedNics
    }
    catch {
        Write-Log "Error getting connected physical NICs: $($_)" -Level ERROR
        return @()
    }
}

function Find-VDSwitchUplinkPortgroup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $VDSwitch,
        
        [Parameter(Mandatory = $false)]
        [string]$UplinkPortgroupName
    )
    
    try {
        # If a specific uplink portgroup name is provided, try to find it
        if ($UplinkPortgroupName) {
            $uplinkPg = Get-VDPortgroup -VDSwitch $VDSwitch -Name $UplinkPortgroupName -ErrorAction SilentlyContinue
            if ($uplinkPg) {
                Write-Log "Found specified uplink portgroup: $UplinkPortgroupName" -Level DEBUG
                return $uplinkPg
            } else {
                Write-Log "Specified uplink portgroup $UplinkPortgroupName not found. Attempting to find default uplink portgroup." -Level WARNING
            }
        }
        
        # Get all portgroups on the VDS
        $allPortgroups = Get-VDPortgroup -VDSwitch $VDSwitch -ErrorAction Stop
        
        # First try to find portgroups with IsUplink property set to true
        $uplinkPg = $allPortgroups | Where-Object {$_.IsUplink -eq $true} | Select-Object -First 1
        
        # If that doesn't work, try to find portgroups with uplink in the name
        if (-not $uplinkPg) {
            $uplinkPg = $allPortgroups | Where-Object {$_.Name -like "*uplink*"} | Select-Object -First 1
        }
        
        # If still not found, use ExtensionData to find uplink portgroups
        if (-not $uplinkPg) {
            Write-Log "Searching for uplink portgroup using ExtensionData" -Level DEBUG
            
            # Get the VDS view for detailed properties
            $vdsView = Get-View $VDSwitch.ExtensionData.MoRef
            
            # Get the uplink portgroup keys from the VDS config
            $uplinkPortgroupKeys = $vdsView.Config.UplinkPortgroup
            
            if ($uplinkPortgroupKeys -and $uplinkPortgroupKeys.Count -gt 0) {
                # Get the first uplink portgroup using its key
                foreach ($key in $uplinkPortgroupKeys) {
                    $pg = $allPortgroups | Where-Object {$_.ExtensionData.Key -eq $key}
                    if ($pg) {
                        Write-Log "Found uplink portgroup via ExtensionData: $($pg.Name)" -Level DEBUG
                        return $pg
                    }
                }
            }
        }
        
        # Additional fallback: Try to find portgroups that appear to be uplinks based on common naming patterns
        if (-not $uplinkPg) {
            # Common uplink naming patterns
            $commonPatterns = @("*dvuplink*", "*dvs-uplink*", "*uplink*", "*trunk*", "*dvs-trunk*", "*pnic*")
            
            foreach ($pattern in $commonPatterns) {
                $possibleUplink = $allPortgroups | Where-Object {$_.Name -like $pattern} | Select-Object -First 1
                if ($possibleUplink) {
                    Write-Log "Found potential uplink portgroup by name pattern: $($possibleUplink.Name)" -Level DEBUG
                    return $possibleUplink
                }
            }
        }
        
        if ($uplinkPg) {
            Write-Log "Found uplink portgroup: $($uplinkPg.Name)" -Level DEBUG
            return $uplinkPg
        } else {
            Write-Log "No uplink portgroup found for distributed switch $($VDSwitch.Name)" -Level WARNING
            return $null
        }
    }
    catch {
        Write-Log "Error finding uplink portgroup: $($_)" -Level ERROR
        return $null
    }
}

function Get-VMHostStorageConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting storage configuration for $($VMHost.Name)" -Level DEBUG
    $storageConfig = [ordered]@{}
    try {
        $datastores = @()
        Get-Datastore -VMHost $VMHost -ErrorAction Stop | ForEach-Object {
            $datastores += [ordered]@{
                Name = $_.Name
                Type = $_.Type
                CapacityGB = $_.CapacityGB
                FreeSpaceGB = $_.FreeSpaceGB
                FileSystemVersion = $_.FileSystemVersion
                StorageIOControlEnabled = $_.StorageIOControlEnabled
            }
        }
        $storageConfig["Datastores"] = $datastores
    }
    catch {
        Write-Log "Error collecting datastores: $($_)" -Level WARNING
        $storageConfig["Datastores"] = @()
    }

    return $storageConfig
}

function Get-VMHostServicesConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting services configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $services = @()
        Get-VMHostService -VMHost $VMHost -ErrorAction Stop | ForEach-Object {
            $services += [ordered]@{
                Key = $_.Key
                Label = $_.Label
                Policy = $_.Policy
                Running = $_.Running
                Required = $_.Required
                Uninstallable = $_.Uninstallable
            }
        }
        return $services
    }
    catch {
        Write-Log "Error collecting services: $($_)" -Level WARNING
        return @()
    }
}

function Get-VMHostFirewallConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting firewall configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $firewallRules = @()
        Get-VMHostFirewallException -VMHost $VMHost -ErrorAction Stop | ForEach-Object {
            $firewallRules += [ordered]@{
                Name = $_.Name
                Enabled = $_.Enabled
                IncomingPorts = $_.ExtensionData.Spec.Port
                OutgoingPorts = $_.ExtensionData.Spec.EndPort
                Protocol = $_.ExtensionData.Spec.Protocol
                ServiceRunning = $_.ExtensionData.ServiceRunning
            }
        }
        return $firewallRules
    }
    catch {
        Write-Log "Error collecting firewall rules: $($_)" -Level WARNING
        return @()
    }
}

function Get-VMHostAdvancedSettings {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting advanced settings for $($VMHost.Name)" -Level DEBUG
    try {
        $advSettings = @()
        Get-AdvancedSetting -Entity $VMHost -ErrorAction Stop | ForEach-Object {
            $advSettings += [ordered]@{
                Name = $_.Name
                Value = $_.Value
                Description = $_.Description
                Modified = $_.Modified
            }
        }
        return $advSettings
    }
    catch {
        Write-Log "Error collecting advanced settings: $($_)" -Level WARNING
        return @()
    }
}

function Get-VMHostTimeConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting time configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $timeConfig = [ordered]@{}
        $ntpServers = Get-VMHostNtpServer -VMHost $VMHost -ErrorAction SilentlyContinue
        $timeConfig["NTPServers"] = @($ntpServers)

        $timeService = Get-VMHostService -VMHost $VMHost | Where-Object { $_.Key -eq "ntpd" }
        if ($timeService) {
            $timeConfig["NTPConfig"] = [ordered]@{
                Policy = $timeService.Policy
                Running = $timeService.Running
            }
        }

        return $timeConfig
    }
    catch {
        Write-Log "Error collecting time configuration: $($_)" -Level WARNING
        return $null
    }
}

function Get-VMHostDNSConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting DNS configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $dnsInfo = Get-VMHostNetwork -VMHost $VMHost -ErrorAction Stop
        $dnsConfig = [ordered]@{
            DomainName = $dnsInfo.DomainName
            SearchDomain = $dnsInfo.SearchDomain
            DNSAddress = $dnsInfo.DNSAddress
            DHCPEnabled = $dnsInfo.DHCPEnabled
        }
        return $dnsConfig
    }
    catch {
        Write-Log "Error collecting DNS configuration: $($_)" -Level WARNING
        return $null
    }
}

function Get-VMHostSysLogConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting syslog configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $syslogServers = Get-VMHostSysLogServer -VMHost $VMHost -ErrorAction SilentlyContinue
        
        # Convert syslog servers to a simple array of strings for JSON serialization
        $syslogConfig = @()
        if ($syslogServers) {
            foreach ($server in $syslogServers) {
                if ($server -is [string]) {
                    $syslogConfig += $server
                } elseif ($server.Host -and $server.Port) {
                    # Handle NamedIPEndPoint objects
                    $syslogConfig += "$($server.Host):$($server.Port)"
                } elseif ($server.ToString()) {
                    $syslogConfig += $server.ToString()
                }
            }
        }
        
        Write-Log "Found $($syslogConfig.Count) syslog servers configured" -Level DEBUG
        return $syslogConfig
    }
    catch {
        Write-Log "Error collecting syslog configuration: $($_)" -Level WARNING
        return @()
    }
}

function Get-VMHostPowerConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [VMware.VimAutomation.ViCore.Impl.V1.Inventory.VMHostImpl]$VMHost
    )

    Write-Log "Collecting power configuration for $($VMHost.Name)" -Level DEBUG
    try {
        $powerInfo = $VMHost.ExtensionData.config.PowerSystemInfo
        $powerConfig = [ordered]@{
            CurrentPolicy = $powerInfo.CurrentPolicy.Key
            AvailablePolicies = @($powerInfo.HardwareSupportPackage.SupportedPolicy.ShortName)
        }
        return $powerConfig
    }
    catch {
        Write-Log "Error collecting power configuration: $($_)" -Level WARNING
        return $null
    }
}
#endregion

#region Restore Functions
function Restore-VMHostConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,

        [Parameter(Mandatory = $true)]
        [string]$ConfigFilePath,

        [Parameter(Mandatory = $false)]
        [int]$Timeout = 600,

        [Parameter(Mandatory = $false)]
        [string]$UplinkPortgroupName
    )

    Write-Log "Starting restoration of host configuration for $($Server.Name)" -Level INFO

    # Create a temporary file path for the rollback backup
    $rollbackBackupFile = Join-Path -Path (Split-Path $ConfigFilePath -Parent) -ChildPath ("Rollback_" + (Split-Path $ConfigFilePath -Leaf))

    try {
        # Back up the current configuration before making any changes
        Write-Log "Backing up current host configuration for rollback purposes..." -Level INFO
        $null = Backup-VMHostConfiguration -Server $Server -BackupPath (Split-Path $rollbackBackupFile -Parent)
        #Rename file so it doesnt conflict
        Rename-Item -Path (Join-Path -Path (Split-Path $rollbackBackupFile -Parent) -ChildPath ("VMHost_"+$($Server.Name.Split('.')[0])+"_"+(Get-Date -Format "yyyyMMdd_HHmmss")+".json")) -NewName (Split-Path $rollbackBackupFile -Leaf)

        # Import the configuration from JSON
        Write-Log "Importing configuration from $($ConfigFilePath)" -Level DEBUG
        $hostConfig = Get-Content -Path $ConfigFilePath -Raw | ConvertFrom-Json

        # Validate configuration structure
        if (-not $hostConfig.HostConfig) {
            throw "Invalid backup file format. HostConfig section missing."
        }

        # Restore network configuration in proper order
        Write-Log "Restoring network configuration..." -Level INFO
        Restore-VMHostNetworkConfiguration -Server $Server -NetworkConfig $hostConfig.HostConfig.Network -UplinkPortgroupName $UplinkPortgroupName

        # Restore NTP servers
        if ($hostConfig.HostConfig.TimeConfig.NTPServers) {
            Write-Log "Restoring NTP configuration" -Level INFO
            Restore-VMHostNTPConfiguration -Server $Server -TimeConfig $hostConfig.HostConfig.TimeConfig
        }

        # Restore advanced settings
        if ($hostConfig.HostConfig.AdvancedSettings) {
            Write-Log "Restoring advanced settings" -Level INFO
            Restore-VMHostAdvancedSettings -Server $Server -AdvSettings $hostConfig.HostConfig.AdvancedSettings
        }
        
        # Restore host services
        if ($hostConfig.HostConfig.Services) {
            Write-Log "Restoring service configuration" -Level INFO
            Restore-VMHostServicesConfiguration -Server $Server -Services $hostConfig.HostConfig.Services
        }

        # Restore firewall rules
        if ($hostConfig.HostConfig.Firewall) {
            Write-Log "Restoring firewall configuration" -Level INFO
            Restore-VMHostFirewallConfiguration -Server $Server -Firewall $hostConfig.HostConfig.Firewall
        }

        # Restore power policy
        if ($hostConfig.HostConfig.PowerConfig) {
            Write-Log "Restoring power management configuration" -Level INFO
            Restore-VMHostPowerConfiguration -Server $Server -PowerConfig $hostConfig.HostConfig.PowerConfig
        }

        # Restore syslog configuration
        if ($hostConfig.HostConfig.SyslogConfig) {
            Write-Log "Restoring syslog configuration" -Level INFO
            Restore-VMHostSysLogConfiguration -Server $Server -SyslogConfig $hostConfig.HostConfig.SyslogConfig
        }

        Write-Log "Host configuration successfully restored" -Level SUCCESS
    }
    catch {
        Write-Log "Critical error during restore process: $($_)" -Level ERROR
        Write-Log "Error details: $($_.Exception.Message)" -Level ERROR
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level DEBUG
        Write-Log "Attempting to rollback to the previous configuration..." -Level WARNING
        try {
            Rollback-VMHostConfiguration -Server $Server -ConfigFilePath $rollbackBackupFile -UplinkPortgroupName $UplinkPortgroupName
        }
        catch {
            Write-Log "Rollback failed: $($_)" -Level ERROR
            Write-Log "Manual intervention might be required to restore the host to a consistent state." -Level ERROR
        }
        throw  # Re-throw the original exception to stop further processing
    }
    finally {
        # Clean up the rollback backup file
        if (Test-Path $rollbackBackupFile) {
            try {
                Remove-Item $rollbackBackupFile -Force -ErrorAction Stop
                Write-Log "Successfully removed rollback backup file: $($rollbackBackupFile)" -Level DEBUG
            }
            catch {
                Write-Log "Error removing rollback backup file $($rollbackBackupFile): $($_)" -Level WARNING
            }
        }
    }
}

#region Restore Sub-Configuration Functions
function Restore-VMHostNetworkConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$NetworkConfig,
        [Parameter(Mandatory = $false)]
        [string]$UplinkPortgroupName
    )

    Write-Log "Restoring network configuration..." -Level INFO

    # Restore standard switches first
    if ($NetworkConfig.StandardSwitches) {
        Write-Log "Restoring standard virtual switches" -Level DEBUG
        foreach ($vsSwitch in $NetworkConfig.StandardSwitches) {
            try {
                # IMPORTANT: Skip if this is actually a distributed switch name
                $isDistributedSwitch = $false
                if ($NetworkConfig.DistributedSwitches) {
                    $isDistributedSwitch = $NetworkConfig.DistributedSwitches |
                        Where-Object { $_.Name -eq $($vsSwitch.Name) }
                }

                if ($isDistributedSwitch) {
                    Write-Log "Skipping $($vsSwitch.Name) - this is a distributed switch, not a standard switch" -Level WARNING
                    continue
                }

                $existingSwitch = Get-VirtualSwitch -VMHost $Server -Name $($vsSwitch.Name) -Standard -ErrorAction SilentlyContinue
                if ($existingSwitch) {
                    # Switch exists, compare and update
                    Write-Log "Standard Switch $($vsSwitch.Name) exists.  Comparing configuration..." -Level DEBUG
                    $switchDiff = Compare-Object -ReferenceObject $vsSwitch -DifferenceObject $existingSwitch -Property Name, Mtu, NumPorts, Nic -ExcludeDifferent

                    if ($switchDiff) {
                        Write-Log "Differences found in Standard Switch $($vsSwitch.Name). Applying changes..." -Level INFO
                        try {
                            if ($PSCmdlet.ShouldProcess($existingSwitch.Name, "Set-VirtualSwitch")) {
                                Set-VirtualSwitch -VirtualSwitch $existingSwitch -Mtu $($vsSwitch.Mtu) -NumPorts $($vsSwitch.NumPorts) -ErrorAction Stop
                                Write-Log "Updated Standard Switch $($vsSwitch.Name) MTU to $($vsSwitch.Mtu) and NumPorts to $($vsSwitch.NumPorts)" -Level DEBUG
                            }

                            # Handle NICs (this is more complex and might need further refinement)
                            if ($vsSwitch.Nic) {
                                Write-Log "Attempting to update physical NICs for Standard Switch $($vsSwitch.Name)" -Level DEBUG
                                foreach ($nic in $vsSwitch.Nic) {
                                    $pnic = Get-VMHostNetworkAdapter -VMHost $Server -Name $($nic) -Physical -ErrorAction SilentlyContinue
                                    if ($pnic) {
                                        # Check if the NIC is already connected to the switch
                                        if (-not ($existingSwitch.Nic -contains $nic)) {
                                            try {
                                                if ($PSCmdlet.ShouldProcess("$($pnic.Name) to $($existingSwitch.Name)", "Add-VirtualSwitchPhysicalNetworkAdapter")) {
                                                    Add-VirtualSwitchPhysicalNetworkAdapter -VirtualSwitch $existingSwitch -VMHostPhysicalNic $pnic -Confirm:$false -ErrorAction Stop
                                                    Write-Log "Added physical NIC $($nic) to switch $($vsSwitch.Name)" -Level DEBUG
                                                }
                                            }
                                            catch {
                                                Write-Log "Failed to add physical NIC $($nic) to switch $($vsSwitch.Name): $($_)" -Level WARNING
                                            }
                                        } else {
                                            Write-Log "Physical NIC $($nic) is already connected to Standard Switch $($vsSwitch.Name)" -Level DEBUG
                                        }
                                    } else {
                                        Write-Log "Physical NIC $($nic) not found - skipping assignment to switch $($vsSwitch.Name)" -Level WARNING
                                    }
                                }
                            }
                        }
                        catch {
                            Write-Log "Error updating Standard Switch $($vsSwitch.Name): $($_)" -Level WARNING
                        }
                    } else {
                        Write-Log "Standard Switch $($vsSwitch.Name) is already configured as desired.  Skipping." -Level DEBUG
                    }
                } else {
                    Write-Log "Creating standard virtual switch $($vsSwitch.Name)" -Level DEBUG
                    try {
                        if ($PSCmdlet.ShouldProcess($vsSwitch.Name, "New-VirtualSwitch")) {
                            $newSwitch = New-VirtualSwitch -VMHost $Server -Name $($vsSwitch.Name) -Mtu $($vsSwitch.Mtu) -NumPorts $($vsSwitch.NumPorts) -ErrorAction Stop

                            # Add physical NICs if specified
                            if ($vsSwitch.Nic) {
                                foreach ($nic in $vsSwitch.Nic) {
                                    try {
                                        $pnic = Get-VMHostNetworkAdapter -VMHost $Server -Name $($nic) -Physical -ErrorAction SilentlyContinue
                                        if ($pnic) {
                                            if ($PSCmdlet.ShouldProcess("$($pnic.Name) to $($newSwitch.Name)", "Add-VirtualSwitchPhysicalNetworkAdapter")) {
                                                Add-VirtualSwitchPhysicalNetworkAdapter -VirtualSwitch $newSwitch -VMHostPhysicalNic $pnic -Confirm:$false -ErrorAction Stop
                                                Write-Log "Added physical NIC $($nic) to switch $($vsSwitch.Name)" -Level DEBUG
                                            }
                                        } else {
                                            Write-Log "Physical NIC $($nic) not found - skipping assignment to switch $($vsSwitch.Name)" -Level WARNING
                                        }
                                    }
                                    catch {
                                        Write-Log "Failed to add physical NIC $($nic) to switch $($vsSwitch.Name): $($_)" -Level WARNING
                                    }
                                }
                            }
                        }
                    }
                    catch {
                        Write-Log "Error creating standard switch $($vsSwitch.Name): $($_)" -Level WARNING
                    }
                }

                # Restore port groups for this switch
                if ($vsSwitch.PortGroups) {
                    foreach ($pg in $vsSwitch.PortGroups) {
                        try {
                            $existingPg = Get-VirtualPortGroup -VirtualSwitch $existingSwitch -Name $($pg.Name) -ErrorAction SilentlyContinue
                            if ($existingPg) {
                                # Portgroup exists, compare and update
                                Write-Log "Port Group $($pg.Name) exists on Switch $($vsSwitch.Name). Comparing configuration..." -Level DEBUG
                                $pgDiff = Compare-Object -ReferenceObject $pg -DifferenceObject $existingPg -Property Name, VLanId, SecurityPolicy -ExcludeDifferent

                                if ($pgDiff) {
                                    Write-Log "Differences found in Port Group $($pg.Name). Applying changes..." -Level INFO
                                    try {
                                        if ($PSCmdlet.ShouldProcess($existingPg.Name, "Set-VirtualPortGroup")) {
                                            # Update the port group's VLAN ID
                                            Set-VirtualPortGroup -VirtualPortGroup $existingPg -VLanId $($pg.VLanId)
                                            Write-Log "Updated VLAN ID for Port Group $($pg.Name) to $($pg.VLanId)" -Level DEBUG
                                        }

                                        # Update the security policy (this is more complex)
                                        if ($pg.SecurityPolicy) {
                                            $spec = New-Object VMware.Vim.HostPortGroupSpec
                                            $spec.Name = $existingPg.Name
                                            $spec.VswitchName = $existingSwitch.Name
                                            $spec.VlanId = $existingPg.VLanId
                                            $spec.Policy = New-Object VMware.Vim.HostNetworkPolicy
                                            $spec.Policy.Security = New-Object VMware.Vim.HostNetworkSecurityPolicy
                                            $spec.Policy.Security.AllowPromiscuous = [bool]$pg.SecurityPolicy.AllowPromiscuous
                                            $spec.Policy.Security.MacChanges = [bool]$pg.SecurityPolicy.MacChanges
                                            $spec.Policy.Security.ForgedTransmits = [bool]$pg.SecurityPolicy.ForgedTransmits

                                            $hostNetworkSystem = $Server.ExtensionData.ConfigManager.NetworkSystem
                                            if ($PSCmdlet.ShouldProcess($existingPg.Name, "Update PortGroup Security Policy")) {
                                                $hostNetworkSystem.UpdatePortGroup($($existingPg.Name), $spec)
                                                Write-Log "Updated security policy for Port Group $($pg.Name)" -Level DEBUG
                                            }
                                        }
                                    }
                                    catch {
                                        Write-Log "Error updating Port Group $($pg.Name): $($_)" -Level WARNING
                                    }
                                } else {
                                    Write-Log "Port Group $($pg.Name) is already configured as desired. Skipping." -Level DEBUG
                                }
                            } else {
                                Write-Log "Creating port group $($pg.Name) on switch $($vsSwitch.Name)" -Level DEBUG
                                try {
                                    if ($PSCmdlet.ShouldProcess("$($pg.Name) on $($vsSwitch.Name)", "New-VirtualPortGroup")) {
                                        $vs = Get-VirtualSwitch -VMHost $Server -Name $($vsSwitch.Name) -Standard -ErrorAction Stop
                                        $newPg = New-VirtualPortGroup -VirtualSwitch $vs -Name $($pg.Name) -VLanId $($pg.VLanId) -ErrorAction Stop

                                        # Apply security policy if specified
                                        if ($pg.SecurityPolicy) {
                                            $spec = New-Object VMware.Vim.HostPortGroupSpec
                                            $spec.Name = $newPg.Name
                                            $spec.VswitchName = $vs.Name
                                            $spec.VlanId = $newPg.VLanId
                                            $spec.Policy = New-Object VMware.Vim.HostNetworkPolicy
                                            $spec.Policy.Security = New-Object VMware.Vim.HostNetworkSecurityPolicy
                                            $spec.Policy.Security.AllowPromiscuous = [bool]$pg.SecurityPolicy.AllowPromiscuous
                                            $spec.Policy.Security.MacChanges = [bool]$pg.SecurityPolicy.MacChanges
                                            $spec.Policy.Security.ForgedTransmits = [bool]$pg.SecurityPolicy.ForgedTransmits

                                            $hostNetworkSystem = $Server.ExtensionData.ConfigManager.NetworkSystem
                                            if ($PSCmdlet.ShouldProcess($newPg.Name, "Update PortGroup Security Policy")) {
                                                $hostNetworkSystem.UpdatePortGroup($($newPg.Name), $spec)
                                            }
                                        }
                                    }
                                }
                                catch {
                                    Write-Log "Error creating port group $($pg.Name): $($_)" -Level WARNING
                                }
                            }
                        }
                        catch {
                            Write-Log "Error restoring port group $($pg.Name): $($_)" -Level WARNING
                        }
                    }
                }
            }
            catch {
                Write-Log "Error restoring standard switch $($vsSwitch.Name): $($_)" -Level ERROR
            }
        }
    }

    # Restore VMkernel adapters on standard switches
    if ($NetworkConfig.VMKernelAdapters) {
        Write-Log "Restoring VMkernel adapters" -Level DEBUG
        foreach ($vmk in $NetworkConfig.VMKernelAdapters) {
            try {
                # Skip adapters on distributed switches (handled later)
                if ($vmk.DistributedSwitch) { continue }

                $existingVmk = Get-VMHostNetworkAdapter -VMHost $Server -Name $($vmk.Name) -ErrorAction SilentlyContinue
                if ($existingVmk) {
                    Write-Log "VMkernel adapter $($vmk.Name) exists. Comparing configuration..." -Level DEBUG

                    # Compare relevant properties
                    $vmkDiff = Compare-Object -ReferenceObject $vmk -DifferenceObject $existingVmk -Property Name, IP, SubnetMask, Mtu, PortGroupName -ExcludeDifferent

                    if ($vmkDiff) {
                        Write-Log "Differences found in VMkernel adapter $($vmk.Name). Applying changes..." -Level INFO
                        try {
                            # Get the portgroup object
                            $pg = Get-VirtualPortGroup -VMHost $Server -Name $($vmk.PortGroupName) -ErrorAction Stop

                            $params = @{
                                VMHost = $Server
                                VMKernelNetworkAdapter = $existingVmk
                                PortGroup = $pg
                                IP = $vmk.IP
                                SubnetMask = $vmk.SubnetMask
                                Mtu = $vmk.Mtu
                                Confirm = $false
                            }

                            # Set traffic types
                            if ($vmk.ManagementTrafficEnabled) { $params["ManagementTraffic"] = $true }
                            if ($vmk.VMotionEnabled) { $params["VMotionEnabled"] = $true }
                            if ($vmk.VsanTrafficEnabled) { $params["VsanTrafficEnabled"] = $true }
                            if ($vmk.FaultToleranceLoggingEnabled) { $params["FaultToleranceLoggingEnabled"] = $true }
                            if ($PSCmdlet.ShouldProcess($vmk.Name, "Set-VMHostNetworkAdapter")) {
                                Set-VMHostNetworkAdapter @params -ErrorAction Stop | Out-Null
                                Write-Log "Updated VMkernel adapter $($vmk.Name)" -Level DEBUG
                            }
                        }
                        catch {
                            Write-Log "Error updating VMkernel adapter $($vmk.Name): $($_)" -Level WARNING
                        }
                    } else {
                        Write-Log "VMkernel adapter $($vmk.Name) is already configured as desired. Skipping." -Level DEBUG
                    }
                } else {
                    Write-Log "Creating VMkernel adapter $($vmk.Name)" -Level DEBUG
                    try {
                        $pg = Get-VirtualPortGroup -VMHost $Server -Name $($vmk.PortGroupName) -ErrorAction Stop

                        $params = @{
                            VMHost = $Server
                            PortGroup = $pg
                            IP = $vmk.IP
                            SubnetMask = $vmk.SubnetMask
                            Mtu = $vmk.Mtu
                            Confirm = $false
                        }

                        # Set traffic types
                        if ($vmk.ManagementTrafficEnabled) { $params["ManagementTraffic"] = $true }
                        if ($vmk.VMotionEnabled) { $params["VMotionEnabled"] = $true }
                        if ($vmk.VsanTrafficEnabled) { $params["VsanTrafficEnabled"] = $true }
                        if ($vmk.FaultToleranceLoggingEnabled) { $params["FaultToleranceLoggingEnabled"] = $true }
                        if ($PSCmdlet.ShouldProcess($vmk.Name, "New-VMHostNetworkAdapter")) {
                            New-VMHostNetworkAdapter @params -ErrorAction Stop | Out-Null
                            Write-Log "Created VMkernel adapter $($vmk.Name)" -Level DEBUG
                        }
                    }
                    catch {
                        Write-Log "Error creating VMkernel adapter $($vmk.Name): $($_)" -Level WARNING
                    }
                }
            }
            catch {
                Write-Log "Error restoring VMkernel adapter: $($_)" -Level WARNING
            }
        }

        # Enhanced Distributed Switch restoration with correct host detection
        if ($NetworkConfig.DistributedSwitches) {
            Write-Log "Restoring distributed switches" -Level INFO

            # First, clean up any standard switches with VDS names
            foreach ($vdsConfig in $NetworkConfig.DistributedSwitches) {
                $incorrectSwitch = Get-VirtualSwitch -VMHost $Server -Name $($vdsConfig.Name) -Standard -ErrorAction SilentlyContinue
                if ($incorrectSwitch) {
                    Write-Log "Removing incorrectly created standard switch $($vdsConfig.Name)" -Level WARNING
                    if ($PSCmdlet.ShouldProcess($incorrectSwitch.Name, "Remove-VirtualSwitch")) {
                        Remove-VirtualSwitch -VirtualSwitch $incorrectSwitch -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
            }

            foreach ($vdsConfig in $NetworkConfig.DistributedSwitches) {
                try {
                    Write-Log "Processing distributed switch $($vdsConfig.Name)" -Level DEBUG
                    $vds = Get-VDSwitch -Name $($vdsConfig.Name) -ErrorAction SilentlyContinue | Select-Object -First 1

                    if (-not $vds) {
                        Write-Log "WARNING: Distributed Switch '$($vdsConfig.Name)' not found in target vCenter. Skipping." -Level WARNING
                        continue
                    }

                    # Create port groups if they don't exist
                    foreach ($pg in $vdsConfig.PortGroups) {
                        try {
                            $existingPG = Get-VDPortgroup -Name $($pg.Name) -VDSwitch $vds -ErrorAction SilentlyContinue
                            if (-not $existingPG) {
                                Write-Log "Creating port group '$($pg.Name)' on Distributed Switch '$($vdsConfig.Name)'" -Level DEBUG
                                if ($PSCmdlet.ShouldProcess("$($pg.Name) on $($vdsConfig.Name)", "New-VDPortgroup")) {
                                    $vds | New-VDPortgroup -Name $($pg.Name) -VlanId $($pg.VLANId) -ErrorAction Stop | Out-Null
                                }
                            }
                        }
                        catch {
                            Write-Log "Error creating port group '$($pg.Name)': $($_)" -Level WARNING
                        }
                    }

                    # Check if host is already a member using correct method
                    $hostAdded = $false
                    try {
                        # Get all hosts connected to this distributed switch
                        $vdsHosts = $vds | Get-VMHost -ErrorAction SilentlyContinue
                        $hostIsMember = $vdsHosts | Where-Object { $_.Name -eq $($Server.Name) }

                        # Debug information
                        Write-Log "Debug - VDSwitch: $($vds.Name), Host: $($Server.Name)" -Level DEBUG
                        Write-Log "Debug - Host is member: $($hostIsMember -ne $null)" -Level DEBUG

                        if ($hostIsMember) {
                            Write-Log "Host is already a member of distributed switch '$($vdsConfig.Name)'" -Level DEBUG
                            $hostAdded = $true
                        } else {
                            Write-Log "Adding host to distributed switch '$($vdsConfig.Name)'" -Level DEBUG

                            # More explicit parameter specification
                            $addVDSwitchParams = @{
                                VDSwitch = $vds
                                VMHost = $Server
                                ErrorAction = "Stop"
                                Confirm = $false
                            }

                            if ($PSCmdlet.ShouldProcess("$($Server.Name) to $($vds.Name)", "Add-VDSwitchVMHost")) {
                                Add-VDSwitchVMHost @addVDSwitchParams | Out-Null
                                $hostAdded = $true
                                Write-Log "Successfully added host to distributed switch '$($vdsConfig.Name)'" -Level SUCCESS
                            }
                        }
                    }
                    catch {
                        # If the error is "already added", that's actually success
                        if ($_.Exception.Message -like "*already added*" -or
                            $_.Exception.Message -like "*already exists*") {
                            Write-Log "Host was already added to distributed switch '$($vdsConfig.Name)'" -Level DEBUG
                            $hostAdded = $true
                        } else {
                            Write-Log "Error adding host to distributed switch '$($vdsConfig.Name)': $($_)" -Level WARNING
                            Write-Log "Error details: $($_.Exception.Message)" -Level DEBUG
                            # Still try to configure uplinks in case the host is actually there
                            $hostAdded = $true
                        }
                    }

                    # Configure physical NIC uplinks (this should always run if host is a member)
                    if ($hostAdded -and $vdsConfig.Uplinks) {
                        Write-Log "Configuring physical NIC uplinks for distributed switch $($vdsConfig.Name)" -Level INFO

                        try {
                            # Refresh the VDSwitch object
                            $vds = Get-VDSwitch -Name $($vdsConfig.Name) -ErrorAction Stop

                            # Get all available physical NICs on the host for reference
                            $allPhysicalNics = Get-VMHostNetworkAdapter -VMHost $Server -Physical -ErrorAction SilentlyContinue
                            Write-Log "Found $($allPhysicalNics.Count) physical NICs on host $($Server.Name)" -Level DEBUG

                            # List all physical NICs for debugging
                            foreach ($nic in $allPhysicalNics) {
                                Write-Log "Available physical NIC: Name=$($nic.Name), MAC=$($nic.Mac), Device=$($nic.DeviceName)" -Level DEBUG
                            }

                            # Process each uplink
                            Write-Log "Processing $($vdsConfig.Uplinks.Count) uplinks for distributed switch $($vdsConfig.Name)" -Level DEBUG
                            foreach ($uplink in $vdsConfig.Uplinks) {
                                try {
                                    Write-Log "Processing uplink assignment: $($uplink.PhysicalNic) -> $($uplink.UplinkName)" -Level INFO

                                    # Find the physical NIC using multiple methods
                                    $pnic = $null

                                    # Method 1: By exact name
                                    $pnic = $allPhysicalNics | Where-Object { $_.Name -eq $($uplink.PhysicalNic) }
                                    if ($pnic) {
                                        Write-Log "Found NIC by exact name: $($uplink.PhysicalNic)" -Level DEBUG
                                    }

                                    # Method 2: By MAC address if name doesn't work
                                    if (-not $pnic -and $uplink.MacAddress) {
                                        Write-Log "Searching for NIC by MAC address: $($uplink.MacAddress)" -Level DEBUG
                                        $pnic = $allPhysicalNics | Where-Object { $_.Mac -eq $($uplink.MacAddress) }
                                        if ($pnic) {
                                            Write-Log "Found NIC by MAC address: $($uplink.MacAddress)" -Level DEBUG
                                        }
                                    }

                                    # Method 3: By PCI if available
                                    if (-not $pnic -and $uplink.PCI) {
                                        Write-Log "Searching for NIC by PCI: $($uplink.PCI)" -Level DEBUG
                                        $pnic = $allPhysicalNics | Where-Object { $_.ExtensionData.Pci -eq $($uplink.PCI) }
                                        if ($pnic) {
                                            Write-Log "Found NIC by PCI: $($uplink.PCI)" -Level DEBUG
                                        }
                                    }

                                    # Method 4: Try to match by device name pattern
                                    if (-not $pnic) {
                                        # Extract device number if possible (e.g., vmnic0, vmnic1)
                                        if ($uplink.PhysicalNic -match 'vmnic(\d+)') {
                                            $nicNumber = $matches[1]
                                            Write-Log "Trying to find NIC by device number: vmnic$($nicNumber)" -Level DEBUG
                                            $pnic = $allPhysicalNics | Where-Object { $_.Name -eq "vmnic$($nicNumber)" -or $_.DeviceName -eq "vmnic$($nicNumber)" }
                                            if ($pnic) {
                                                Write-Log "Found NIC by device number: vmnic$($nicNumber)" -Level DEBUG
                                            }
                                        }
                                    }

                                    if ($pnic) {
                                        Write-Log "Found physical NIC: $($pnic.Name) (MAC: $($pnic.Mac))" -Level INFO

                                        # Check if NIC is already assigned to this switch
                                        $nicCurrentSwitch = $pnic.ExtensionData.Spec.DistributedVirtualPort
                                        $isAlreadyAssigned = $false

                                        if ($nicCurrentSwitch -and $nicCurrentSwitch.SwitchUuid -eq $vds.ExtensionData.Uuid) {
                                            Write-Log "$($pnic.Name) is already assigned to distributed switch $($vds.Name)" -Level INFO
                                            $isAlreadyAssigned = $true
                                        }

                                        if (-not $isAlreadyAssigned) {
                                            # Try to add the physical NIC to the distributed switch
                                            try {
                                                Write-Log "Adding $($pnic.Name) to distributed switch $($vds.Name)" -Level INFO

                                                # Get the uplink portgroup
                                                $uplinkPortgroup = Find-VDSwitchUplinkPortgroup -VDSwitch $vds -UplinkPortgroupName $UplinkPortgroupName

                                                if ($uplinkPortgroup) {
                                                    Write-Log "Using uplink portgroup: $($uplinkPortgroup.Name)" -Level DEBUG

                                                    # Try multiple methods to add the uplink

                                                    # Method 1: Using Add-VDSwitchPhysicalNetworkAdapter
                                                    try {
                                                        if ($PSCmdlet.ShouldProcess("$($pnic.Name) to $($vds.Name)", "Add-VDSwitchPhysicalNetworkAdapter")) {
                                                            Add-VDSwitchPhysicalNetworkAdapter -DistributedSwitch $vds -VMHostPhysicalNic $pnic -Confirm:$false -ErrorAction Stop
                                                            Write-Log "Successfully added $($pnic.Name) to distributed switch $($vds.Name) using method 1" -Level SUCCESS
                                                        }
                                                    }
                                                    catch {
                                                        Write-Log "Method 1 failed: $($_)" -Level WARNING

                                                        # Method 2: Using Add-VDSwitchPhysicalNetworkAdapter with portgroup
                                                        try {
                                                            if ($PSCmdlet.ShouldProcess("$($pnic.Name) to $($vds.Name)", "Add-VDSwitchPhysicalNetworkAdapter")) {
                                                                Add-VDSwitchPhysicalNetworkAdapter -DistributedSwitch $vds -VMHostPhysicalNic $pnic -VMHostVirtualNic $null -VirtualNicPortgroup $uplinkPortgroup -Confirm:$false -ErrorAction Stop
                                                                Write-Log "Successfully added $($pnic.Name) to distributed switch $($vds.Name) using method 2" -Level SUCCESS
                                                            }
                                                        }
                                                        catch {
                                                            Write-Log "Method 2 failed: $($_)" -Level WARNING

                                                            # Method 3: Using direct ESXi host API if needed
                                                            try {
                                                                $hostNetworkSystem = Get-View -Id $($Server.ExtensionData.ConfigManager.NetworkSystem)
                                                                $dvPort = New-Object VMware.Vim.DistributedVirtualSwitchPortConnection
                                                                $dvPort.SwitchUuid = $vds.ExtensionData.Uuid
                                                                $dvPort.PortgroupKey = $uplinkPortgroup.ExtensionData.Key

                                                                if ($PSCmdlet.ShouldProcess("$($pnic.Name) to $($vds.Name)", "UpdateNetworkConfig")) {
                                                                    $hostNetworkSystem.UpdatePhysicalNicLinkSpeed($pnic.DeviceName, $null)
                                                                    $hostNetworkSystem.UpdateNetworkConfig($null, $dvPort, $pnic.DeviceName)
                                                                    Write-Log "Successfully added $($pnic.Name) to distributed switch $($vds.Name) using method 3" -Level SUCCESS
                                                                }
                                                            }
                                                            catch {
                                                                Write-Log "Method 3 failed: $($_)" -Level ERROR
                                                                Write-Log "All methods to add physical NIC to distributed switch failed" -Level ERROR
                                                            }
                                                        }
                                                    }
                                                }
                                                else {
                                                    Write-Log "No uplink portgroup found on $($vds.Name), cannot add physical NIC" -Level ERROR
                                                }
                                            }
                                            catch {
                                                if ($_.Exception.Message -like "*already assigned*" -or $_.Exception.Message -like "*already connected*") {
                                                    Write-Log "$($pnic.Name) is already assigned to a distributed switch - this is expected" -Level INFO
                                                } else {
                                                    Write-Log "Error adding $($pnic.Name) to distributed switch: $($_)" -Level ERROR
                                                }
                                            }
                                        } else {
                                            Write-Log "$($pnic.Name) already correctly assigned to $($vds.Name)" -Level SUCCESS
                                        }
                                    } else {
                                        Write-Log "Physical NIC not found for uplink $($uplink.UplinkName) (Name: $($uplink.PhysicalNic), MAC: $($uplink.MacAddress))" -Level ERROR

                                        # List available physical NICs for troubleshooting
                                        Write-Log "Available physical NICs:" -Level INFO
                                        foreach ($nic in $allPhysicalNics) {
                                            Write-Log "  - $($nic.Name): MAC=$($nic.Mac), Device=$($nic.DeviceName)" -Level INFO
                                        }
                                    }
                                }
                                catch {
                                    Write-Log "Error processing uplink $($uplink.UplinkName): $($_)" -Level ERROR
                                }
                            }

                            # Verify uplink assignments after configuration
                            Write-Log "Verifying uplink assignments for $($vdsConfig.Name)" -Level INFO
                            $vdsPhysicalNics = Get-VMHostNetworkAdapter -VMHost $Server -Physical |
                                Where-Object { $_.ExtensionData.Spec.DistributedVirtualPort.SwitchUuid -eq $vds.ExtensionData.Uuid }

                            if ($vdsPhysicalNics) {
                                Write-Log "Physical NICs assigned to $($vdsConfig.Name):" -Level SUCCESS
                                foreach ($nic in $vdsPhysicalNics) {
                                    Write-Log "  - $($nic.Name) (MAC: $($nic.Mac))" -Level SUCCESS
                                }
                            } else {
                                Write-Log "WARNING: No physical NICs appear to be assigned to $($vdsConfig.Name)" -Level WARNING
                            }
                        }
                        catch {
                            Write-Log "Error configuring distributed switch uplinks: $($_)" -Level ERROR
                        }
                    } elseif (-not $hostAdded) {
                        Write-Log "Skipping uplink configuration - host not properly added to distributed switch $($vdsConfig.Name)" -Level WARNING
                    } elseif (-not $vdsConfig.Uplinks) {
                        Write-Log "No uplink configuration found for distributed switch $($vdsConfig.Name)" -Level WARNING
                    }

                    # Recreate VMkernel adapters on Distributed Switch port groups
                    if ($hostAdded -and $vdsConfig.VMkernelAdapters) {
                        foreach ($vmk in $vdsConfig.VMkernelAdapters) {
                            try {
                                $existingAdapter = Get-VMHostNetworkAdapter -VMHost $Server -Name $($vmk.Name) -ErrorAction SilentlyContinue
                                if (-not $existingAdapter) {
                                    Write-Log "Creating VMkernel adapter '$($vmk.Name)' on port group '$($vmk.PortGroupName)'" -Level DEBUG

                                    $params = @{
                                        VMHost = $Server
                                        PortGroup = $($vmk.PortGroupName)
                                        IP = $vmk.IP
                                        SubnetMask = $vmk.SubnetMask
                                        Mtu = $vmk.Mtu
                                        Confirm = $false
                                    }

                                    # Set traffic types
                                    if ($vmk.TrafficTypes -contains "Management") { $params["ManagementTraffic"] = $true }
                                    if ($vmk.TrafficTypes -contains "VMotion") { $params["VMotionEnabled"] = $true }
                                    if ($vmk.TrafficTypes -contains "VSAN") { $params["VsanTrafficEnabled"] = $true }
                                    if ($vmk.FaultToleranceLoggingEnabled) { $params["FaultToleranceLoggingEnabled"] = $true }
                                    if ($PSCmdlet.ShouldProcess($vmk.Name, "New-VMHostNetworkAdapter")) {
                                        New-VMHostNetworkAdapter @params -ErrorAction Stop | Out-Null
                                    }
                                }
                            }
                            catch {
                                Write-Log "Error restoring VMkernel adapter '$($vmk.Name)': $($_)" -Level WARNING
                            }
                        }
                    }
                }
                catch {
                    Write-Log "Error processing distributed switch '$($vdsConfig.Name)': $($_)" -Level ERROR
                }
            }
        }
    }
}

function Restore-VMHostNTPConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$TimeConfig
    )
    Write-Log "Restoring NTP configuration" -Level INFO
    try {
        # Remove existing NTP servers first
        if ($PSCmdlet.ShouldProcess($Server.Name, "Remove-VMHostNtpServer")) {
            Remove-VMHostNtpServer -VMHost $Server -NtpServer (Get-VMHostNtpServer -VMHost $Server) -Confirm:$false -ErrorAction SilentlyContinue
        }

        # Add configured NTP servers
        foreach ($ntp in $TimeConfig.NTPServers) {
            try {
                if ($PSCmdlet.ShouldProcess($ntp, "Add-VMHostNtpServer")) {
                    Add-VMHostNtpServer -VMHost $Server -NtpServer $ntp -ErrorAction Stop
                }
            }
            catch {
                Write-Log "Error adding NTP server $($ntp): $($_)" -Level WARNING
            }
        }

        # Configure NTP service
        $ntpService = Get-VMHostService -VMHost $Server | Where-Object { $_.Key -eq "ntpd" }
        if ($ntpService) {
            if ($TimeConfig.NTPConfig.Policy) {
                if ($PSCmdlet.ShouldProcess($ntpService.Key, "Set-VMHostService -Policy")) {
                    Set-VMHostService -HostService $ntpService -Policy $($TimeConfig.NTPConfig.Policy) -Confirm:$false -ErrorAction Stop
                }
            }

            if ($TimeConfig.NTPConfig.Running) {
                if ($PSCmdlet.ShouldProcess($ntpService.Key, "Start-VMHostService")) {
                    Start-VMHostService -HostService $ntpService -Confirm:$false -ErrorAction Stop
                }
            }
        }
    }
    catch {
        Write-Log "Error configuring NTP: $($_)" -Level ERROR
    }
}

function Restore-VMHostAdvancedSettings {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$AdvSettings
    )

    Write-Log "Restoring advanced settings" -Level INFO
    foreach ($setting in $AdvSettings) {
        try {
            $advSetting = Get-AdvancedSetting -Entity $Server -Name $($setting.Name) -ErrorAction SilentlyContinue
            if ($advSetting) {
                if ($advSetting.Value -ne $setting.Value) {
                    Write-Log "Setting advanced setting $($setting.Name) to $($setting.Value)" -Level DEBUG
                    if ($PSCmdlet.ShouldProcess($setting.Name, "Set-AdvancedSetting")) {
                        Set-AdvancedSetting -AdvancedSetting $advSetting -Value $($setting.Value) -Confirm:$false -ErrorAction Stop
                    }
                }
            }
        }
        catch {
            Write-Log "Error setting advanced setting $($setting.Name): $($_)" -Level WARNING
        }
    }
}

function Restore-VMHostServicesConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$Services
    )
    Write-Log "Restoring service configuration" -Level INFO
    foreach ($service in $Services) {
        try {
            $vmHostService = Get-VMHostService -VMHost $Server | Where-Object { $_.Key -eq $($service.Key) }
            if ($vmHostService) {
                # Only change if different
                if ($vmHostService.Policy -ne $service.Policy) {
                    Write-Log "Setting service $($service.Key) policy to $($service.Policy)" -Level DEBUG
                    if ($PSCmdlet.ShouldProcess($service.Key, "Set-VMHostService -Policy")) {
                        Set-VMHostService -HostService $vmHostService -Policy $($service.Policy) -Confirm:$false -ErrorAction Stop
                    }
                }

                # Only change running state if needed
                if ($service.Running -and -not $vmHostService.Running) {
                    Write-Log "Starting service $($service.Key)" -Level DEBUG
                    if ($PSCmdlet.ShouldProcess($service.Key, "Start-VMHostService")) {
                        Start-VMHostService -HostService $vmHostService -Confirm:$false -ErrorAction Stop
                    }
                }
                elseif (-not $service.Running -and $vmHostService.Running) {
                    Write-Log "Stopping service $($service.Key)" -Level DEBUG
                    if ($PSCmdlet.ShouldProcess($service.Key, "Stop-VMHostService")) {
                        Stop-VMHostService -HostService $vmHostService -Confirm:$false -ErrorAction Stop
                    }
                }
            }
        }
        catch {
            Write-Log "Error configuring service $($service.Key): $($_)" -Level WARNING
        }
    }
}

function Restore-VMHostFirewallConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$Firewall
    )
    Write-Log "Restoring firewall configuration" -Level INFO
    foreach ($rule in $Firewall) {
        try {
            $fwRule = Get-VMHostFirewallException -VMHost $Server -Name $($rule.Name) -ErrorAction SilentlyContinue
            if ($fwRule) {
                if ($fwRule.Enabled -ne $rule.Enabled) {
                    Write-Log "Setting firewall rule $($rule.Name) enabled: $($rule.Enabled)" -Level DEBUG
                    if ($PSCmdlet.ShouldProcess($rule.Name, "Set-VMHostFirewallException")) {
                        Set-VMHostFirewallException -Exception $fwRule -Enabled $($rule.Enabled) -Confirm:$false -ErrorAction Stop
                    }
                }
            }
        }
        catch {
            Write-Log "Error configuring firewall rule $($rule.Name): $($_)" -Level WARNING
        }
    }
}

function Restore-VMHostPowerConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$PowerConfig
    )
    Write-Log "Restoring power management configuration" -Level INFO
    try {
        $currentPolicy = $Server.ExtensionData.config.PowerSystemInfo.CurrentPolicy.Key
        $desiredPolicy = $PowerConfig.CurrentPolicy

        if ($currentPolicy -ne $desiredPolicy) {
            Write-Log "Setting power policy to $($desiredPolicy)" -Level DEBUG
            if ($PSCmdlet.ShouldProcess($Server.Name, "ConfigurePowerPolicy")) {
                $vmHostView = $Server | Get-View
                $spec = New-Object VMware.Vim.HostPowerSpec
                $spec.PowerPolicy = $desiredPolicy
                $vmHostView.ConfigurePowerPolicy($spec)
            }
        }
    }
    catch {
        Write-Log "Error setting power policy: $($_)" -Level ERROR
    }
}

function Restore-VMHostSysLogConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,
        [Parameter(Mandatory = $true)]
        [object]$SyslogConfig
    )

    Write-Log "Restoring syslog configuration" -Level INFO
    try {
        # Clear existing syslog servers first
        Write-Log "Clearing existing syslog servers" -Level DEBUG
        if ($PSCmdlet.ShouldProcess($Server.Name, "Clear-VMHostSysLogServer")) {
            try {
                # Get current syslog servers and remove them
                $currentServers = Get-VMHostSysLogServer -VMHost $Server -ErrorAction SilentlyContinue
                if ($currentServers) {
                    Set-VMHostSysLogServer -VMHost $Server -SysLogServer $null -ErrorAction SilentlyContinue
                }
            }
            catch {
                Write-Log "Note: Could not clear existing syslog servers (this may be normal): $($_)" -Level DEBUG
            }
        }

        # Convert SyslogConfig to appropriate format and set new servers
        if ($SyslogConfig -and $SyslogConfig.Count -gt 0) {
            # Handle different input formats
            $syslogServers = @()
            
            foreach ($server in $SyslogConfig) {
                if ($server -is [string]) {
                    $syslogServers += $server
                } elseif ($server.Host -and $server.Port) {
                    # Handle object with Host and Port properties
                    $syslogServers += "$($server.Host):$($server.Port)"
                } elseif ($server.ToString()) {
                    $syslogServers += $server.ToString()
                }
            }
            
            if ($syslogServers.Count -gt 0) {
                Write-Log "Setting syslog servers: $($syslogServers -join ', ')" -Level DEBUG
                if ($PSCmdlet.ShouldProcess($Server.Name, "Set-VMHostSysLogServer")) {
                    Set-VMHostSysLogServer -VMHost $Server -SysLogServer $syslogServers -ErrorAction Stop
                    Write-Log "Successfully configured $($syslogServers.Count) syslog servers" -Level SUCCESS
                }
            } else {
                Write-Log "No valid syslog servers found in configuration" -Level WARNING
            }
        } else {
            Write-Log "No syslog servers to configure" -Level INFO
        }
    }
    catch {
        Write-Log "Error setting syslog server: $($_)" -Level ERROR
        Write-Log "Syslog configuration details: $($SyslogConfig | ConvertTo-Json -Depth 2)" -Level DEBUG
    }
}
#endregion

#region Rollback Function
function Rollback-VMHostConfiguration {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [object]$Server,

        [Parameter(Mandatory = $true)]
        [string]$ConfigFilePath,

        [Parameter(Mandatory = $false)]
        [string]$UplinkPortgroupName
    )

    Write-Log "Starting rollback of host configuration for $($Server.Name)" -Level WARNING

    try {
        # Import the rollback configuration from JSON
        Write-Log "Importing rollback configuration from $($ConfigFilePath)" -Level DEBUG
        $hostConfig = Get-Content -Path $ConfigFilePath -Raw | ConvertFrom-Json

        # Validate configuration structure
        if (-not $hostConfig.HostConfig) {
            throw "Invalid rollback file format. HostConfig section missing."
        }

        # Restore network configuration
        Write-Log "Rolling back network configuration..." -Level INFO
        Restore-VMHostNetworkConfiguration -Server $Server -NetworkConfig $hostConfig.HostConfig.Network -UplinkPortgroupName $UplinkPortgroupName

        Write-Log "Host configuration rollback completed" -Level SUCCESS
    }
    catch {
        Write-Log "Critical error during rollback process: $($_)" -Level ERROR
        Write-Log "Error details: $($_.Exception.Message)" -Level ERROR
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level DEBUG
        throw "Rollback failed: $($_)"
    }
}
#endregion

#region Migration Functions
function Remove-OrphanedVDSwitches {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ESXiHostName,
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.PSCredential]$Credential
    )
    
    Write-Log "Attempting to clean up orphaned distributed switch configurations on $ESXiHostName" -Level INFO
    
    try {
        # Connect directly to the ESXi host
        $directHostConnection = Connect-VIServer -Server $ESXiHostName -Credential $Credential -ErrorAction Stop
        Write-Log "Connected directly to ESXi host to clean up orphaned VDS configurations" -Level SUCCESS
    
        # Get the host view
        $hostView = Get-VMHost -Server $directHostConnection | Get-View
        
        # Get the network system
        $networkSystem = $hostView.ConfigManager.NetworkSystem
        $networkSystemView = Get-View -Id $networkSystem -Server $directHostConnection
        
        # Get proxy switches (VDS proxies)
        $proxySwitches = $networkSystemView.NetworkConfig.ProxySwitch
        
        if ($proxySwitches -and $proxySwitches.Count -gt 0) {
            Write-Log "Found $($proxySwitches.Count) proxy switches to clean up" -Level WARNING
            
            foreach ($proxySwitch in $proxySwitches) {
                Write-Log "Removing proxy switch: $($proxySwitch.Uuid) ($($proxySwitch.DvsName))" -Level INFO
                
                try {
                    # Create spec to remove the proxy switch
                    $spec = New-Object VMware.Vim.HostNetworkConfig
                    $spec.ProxySwitch = New-Object VMware.Vim.HostProxySwitchConfig[] (1)
                    $spec.ProxySwitch[0] = New-Object VMware.Vim.HostProxySwitchConfig
                    $spec.ProxySwitch[0].Uuid = $proxySwitch.Uuid
                    $spec.ProxySwitch[0].ChangeOperation = "remove"
                    
                    # Apply the change
                    $networkSystemView.UpdateNetworkConfig($spec, "modify")
                    Write-Log "Successfully removed proxy switch: $($proxySwitch.DvsName)" -Level SUCCESS
                }
                catch {
                    Write-Log "Error removing proxy switch $($proxySwitch.DvsName): $($_)" -Level ERROR
                }
            }
        }
        else {
            Write-Log "No orphaned proxy switches found on the host" -Level INFO
        }
    }
    catch {
        Write-Log "Failed to clean up orphaned distributed switches: $($_)" -Level ERROR
    }
    finally {
        # Disconnect from the host
        if ($directHostConnection) {
            Disconnect-VIServer -Server $directHostConnection -Confirm:$false -Force -ErrorAction SilentlyContinue
            Write-Log "Disconnected from ESXi host after cleanup" -Level INFO
        }
    }
}

function Migrate-VMHostBetweenVCenters {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$VMHostName,
        [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
        [ValidateNotNullOrEmpty()]
        [string]$SourceVCenter,
        [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
        [ValidateNotNullOrEmpty()]
        [string]$TargetVCenter,
        [Parameter(Mandatory = $true)]
        [string]$BackupPath,
        [Parameter(Mandatory = $false, ParameterSetName = "Migrate")]
        [System.Management.Automation.PSCredential]$SourceCredential,
        [Parameter(Mandatory = $false, ParameterSetName = "Migrate")]
        [System.Management.Automation.PSCredential]$TargetCredential,
        [Parameter(Mandatory = $true, ParameterSetName = "Migrate")]
        [System.Management.Automation.PSCredential]$ESXiHostCredential,
        [Parameter(Mandatory = $false)]
        [string]$TargetDatacenterName,
        [Parameter(Mandatory = $false)]
        [string]$TargetClusterName,
        [Parameter(Mandatory = $false)]
        [int]$OperationTimeout = 600,
        [Parameter(Mandatory = $false)]
        [string]$UplinkPortgroupName
    )

    # Validate ESXi host credentials were provided
    if (-not $ESXiHostCredential) {
        Write-Log "ESXi host credentials are required for migration" -Level ERROR
        throw "ESXiHostCredential parameter is required for migration"
    }

    # Validate backup path exists or create it
    if (-not (Test-Path -Path $($BackupPath))) {
        try {
            New-Item -ItemType Directory -Path $($BackupPath) -Force | Out-Null
            Write-Log "Backup directory $($BackupPath) created" -Level DEBUG
        }
        catch {
            Write-Log "Failed to create backup directory $($BackupPath): $($_)" -Level ERROR
            throw "Backup path $($BackupPath) does not exist and could not be created."
        }
    }

    # Connect to source vCenter
    $srcVC = Connect-ToVIServer -Server $SourceVCenter -Credential $SourceCredential

    # Get the ESXi host object
    Write-Log "Getting host $($VMHostName) from source vCenter $($SourceVCenter)" -Level INFO
    try {
        $vmHost = Get-VMHost -Name $($VMHostName) -ErrorAction Stop -Server $srcVC

        # Check host connection using the Test-VMHostConnection function
        if (-not (Test-VMHostConnection -VMHost $vmHost)) {
            # Prompt the user to put the host in maintenance mode
            $promptMessage = "Host $($VMHostName) is not in maintenance mode. Do you want to put it in maintenance mode and continue? (Y/N)"
            $response = Read-Host $promptMessage

            if ($response -match "^[Yy]") {
                Write-Log "User chose to put host in maintenance mode" -Level INFO
                try {
                    Set-VMHost -VMHost $vmHost -State "Maintenance" -Confirm:$false -ErrorAction Stop
                    Start-Sleep -Seconds 10

                    # Refresh host object
                    $vmHost = Get-VMHost -Name $($VMHostName) -ErrorAction Stop -Server $srcVC

                    # Re-test the connection after attempting to enter maintenance mode
                    if (-not (Test-VMHostConnection -VMHost $vmHost)) {
                        Write-Log "Failed to put host in maintenance mode. Exiting script." -Level ERROR
                        throw "Failed to put host in maintenance mode."
                    }
                }
                catch {
                    Write-Log "Error putting host in maintenance mode: $($_). Exiting script." -Level ERROR
                    throw "Error putting host in maintenance mode."
                }
            }
            else {
                Write-Log "User chose not to put host in maintenance mode. Exiting script." -Level INFO
                throw "Host is not in maintenance mode. Script execution aborted."
            }
        } else {
            Write-Log "Host is already in maintenance mode and connected. Continuing..." -Level INFO
        }
    }
    catch {
        Write-Log "Failed to get host from source vCenter $($SourceVCenter): $($_)" -Level ERROR
        throw $_
    }

    # Create a unique filename for the rollback backup
    $rollbackBackupFile = Join-Path -Path $BackupPath -ChildPath "Rollback_VMHost_$($VMHostName.Split('.')[0])_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"

    try {
        # Check and handle lockdown mode before migration
        Write-Log "Checking host lockdown mode before migration" -Level INFO
        $wasInLockdownMode = Test-ESXiHostLockdownMode -ESXiHostName $VMHostName -Credential $ESXiHostCredential
        $originalLockdownMode = $null
        
        if ($wasInLockdownMode) {
            Write-Log "Host is in lockdown mode. This will prevent adding to new vCenter." -Level WARNING
            
            # Get the current lockdown mode for restoration later
            try {
                $directConnection = Connect-VIServer -Server $VMHostName -Credential $ESXiHostCredential -ErrorAction Stop
                $tempHost = Get-VMHost -Server $directConnection -ErrorAction Stop
                $originalLockdownMode = $tempHost.ExtensionData.Config.LockdownMode
                Disconnect-VIServer -Server $directConnection -Confirm:$false -Force -ErrorAction SilentlyContinue
                Write-Log "Original lockdown mode: $originalLockdownMode" -Level INFO
            }
            catch {
                Write-Log "Could not determine original lockdown mode: $($_)" -Level WARNING
                $originalLockdownMode = "lockdownNormal"  # Default assumption
            }
            
            # Prompt user to disable lockdown mode
            $promptMessage = "Host $VMHostName is in lockdown mode which will prevent migration. Do you want to temporarily disable lockdown mode for migration? (Y/N)"
            $response = Read-Host $promptMessage
            $disableLockdown = ($response -match "^[Yy]")
            
            if ($disableLockdown) {
                Write-Log "User chose to disable lockdown mode for migration" -Level INFO
                $lockdownDisabled = Disable-ESXiHostLockdownMode -ESXiHostName $VMHostName -Credential $ESXiHostCredential
                
                if (-not $lockdownDisabled) {
                    Write-Log "Failed to disable lockdown mode. Migration cannot continue." -Level ERROR
                    throw "Failed to disable lockdown mode on host $VMHostName"
                }
                
                Write-Log "Lockdown mode disabled successfully. Migration will continue." -Level SUCCESS
            } else {
                Write-Log "User chose not to disable lockdown mode. Migration cannot continue." -Level ERROR
                throw "Host $VMHostName is in lockdown mode and user chose not to disable it."
            }
        } else {
            Write-Log "Host is not in lockdown mode. Migration can proceed." -Level SUCCESS
        }

        # Backup host configuration BEFORE migration
        Write-Log "Backing up host configuration BEFORE migration for rollback purposes" -Level INFO
        try {
            $backupFile = Backup-VMHostConfiguration -Server $vmHost -BackupPath $BackupPath
        }
        catch {
            Write-Log "Failed to backup host configuration (initial backup): $($_)" -Level ERROR
            throw
        }

        # Create a temporary file path for the rollback backup
        Write-Log "Backing up current host configuration for rollback purposes..." -Level INFO
        $null = Backup-VMHostConfiguration -Server $vmHost -BackupPath (Split-Path $rollbackBackupFile -Parent)
        #Rename file so it doesnt conflict
        Rename-Item -Path (Join-Path -Path (Split-Path $rollbackBackupFile -Parent) -ChildPath ("VMHost_"+$($vmHost.Name.Split('.')[0])+"_"+(Get-Date -Format "yyyyMMdd_HHmmss")+".json")) -NewName (Split-Path $rollbackBackupFile -Leaf)

        # Disconnect host from source vCenter
        Write-Log "Disconnecting host $($VMHostName) from source vCenter $($SourceVCenter)" -Level INFO
        try {
            Set-VMHost -VMHost $vmHost -State "Disconnected" -Confirm:$false -ErrorAction Stop
            Write-Log "Host $($VMHostName) successfully disconnected from source vCenter $($SourceVCenter)" -Level SUCCESS
            # Wait to ensure disconnection is complete
            Start-Sleep -Seconds 10
        }
        catch {
            Write-Log "Failed to disconnect host: $($_)" -Level ERROR
            throw $_
        }

        # Disconnect from source vCenter
        Disconnect-VIServer -Server $srcVC -Confirm:$false
        Write-Log "Disconnected from source vCenter $($SourceVCenter)" -Level INFO

        # After disconnecting from source vCenter and before connecting to target vCenter
        Write-Log "Cleaning up orphaned distributed switch configurations before adding to new vCenter" -Level INFO
        Remove-OrphanedVDSwitches -ESXiHostName $VMHostName -Credential $ESXiHostCredential

        # Wait additional time to ensure vCenter operations complete and host is fully disconnected
        Write-Log "Waiting 30 seconds to ensure host disconnection processes complete" -Level INFO
        Start-Sleep -Seconds 30

        # Connect to target vCenter
        $tgtVC = Connect-ToVIServer -Server $TargetVCenter -Credential $TargetCredential

        # Check if host already exists in target vCenter
        $existingHost = Get-VMHost -Name $VMHostName -Server $tgtVC -ErrorAction SilentlyContinue

        if ($existingHost) {
            # Verify that the host is actually managed by the target vCenter
            if ($existingHost.Uid -like "*/VIServer=$($TargetVCenter)*") {
                Write-Log "Host $VMHostName already exists in target vCenter $($TargetVCenter). Checking its status." -Level WARNING

                if ($existingHost.ConnectionState -ne "Connected") {
                    Write-Log "Host exists but is in $($existingHost.ConnectionState) state. Attempting to reconnect." -Level INFO
                    try {
                        # Use Connect-VIServer directly for the host with credentials
                        Connect-VIServer -Server $VMHostName -Credential $ESXiHostCredential -ErrorAction Stop | Out-Null

                        # Now reconnect through vCenter
                        Set-VMHost -VMHost $existingHost -State "Connected" -Confirm:$false -ErrorAction Stop
                        Start-Sleep -Seconds 10
                        $existingHost = Get-VMHost -Name $VMHostName -Server $tgtVC -ErrorAction Stop
                        $newHost = $existingHost  # Use this for later configuration
                        Write-Log "Successfully reconnected existing host" -Level SUCCESS
                    }
                    catch {
                        Write-Log "Failed to reconnect existing host: $($_). Will attempt to add it again." -Level WARNING
                        $existingHost = $null  # Reset so we try to add it
                    }
                }
                else {
                    Write-Log "Host is already connected to target vCenter $($TargetVCenter). Using existing connection." -Level INFO
                    $newHost = $existingHost  # Use this for later configuration
                }
            }
            else {
                Write-Log "Host $VMHostName found, but not managed by target vCenter $($TargetVCenter). Continuing to add it." -Level WARNING
                $existingHost = $null  # Treat it as if it doesn't exist
            }
        }

        # Determine target location and add host if needed
        if (-not $existingHost) {
            # Determine target location
            Write-Log "Determining target location..." -Level INFO
            try {
                if ($TargetDatacenterName -and $TargetClusterName) {
                    $datacenter = Get-Datacenter -Name $TargetDatacenterName -ErrorAction Stop
                    $cluster = Get-Cluster -Name $TargetClusterName -Location $datacenter -ErrorAction Stop
                    $location = $cluster
                    Write-Log "Using specified Datacenter: '$($TargetDatacenterName)' and Cluster: '$($TargetClusterName)'." -Level INFO
                } elseif ($TargetDatacenterName) {
                    $location = Get-Datacenter -Name $TargetDatacenterName -ErrorAction Stop
                    Write-Log "Using specified Datacenter: '$($TargetDatacenterName)'." -Level INFO
                } else {
                    $location = Get-Datacenter | Select-Object -First 1
                    Write-Log "No Datacenter or Cluster specified. Using first Datacenter found: '$($location.Name)'." -Level INFO
                }
            }
            catch {
                Write-Log "Failed to determine target location: $($_)" -Level ERROR
                throw $_
            }

            # Test direct connection to ESXi host first
            try {
                Write-Log "Testing direct connection to ESXi host $VMHostName before adding to vCenter" -Level INFO
                $directConnection = Connect-VIServer -Server $VMHostName -Credential $ESXiHostCredential -ErrorAction Stop
                Write-Log "Successfully connected directly to host $VMHostName" -Level SUCCESS
                Disconnect-VIServer -Server $directConnection -Confirm:$false -ErrorAction SilentlyContinue
            } catch {
                Write-Log "Cannot connect directly to host $($VMHostName): $($_)" -Level ERROR
                Write-Log "Please check credentials and network connectivity to the host" -Level ERROR
                throw "Cannot connect to host directly. Please check credentials and network connectivity."
            }
            # Add host to target vCenter with improved error handling
            Write-Log "Adding host $($VMHostName) to target vCenter $($TargetVCenter) in location $($location.Name)" -Level INFO
            try {
                $newHost = Add-VMHost -Name $($VMHostName) -Location $location -Credential $ESXiHostCredential -Force -ErrorAction Stop
                Write-Log "Host $($VMHostName) successfully added to target vCenter $($TargetVCenter)" -Level SUCCESS

                # Wait longer to ensure host is fully connected
                Write-Log "Waiting 60 seconds for host connection to stabilize..." -Level INFO
                Start-Sleep -Seconds 60
                $newHost = Get-VMHost -Name $VMHostName -Server $tgtVC -ErrorAction Stop
            }
            catch {
                Write-Log "Failed to add host to target vCenter: $($_)" -Level ERROR
                Write-Log "Exception type: $($_.Exception.GetType().FullName)" -Level ERROR
                Write-Log "Exception details: $($_.Exception.Message)" -Level ERROR

                # Check if it's a credential issue
                if ($_.Exception.Message -like "*incorrect user name or password*") {
                    Write-Log "This appears to be a credential issue. Please verify the ESXi host credentials." -Level ERROR
                }
                # Check if it's a network connectivity issue
                elseif ($_.Exception.Message -like "*Unable to connect to the remote server*") {
                    Write-Log "This appears to be a network connectivity issue. Please verify the host is reachable." -Level ERROR
                }
                # Check if it's a certificate issue
                elseif ($_.Exception.Message -like "*certificate*") {
                    Write-Log "This appears to be a certificate validation issue." -Level ERROR

                    # Try again with explicit certificate bypass
                    try {
                        Write-Log "Attempting to add host with certificate validation bypassed..." -Level INFO
                        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
                        $newHost = Add-VMHost -Name $($VMHostName) -Location $location -Credential $ESXiHostCredential -Force -ErrorAction Stop
                        Write-Log "Host successfully added with certificate validation bypassed" -Level SUCCESS
                    }
                    catch {
                        Write-Log "Still failed to add host: $($_)" -Level ERROR
                        throw
                    }
                }
                else {
                    throw
                }
            }
        }

        # Restore host configuration
        Write-Log "Restoring host configuration on target vCenter $($TargetVCenter)" -Level INFO
        Restore-VMHostConfiguration -Server $newHost -ConfigFilePath $($backupFile) -Timeout $OperationTimeout -UplinkPortgroupName $UplinkPortgroupName

        # Restore original lockdown mode if it was disabled for migration
        if ($wasInLockdownMode -and $originalLockdownMode -and $originalLockdownMode -ne "lockdownDisabled") {
            Write-Log "Restoring original lockdown mode: $originalLockdownMode" -Level INFO
            $lockdownRestored = Enable-ESXiHostLockdownMode -ESXiHostName $VMHostName -Credential $ESXiHostCredential -LockdownMode $originalLockdownMode
            
            if ($lockdownRestored) {
                Write-Log "Successfully restored original lockdown mode" -Level SUCCESS
            } else {
                Write-Log "Failed to restore original lockdown mode. Manual intervention may be required." -Level WARNING
            }
        }
    }
    catch {
        Write-Log "An error occurred during migration: $($_)" -Level ERROR
        Write-Log "Error details: $($_.Exception.Message)" -Level ERROR
        Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level DEBUG

        Write-Log "Attempting to rollback to the previous configuration..." -Level WARNING
        try {
            Rollback-VMHostConfiguration -Server $vmHost -ConfigFilePath $rollbackBackupFile -UplinkPortgroupName $UplinkPortgroupName
        }
        catch {
            Write-Log "Rollback failed: $($_)" -Level ERROR
            Write-Log "Manual intervention might be required to restore the host to a consistent state." -Level ERROR
        }
        throw # Re-throw the original exception to stop further processing
    }
    finally {
        # Disconnect from target vCenter
        if ($tgtVC -and $tgtVC.IsConnected) {
            try {
                Disconnect-VIServer -Server $tgtVC -Confirm:$false
                Write-Log "Disconnected from target vCenter $($TargetVCenter)" -Level INFO
            }
            catch {
                Write-Log "Error disconnecting from target vCenter $($TargetVCenter): $($_)" -Level WARNING
            }
        }

        # Disconnect from source vCenter
        if ($srcVC -and $srcVC.IsConnected) {
            try {
                Disconnect-VIServer -Server $srcVC -Confirm:$false
                Write-Log "Disconnected from source vCenter $($SourceVCenter)" -Level INFO
            }
            catch {
                Write-Log "Error disconnecting from source vCenter $($SourceVCenter): $($_)" -Level WARNING
            }
        }

        # Clean up the rollback backup file
        if (Test-Path $rollbackBackupFile) {
            try {
                Remove-Item $rollbackBackupFile -Force -ErrorAction Stop
                Write-Log "Successfully removed rollback backup file: $($rollbackBackupFile)" -Level DEBUG
            }
            catch {
                Write-Log "Error removing rollback backup file $($rollbackBackupFile): $($_)" -Level WARNING
            }
        }
        Write-Log "Migration completed (or attempted). See logs for details." -Level INFO
    }
}
#endregion

#region Main script execution
try {
    Write-Log "Script execution started - Action: $Action" -Level INFO

    # Check PowerCLI version
    $powerCliModule = Get-Module -Name VMware.PowerCLI -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
    if ($powerCliModule) {
        Write-Log "PowerCLI version $($powerCliModule.Version) detected" -Level INFO
    } else {
        Write-Log "PowerCLI module not found. Script may fail." -Level WARNING
    }
    
    # Set PowerCLI configuration to ignore certificate errors
    Write-Log "Configuring PowerCLI to ignore certificate errors" -Level DEBUG
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null

    # Connect to vCenter (source for Backup and Restore, source for Migrate)
    if ($Action -ne "Migrate") {
        Write-Log "Connecting to vCenter $vCenter..." -Level INFO
        if ($Credential) {
            $vc = Connect-VIServer -Server $vCenter -Credential $Credential -ErrorAction Stop
        }
        else {
            $vc = Connect-VIServer -Server $vCenter -ErrorAction Stop
        }
        Write-Log "Successfully connected to vCenter $vCenter" -Level SUCCESS

        # Get the VMHost (only for Backup and Restore)
        Write-Log "Attempting to find host $VMHostName" -Level DEBUG
        $vmHost = Get-VMHost -Name $VMHostName -ErrorAction Stop
        Write-Log "Found host $($vmHost.Name) in vCenter" -Level SUCCESS
    }

    switch ($Action) {
        "Backup" {
            Write-Log "Starting backup operation" -Level INFO
            $backupFile = Backup-VMHostConfiguration -Server $vmHost -BackupPath $BackupPath
            Write-Log "Backup completed successfully to: $backupFile" -Level SUCCESS
        }
        "Restore" {
            Write-Log "Starting restore operation" -Level INFO
            if (-not $BackupFile) {
                $errorMsg = "BackupFile parameter is required for Restore action"
                Write-Log $errorMsg -Level ERROR
                throw $errorMsg
            }
            
            if (-not (Test-Path -Path $BackupFile)) {
                $errorMsg = "Backup file not found: $BackupFile"
                Write-Log $errorMsg -Level ERROR
                throw $errorMsg
            }
            
            Restore-VMHostConfiguration -Server $vmHost -ConfigFilePath $BackupFile -Timeout $OperationTimeout
            Write-Log "Restore completed successfully" -Level SUCCESS
        }
        "Migrate" {
            Write-Log "Starting migration operation" -Level INFO
            if (-not $TargetVCenter) {
                $errorMsg = "TargetVCenter parameter is required for Migrate action"
                Write-Log $errorMsg -Level ERROR
                throw $errorMsg
            }

            Migrate-VMHostBetweenVCenters -VMHostName $VMHostName `
                -SourceVCenter $SourceVCenter `
                -TargetVCenter $TargetVCenter `
                -BackupPath $BackupPath `
                -SourceCredential $SourceCredential `
                -TargetCredential $TargetCredential `
                -ESXiHostCredential $ESXiHostCredential `
                -TargetDatacenterName $TargetDatacenterName `
                -TargetClusterName $TargetClusterName `
                -OperationTimeout $OperationTimeout `
                -UplinkPortgroupName $UplinkPortgroupName
            Write-Log "Migration completed successfully" -Level SUCCESS
        }
    }
}
catch {
    Write-Log "An error occurred: $($_)" -Level ERROR
    Write-Log "Error details: $($_.Exception.Message)" -Level ERROR
    Write-Log "Stack trace: $($_.ScriptStackTrace)" -Level DEBUG
}
finally {
    # Disconnect from any connected vCenter servers
    if ($vc -and $vc.IsConnected) {
        Write-Log "Disconnecting from vCenter $vCenter" -Level INFO
        Disconnect-VIServer -Server $vCenter -Confirm:$false
    }
    Write-Log "Script execution completed" -Level INFO
}
#endregion
