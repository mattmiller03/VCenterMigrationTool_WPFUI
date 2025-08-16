param(
    [Parameter(Mandatory = $true)]
    [string]$SourceVCenter,
    
    [Parameter(Mandatory = $true)]
    [string]$TargetVCenter,
    
    [bool]$MigrateStandardSwitches = $true,
    [bool]$MigrateDistributedSwitches = $false,
    [bool]$MigratePortGroups = $true,
    [bool]$MigrateVmkernelPorts = $true,
    [bool]$PreserveVlanIds = $true,
    [bool]$RecreateIfExists = $false,
    [bool]$ValidateOnly = $false,
    [hashtable]$NetworkMappings = @{},
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
    Write-Log "Starting network configuration migration..." "Info"
    
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
    else {
        Write-Log "Bypassing PowerCLI module check (already confirmed installed)" "Info"
    }
    
    # Check vCenter connections
    $sourceConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $SourceVCenter }
    $targetConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $TargetVCenter }
    
    if (-not $sourceConnection -or -not $sourceConnection.IsConnected) {
        Write-Error "No active connection to source vCenter: $SourceVCenter"
        exit 1
    }
    
    if (-not $targetConnection -or -not $targetConnection.IsConnected) {
        Write-Error "No active connection to target vCenter: $TargetVCenter"
        exit 1
    }
    
    Write-Log "Connected to source vCenter: $SourceVCenter" "Info"
    Write-Log "Connected to target vCenter: $TargetVCenter" "Info"
    
    # Get source network configuration
    Write-Log "Analyzing source network configuration..." "Info"
    
    $sourceHosts = Get-VMHost -Server $sourceConnection
    $sourceNetworkConfig = @{
        StandardSwitches = @()
        DistributedSwitches = @()
        PortGroups = @()
        VmkernelPorts = @()
    }
    
    # Process each source host
    foreach ($vmHost in $sourceHosts) {
        Write-Log "Processing host: $($vmHost.Name)" "Info"
        
        # Get standard vSwitches
        if ($MigrateStandardSwitches) {
            $standardSwitches = Get-VirtualSwitch -VMHost $vmHost -Standard
            foreach ($vSwitch in $standardSwitches) {
                $sourceNetworkConfig.StandardSwitches += @{
                    HostName = $vmHost.Name
                    Name = $vSwitch.Name
                    NumPorts = $vSwitch.NumPorts
                    Mtu = $vSwitch.Mtu
                    Nics = @($vSwitch.Nic)
                }
                
                # Get port groups for this vSwitch
                if ($MigratePortGroups) {
                    $portGroups = Get-VirtualPortGroup -VMHost $vmHost -VirtualSwitch $vSwitch
                    foreach ($portGroup in $portGroups) {
                        $sourceNetworkConfig.PortGroups += @{
                            HostName = $vmHost.Name
                            VSwitchName = $vSwitch.Name
                            Name = $portGroup.Name
                            VLanId = $portGroup.VLanId
                            VSwitchType = "Standard"
                        }
                    }
                }
            }
        }
        
        # Get distributed vSwitches (if enabled)
        if ($MigrateDistributedSwitches) {
            try {
                $distributedSwitches = Get-VDSwitch -VMHost $vmHost -ErrorAction SilentlyContinue
                foreach ($vdSwitch in $distributedSwitches) {
                    $sourceNetworkConfig.DistributedSwitches += @{
                        Name = $vdSwitch.Name
                        Version = $vdSwitch.Version
                        NumUplinkPorts = $vdSwitch.NumUplinkPorts
                        Mtu = $vdSwitch.Mtu
                        Notes = $vdSwitch.Notes
                    }
                }
            }
            catch {
                Write-Log "Could not retrieve distributed switches for host $($vmHost.Name): $($_.Exception.Message)" "Warning"
            }
        }
        
        # Get VMkernel ports
        if ($MigrateVmkernelPorts) {
            $vmkPorts = Get-VMHostNetworkAdapter -VMHost $vmHost -VMKernel
            foreach ($vmkPort in $vmkPorts) {
                $sourceNetworkConfig.VmkernelPorts += @{
                    HostName = $vmHost.Name
                    Name = $vmkPort.Name
                    IP = $vmkPort.IP
                    SubnetMask = $vmkPort.SubnetMask
                    PortGroupName = $vmkPort.PortGroupName
                    VMotionEnabled = $vmkPort.VMotionEnabled
                    ManagementTrafficEnabled = $vmkPort.ManagementTrafficEnabled
                    FaultToleranceLoggingEnabled = $vmkPort.FaultToleranceLoggingEnabled
                    VSANTrafficEnabled = $vmkPort.VSANTrafficEnabled
                }
            }
        }
    }
    
    Write-Log "Source network analysis complete:" "Info"
    Write-Log "  Standard vSwitches: $($sourceNetworkConfig.StandardSwitches.Count)" "Info"
    Write-Log "  Distributed vSwitches: $($sourceNetworkConfig.DistributedSwitches.Count)" "Info"
    Write-Log "  Port Groups: $($sourceNetworkConfig.PortGroups.Count)" "Info"
    Write-Log "  VMkernel Ports: $($sourceNetworkConfig.VmkernelPorts.Count)" "Info"
    
    if ($ValidateOnly) {
        Write-Log "Validation mode enabled - no changes will be made" "Info"
        
        # Validate target environment
        Write-Log "Validating target environment..." "Info"
        $targetHosts = Get-VMHost -Server $targetConnection
        
        Write-Log "Target validation results:" "Info"
        Write-Log "  Target hosts available: $($targetHosts.Count)" "Info"
        
        # Check for naming conflicts
        $conflicts = @()
        foreach ($portGroup in $sourceNetworkConfig.PortGroups) {
            $mappedName = if ($NetworkMappings.ContainsKey($portGroup.Name)) { 
                $NetworkMappings[$portGroup.Name] 
            } else { 
                $portGroup.Name 
            }
            
            $existingPG = Get-VirtualPortGroup -Name $mappedName -Server $targetConnection -ErrorAction SilentlyContinue
            if ($existingPG -and -not $RecreateIfExists) {
                $conflicts += "Port Group '$mappedName' already exists on target"
            }
        }
        
        if ($conflicts.Count -gt 0) {
            Write-Log "Validation found conflicts:" "Warning"
            foreach ($conflict in $conflicts) {
                Write-Log "  $conflict" "Warning"
            }
        } else {
            Write-Log "Validation passed - no conflicts found" "Info"
        }
        
        Write-Host "SUCCESS: Network migration validation completed"
        exit 0
    }
    
    # Start actual migration
    Write-Log "Starting network configuration migration..." "Info"
    $migrationResults = @{
        StandardSwitches = @()
        PortGroups = @()
        VmkernelPorts = @()
        Errors = @()
    }
    
    # Get target hosts
    $targetHosts = Get-VMHost -Server $targetConnection
    if ($targetHosts.Count -eq 0) {
        throw "No target hosts available for migration"
    }
    
    # Migrate standard vSwitches
    if ($MigrateStandardSwitches -and $sourceNetworkConfig.StandardSwitches.Count -gt 0) {
        Write-Log "Migrating standard vSwitches..." "Info"
        
        foreach ($switch in $sourceNetworkConfig.StandardSwitches) {
            try {
                # Find corresponding target host (by name or use first available)
                $targetHost = $targetHosts | Where-Object { $_.Name -eq $switch.HostName } | Select-Object -First 1
                if (-not $targetHost) {
                    $targetHost = $targetHosts | Select-Object -First 1
                    Write-Log "Target host '$($switch.HostName)' not found, using '$($targetHost.Name)'" "Warning"
                }
                
                # Check if vSwitch already exists
                $existingSwitch = Get-VirtualSwitch -VMHost $targetHost -Name $switch.Name -Standard -ErrorAction SilentlyContinue
                
                if ($existingSwitch) {
                    if ($RecreateIfExists) {
                        Write-Log "Removing existing vSwitch '$($switch.Name)' on host '$($targetHost.Name)'" "Info"
                        Remove-VirtualSwitch -VirtualSwitch $existingSwitch -Confirm:$false
                    } else {
                        Write-Log "vSwitch '$($switch.Name)' already exists on host '$($targetHost.Name)', skipping" "Warning"
                        continue
                    }
                }
                
                # Create new vSwitch
                Write-Log "Creating vSwitch '$($switch.Name)' on host '$($targetHost.Name)'" "Info"
                $newSwitch = New-VirtualSwitch -VMHost $targetHost -Name $switch.Name -NumPorts $switch.NumPorts
                
                # Set MTU if different from default
                if ($switch.Mtu -ne 1500) {
                    Set-VirtualSwitch -VirtualSwitch $newSwitch -Mtu $switch.Mtu -Confirm:$false
                }
                
                $migrationResults.StandardSwitches += "Created vSwitch '$($switch.Name)' on host '$($targetHost.Name)'"
            }
            catch {
                $error = "Failed to create vSwitch '$($switch.Name)': $($_.Exception.Message)"
                Write-Log $error "Error"
                $migrationResults.Errors += $error
            }
        }
    }
    
    # Migrate port groups
    if ($MigratePortGroups -and $sourceNetworkConfig.PortGroups.Count -gt 0) {
        Write-Log "Migrating port groups..." "Info"
        
        foreach ($portGroup in $sourceNetworkConfig.PortGroups) {
            try {
                # Apply network mapping if configured
                $targetPortGroupName = if ($NetworkMappings.ContainsKey($portGroup.Name)) { 
                    $NetworkMappings[$portGroup.Name] 
                } else { 
                    $portGroup.Name 
                }
                
                # Find target host
                $targetHost = $targetHosts | Where-Object { $_.Name -eq $portGroup.HostName } | Select-Object -First 1
                if (-not $targetHost) {
                    $targetHost = $targetHosts | Select-Object -First 1
                }
                
                # Find target vSwitch
                $targetVSwitch = Get-VirtualSwitch -VMHost $targetHost -Name $portGroup.VSwitchName -Standard -ErrorAction SilentlyContinue
                if (-not $targetVSwitch) {
                    Write-Log "Target vSwitch '$($portGroup.VSwitchName)' not found on host '$($targetHost.Name)', skipping port group '$($portGroup.Name)'" "Warning"
                    continue
                }
                
                # Check if port group already exists
                $existingPG = Get-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -ErrorAction SilentlyContinue
                
                if ($existingPG) {
                    if ($RecreateIfExists) {
                        Write-Log "Removing existing port group '$targetPortGroupName' on host '$($targetHost.Name)'" "Info"
                        Remove-VirtualPortGroup -VirtualPortGroup $existingPG -Confirm:$false
                    } else {
                        Write-Log "Port group '$targetPortGroupName' already exists on host '$($targetHost.Name)', skipping" "Warning"
                        continue
                    }
                }
                
                # Create port group
                Write-Log "Creating port group '$targetPortGroupName' on vSwitch '$($targetVSwitch.Name)'" "Info"
                $vlanId = if ($PreserveVlanIds) { $portGroup.VLanId } else { 0 }
                
                $newPortGroup = New-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -VirtualSwitch $targetVSwitch -VLanId $vlanId
                
                $migrationResults.PortGroups += "Created port group '$targetPortGroupName' (VLAN $vlanId) on host '$($targetHost.Name)'"
            }
            catch {
                $error = "Failed to create port group '$($portGroup.Name)': $($_.Exception.Message)"
                Write-Log $error "Error"
                $migrationResults.Errors += $error
            }
        }
    }
    
    # Migrate VMkernel ports (if enabled)
    if ($MigrateVmkernelPorts -and $sourceNetworkConfig.VmkernelPorts.Count -gt 0) {
        Write-Log "VMkernel port migration requires manual configuration due to IP conflicts" "Warning"
        Write-Log "The following VMkernel ports were found and require manual setup:" "Info"
        
        foreach ($vmkPort in $sourceNetworkConfig.VmkernelPorts) {
            $migrationResults.VmkernelPorts += "VMkernel '$($vmkPort.Name)' - IP: $($vmkPort.IP), Port Group: $($vmkPort.PortGroupName)"
            Write-Log "  $($vmkPort.Name): $($vmkPort.IP)/$($vmkPort.SubnetMask) on '$($vmkPort.PortGroupName)'" "Info"
        }
    }
    
    # Summary
    Write-Log "Network migration completed!" "Info"
    Write-Log "Migration Results:" "Info"
    Write-Log "  Standard vSwitches created: $($migrationResults.StandardSwitches.Count)" "Info"
    Write-Log "  Port Groups created: $($migrationResults.PortGroups.Count)" "Info"
    Write-Log "  VMkernel Ports identified: $($migrationResults.VmkernelPorts.Count)" "Info"
    Write-Log "  Errors encountered: $($migrationResults.Errors.Count)" "Info"
    
    if ($migrationResults.Errors.Count -gt 0) {
        Write-Log "Errors encountered during migration:" "Error"
        foreach ($error in $migrationResults.Errors) {
            Write-Log "  $error" "Error"
        }
    }
    
    Write-Host "SUCCESS: Network configuration migration completed"
    Write-Host "Standard vSwitches: $($migrationResults.StandardSwitches.Count)"
    Write-Host "Port Groups: $($migrationResults.PortGroups.Count)"
    Write-Host "VMkernel Ports: $($migrationResults.VmkernelPorts.Count)"
    Write-Host "Errors: $($migrationResults.Errors.Count)"
}
catch {
    $errorMsg = "Network migration failed: $($_.Exception.Message)"
    Write-Log $errorMsg "Error"
    Write-Error $errorMsg
    exit 1
}

finally {
    # Cleanup PowerCLI session
    if (-not $BypassModuleCheck) {
        Write-Log "Disconnecting from vCenter servers..." "Info"
        Disconnect-VIServer -Server $SourceVCenter -Confirm:$false -ErrorAction SilentlyContinue
        Disconnect-VIServer -Server $TargetVCenter -Confirm:$false -ErrorAction SilentlyContinue
        Write-Log "Disconnected from vCenter servers" "Info"
    }
}