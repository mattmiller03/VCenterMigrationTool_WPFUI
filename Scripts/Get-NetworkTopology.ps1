param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory = $true)]
    [string]$Username,
    
    [Parameter(Mandatory = $true)]
    [string]$Password,
    
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = ""
)

# Function to write log messages
function Write-Log {
    param([string]$Message, [string]$Level = "Info")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Host $logMessage
    
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        try {
            $logMessage | Out-File -FilePath $LogPath -Append -Encoding UTF8
        }
        catch {
            # Ignore log file errors
        }
    }
}

try {
    Write-Log "Starting network topology discovery..." "Info"
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Importing PowerCLI modules..." "Info"
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-Log "PowerCLI modules imported successfully" "Info"
        }
        catch {
            Write-Error "Failed to import PowerCLI modules: $($_.Exception.Message)"
            exit 1
        }
    }
    
    # Connect to vCenter if not already connected
    $existingConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer -and $_.IsConnected }
    
    if (-not $existingConnection) {
        Write-Log "Connecting to vCenter: $VCenterServer" "Info"
        $credential = New-Object System.Management.Automation.PSCredential($Username, (ConvertTo-SecureString $Password -AsPlainText -Force))
        Connect-VIServer -Server $VCenterServer -Credential $credential -Force | Out-Null
    } else {
        Write-Log "Using existing connection to vCenter: $VCenterServer" "Info"
    }
    
    # Get all ESXi hosts
    $vmHosts = Get-VMHost
    $networkTopology = @()
    
    foreach ($vmHost in $vmHosts) {
        Write-Log "Processing network topology for host: $($vmHost.Name)" "Info"
        
        $hostNetworkConfig = @{
            Name = $vmHost.Name
            VSwitches = @()
            VmKernelPorts = @()
        }
        
        # Get standard vSwitches
        try {
            $standardSwitches = Get-VirtualSwitch -VMHost $vmHost -Standard -ErrorAction SilentlyContinue
            
            foreach ($vSwitch in $standardSwitches) {
                $switchConfig = @{
                    IsSelected = $true
                    Name = $vSwitch.Name
                    Type = "Standard"
                    NumPorts = $vSwitch.NumPorts
                    Mtu = $vSwitch.Mtu
                    Nics = @($vSwitch.Nic)
                    PortGroups = @()
                }
                
                # Get port groups for this vSwitch
                try {
                    $portGroups = Get-VirtualPortGroup -VMHost $vmHost -VirtualSwitch $vSwitch -ErrorAction SilentlyContinue
                    
                    foreach ($portGroup in $portGroups) {
                        $pgConfig = @{
                            IsSelected = $true
                            Name = $portGroup.Name
                            VlanId = $portGroup.VLanId
                            ActiveNics = @($portGroup.ActiveNic)
                            StandbyNics = @($portGroup.StandbyNic)
                        }
                        
                        $switchConfig.PortGroups += $pgConfig
                    }
                }
                catch {
                    Write-Log "Could not retrieve port groups for vSwitch '$($vSwitch.Name)' on host '$($vmHost.Name)': $($_.Exception.Message)" "Warning"
                }
                
                $hostNetworkConfig.VSwitches += $switchConfig
            }
        }
        catch {
            Write-Log "Could not retrieve standard vSwitches for host '$($vmHost.Name)': $($_.Exception.Message)" "Warning"
        }
        
        # Get distributed vSwitches (if any)
        try {
            $distributedSwitches = Get-VDSwitch -VMHost $vmHost -ErrorAction SilentlyContinue
            
            foreach ($vdSwitch in $distributedSwitches) {
                $vdSwitchConfig = @{
                    IsSelected = $true
                    Name = $vdSwitch.Name
                    Type = "Distributed"
                    Version = $vdSwitch.Version
                    NumUplinkPorts = $vdSwitch.NumUplinkPorts
                    Mtu = $vdSwitch.Mtu
                    PortGroups = @()
                }
                
                # Get distributed port groups
                try {
                    $vdPortGroups = Get-VDPortgroup -VDSwitch $vdSwitch -ErrorAction SilentlyContinue
                    
                    foreach ($vdPortGroup in $vdPortGroups) {
                        $vdPgConfig = @{
                            IsSelected = $true
                            Name = $vdPortGroup.Name
                            VlanId = if ($vdPortGroup.VlanConfiguration.VlanId) { $vdPortGroup.VlanConfiguration.VlanId } else { 0 }
                            NumPorts = $vdPortGroup.NumPorts
                            PortBinding = $vdPortGroup.PortBinding
                        }
                        
                        $vdSwitchConfig.PortGroups += $vdPgConfig
                    }
                }
                catch {
                    Write-Log "Could not retrieve distributed port groups for vDSwitch '$($vdSwitch.Name)': $($_.Exception.Message)" "Warning"
                }
                
                $hostNetworkConfig.VSwitches += $vdSwitchConfig
            }
        }
        catch {
            Write-Log "Could not retrieve distributed vSwitches for host '$($vmHost.Name)': $($_.Exception.Message)" "Warning"
        }
        
        # Get VMkernel ports
        try {
            $vmkPorts = Get-VMHostNetworkAdapter -VMHost $vmHost -VMKernel -ErrorAction SilentlyContinue
            
            foreach ($vmkPort in $vmkPorts) {
                $vmkConfig = @{
                    IsSelected = $true
                    Name = $vmkPort.Name
                    IpAddress = $vmkPort.IP
                    SubnetMask = $vmkPort.SubnetMask
                    VSwitchName = $vmkPort.VirtualSwitch
                    PortGroupName = $vmkPort.PortGroupName
                    VMotionEnabled = $vmkPort.VMotionEnabled
                    ManagementTrafficEnabled = $vmkPort.ManagementTrafficEnabled
                    FaultToleranceLoggingEnabled = $vmkPort.FaultToleranceLoggingEnabled
                    VSANTrafficEnabled = $vmkPort.VSANTrafficEnabled
                    Mtu = $vmkPort.Mtu
                }
                
                $hostNetworkConfig.VmKernelPorts += $vmkConfig
            }
        }
        catch {
            Write-Log "Could not retrieve VMkernel ports for host '$($vmHost.Name)': $($_.Exception.Message)" "Warning"
        }
        
        $networkTopology += $hostNetworkConfig
        
        Write-Log "Host '$($vmHost.Name)' network summary: $($hostNetworkConfig.VSwitches.Count) vSwitches, $($hostNetworkConfig.VmKernelPorts.Count) VMkernel ports" "Info"
    }
    
    # Convert to JSON for output
    $jsonOutput = $networkTopology | ConvertTo-Json -Depth 10
    Write-Output $jsonOutput
    
    Write-Log "Network topology discovery completed successfully" "Info"
    Write-Log "Total hosts processed: $($vmHosts.Count)" "Info"
    Write-Log "Total network configurations: $($networkTopology.Count)" "Info"
}
catch {
    $errorMsg = "Network topology discovery failed: $($_.Exception.Message)"
    Write-Log $errorMsg "Error"
    Write-Error $errorMsg
    exit 1
}

finally {
    # Cleanup PowerCLI session
    if (-not $BypassModuleCheck) {
        Write-Log "Disconnecting from vCenter server..." "Info"
        Disconnect-VIServer -Server $VCenterServer -Confirm:$false -ErrorAction SilentlyContinue
        Write-Log "Disconnected from vCenter server" "Info"
    }
}