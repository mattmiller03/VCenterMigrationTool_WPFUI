<#
.SYNOPSIS
    Imports Virtual Distributed Switch (vDS) and Port Groups using PowerCLI 13.x
.DESCRIPTION
    Connects to target vCenter and recreates vDS switches with their port groups
    from an exported configuration file.
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
    [string]$ImportPath,
    
    [Parameter()]
    [string]$DatacenterName = "",  # Target datacenter, if empty uses first available
    
    [Parameter()]
    [bool]$ValidateOnly = $false,
    
    [Parameter()]
    [bool]$OverwriteExisting = $false,
    
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
Start-ScriptLogging -ScriptName "Import-VDS" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$importStats = @{
    SwitchesCreated = 0
    SwitchesSkipped = 0
    PortGroupsCreated = 0
    PortGroupsSkipped = 0
    Errors = 0
}

try {
    Write-LogInfo "Starting vDS import process" -Category "Initialization"
    
    # Validate import file exists
    if (-not (Test-Path $ImportPath)) {
        throw "Import file not found: $ImportPath"
    }
    
    Write-LogInfo "Reading import file: $ImportPath" -Category "Import"
    $importContent = Get-Content -Path $ImportPath -Raw
    $importData = $importContent | ConvertFrom-Json
    
    Write-LogInfo "Import file contains $($importData.TotalSwitches) vDS switches and $($importData.TotalPortGroups) port groups" -Category "Import"
    
    if ($ValidateOnly) {
        Write-LogInfo "VALIDATION MODE: No changes will be made to the target vCenter" -Category "Validation"
    }
    
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
    Write-LogInfo "Connecting to target vCenter: $VCenterServer" -Category "Connection"
    $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Get target datacenter
    if ($DatacenterName) {
        $datacenter = Get-Datacenter -Name $DatacenterName -ErrorAction SilentlyContinue
        if (-not $datacenter) {
            throw "Datacenter '$DatacenterName' not found in target vCenter"
        }
    }
    else {
        $datacenter = Get-Datacenter | Select-Object -First 1
        if (-not $datacenter) {
            throw "No datacenters found in target vCenter"
        }
        Write-LogInfo "Using datacenter: $($datacenter.Name)" -Category "Import"
    }
    
    # Process each vDS
    foreach ($vdsData in $importData.VDSSwitches) {
        try {
            Write-LogInfo "Processing vDS: $($vdsData.Name)" -Category "Import"
            
            # Check if vDS already exists
            $existingVds = Get-VDSwitch -Name $vdsData.Name -ErrorAction SilentlyContinue
            
            if ($existingVds) {
                if ($OverwriteExisting -and -not $ValidateOnly) {
                    Write-LogWarning "Removing existing vDS: $($vdsData.Name)" -Category "Import"
                    Remove-VDSwitch -VDSwitch $existingVds -Confirm:$false -ErrorAction Stop
                    $existingVds = $null
                }
                else {
                    Write-LogWarning "vDS '$($vdsData.Name)' already exists - skipping" -Category "Import"
                    $importStats.SwitchesSkipped++
                    
                    # Still process port groups if vDS exists
                    $targetVds = $existingVds
                }
            }
            
            # Create vDS if it doesn't exist
            if (-not $existingVds) {
                if ($ValidateOnly) {
                    Write-LogInfo "VALIDATION: Would create vDS '$($vdsData.Name)' with $($vdsData.MaxPorts) max ports" -Category "Validation"
                    $importStats.SwitchesCreated++
                    
                    # Validate port groups
                    foreach ($pgData in $vdsData.PortGroups) {
                        Write-LogInfo "VALIDATION: Would create port group '$($pgData.Name)' with $($pgData.NumPorts) ports" -Category "Validation"
                        $importStats.PortGroupsCreated++
                    }
                    continue
                }
                else {
                    Write-LogInfo "Creating vDS: $($vdsData.Name)" -Category "Import"
                    
                    # Create the vDS with basic configuration
                    $vdsParams = @{
                        Name = $vdsData.Name
                        Location = $datacenter
                        NumUplinkPorts = if ($vdsData.NumUplinkPorts) { $vdsData.NumUplinkPorts } else { 4 }
                        Version = if ($vdsData.Version) { $vdsData.Version } else { "7.0.0" }
                        Mtu = if ($vdsData.Mtu) { $vdsData.Mtu } else { 1500 }
                        ContactName = $vdsData.ContactName
                        ContactDetails = $vdsData.ContactDetails
                        Notes = $vdsData.Notes
                    }
                    
                    $targetVds = New-VDSwitch @vdsParams -ErrorAction Stop
                    Write-LogSuccess "Created vDS: $($targetVds.Name)" -Category "Import"
                    $importStats.SwitchesCreated++
                    
                    # Configure additional vDS settings if needed
                    if ($vdsData.MaxPorts -and $vdsData.MaxPorts -ne $targetVds.MaxPorts) {
                        Set-VDSwitch -VDSwitch $targetVds -MaxPorts $vdsData.MaxPorts -Confirm:$false
                        Write-LogInfo "Set MaxPorts to $($vdsData.MaxPorts)" -Category "Import"
                    }
                    
                    # Configure uplink names if provided
                    if ($vdsData.UplinkPortNames -and $vdsData.UplinkPortNames.Count -gt 0) {
                        $uplinkPolicy = New-Object VMware.VimAutomation.Vds.Types.V1.VDUplinkPortPolicy
                        $uplinkPolicy.UplinkPortName = $vdsData.UplinkPortNames
                        Set-VDSwitch -VDSwitch $targetVds -UplinkPortPolicy $uplinkPolicy -Confirm:$false
                        Write-LogInfo "Configured uplink port names" -Category "Import"
                    }
                }
            }
            
            # Process port groups
            if ($targetVds -and -not $ValidateOnly) {
                foreach ($pgData in $vdsData.PortGroups) {
                    try {
                        Write-LogInfo "Processing port group: $($pgData.Name)" -Category "Import"
                        
                        # Check if port group already exists
                        $existingPg = Get-VDPortgroup -VDSwitch $targetVds -Name $pgData.Name -ErrorAction SilentlyContinue
                        
                        if ($existingPg) {
                            if ($OverwriteExisting) {
                                Write-LogWarning "Removing existing port group: $($pgData.Name)" -Category "Import"
                                Remove-VDPortgroup -VDPortgroup $existingPg -Confirm:$false -ErrorAction Stop
                                $existingPg = $null
                            }
                            else {
                                Write-LogWarning "Port group '$($pgData.Name)' already exists - skipping" -Category "Import"
                                $importStats.PortGroupsSkipped++
                                continue
                            }
                        }
                        
                        # Create port group
                        Write-LogInfo "Creating port group: $($pgData.Name)" -Category "Import"
                        
                        $pgParams = @{
                            VDSwitch = $targetVds
                            Name = $pgData.Name
                            NumPorts = if ($pgData.NumPorts) { $pgData.NumPorts } else { 128 }
                            PortBinding = if ($pgData.PortBinding) { $pgData.PortBinding } else { "Static" }
                            Notes = $pgData.Notes
                        }
                        
                        # Configure VLAN
                        if ($pgData.VlanConfiguration) {
                            switch ($pgData.VlanConfiguration.Type) {
                                "VLAN" {
                                    if ($pgData.VlanConfiguration.VlanId -gt 0) {
                                        $pgParams.VlanId = $pgData.VlanConfiguration.VlanId
                                        Write-LogDebug "Setting VLAN ID: $($pgData.VlanConfiguration.VlanId)" -Category "Import"
                                    }
                                }
                                "Trunk" {
                                    if ($pgData.VlanConfiguration.VlanTrunkRange) {
                                        # Convert trunk range to string format "1-4094"
                                        $trunkRange = $pgData.VlanConfiguration.VlanTrunkRange -join ","
                                        $pgParams.VlanTrunkRange = $trunkRange
                                        Write-LogDebug "Setting VLAN Trunk Range: $trunkRange" -Category "Import"
                                    }
                                }
                                "PrivateVLAN" {
                                    if ($pgData.VlanConfiguration.PrivateVlanId -gt 0) {
                                        # Private VLAN requires additional configuration
                                        Write-LogWarning "Private VLAN configuration not fully implemented" -Category "Import"
                                    }
                                }
                            }
                        }
                        
                        $newPg = New-VDPortgroup @pgParams -ErrorAction Stop
                        Write-LogSuccess "Created port group: $($newPg.Name)" -Category "Import"
                        
                        # Configure advanced port group settings
                        $pgSpec = New-Object VMware.Vim.DVPortgroupConfigSpec
                        $pgSpec.ConfigVersion = $newPg.ExtensionData.Config.ConfigVersion
                        
                        # Security Policy
                        if ($pgData.SecurityPolicy) {
                            $pgSpec.DefaultPortConfig = New-Object VMware.Vim.VMwareDVSPortSetting
                            $pgSpec.DefaultPortConfig.SecurityPolicy = New-Object VMware.Vim.DVSSecurityPolicy
                            
                            $pgSpec.DefaultPortConfig.SecurityPolicy.AllowPromiscuous = New-Object VMware.Vim.BoolPolicy
                            $pgSpec.DefaultPortConfig.SecurityPolicy.AllowPromiscuous.Value = $pgData.SecurityPolicy.AllowPromiscuous
                            $pgSpec.DefaultPortConfig.SecurityPolicy.AllowPromiscuous.Inherited = $false
                            
                            $pgSpec.DefaultPortConfig.SecurityPolicy.ForgedTransmits = New-Object VMware.Vim.BoolPolicy
                            $pgSpec.DefaultPortConfig.SecurityPolicy.ForgedTransmits.Value = $pgData.SecurityPolicy.ForgedTransmits
                            $pgSpec.DefaultPortConfig.SecurityPolicy.ForgedTransmits.Inherited = $false
                            
                            $pgSpec.DefaultPortConfig.SecurityPolicy.MacChanges = New-Object VMware.Vim.BoolPolicy
                            $pgSpec.DefaultPortConfig.SecurityPolicy.MacChanges.Value = $pgData.SecurityPolicy.MacChanges
                            $pgSpec.DefaultPortConfig.SecurityPolicy.MacChanges.Inherited = $false
                            
                            $newPg.ExtensionData.ReconfigureDVPortgroup($pgSpec)
                            Write-LogDebug "Configured security policy for port group: $($pgData.Name)" -Category "Import"
                        }
                        
                        # Auto-expand setting
                        if ($pgData.AutoExpand -eq $true) {
                            $pgSpec = New-Object VMware.Vim.DVPortgroupConfigSpec
                            $pgSpec.ConfigVersion = $newPg.ExtensionData.Config.ConfigVersion
                            $pgSpec.AutoExpand = $true
                            $newPg.ExtensionData.ReconfigureDVPortgroup($pgSpec)
                            Write-LogDebug "Enabled auto-expand for port group: $($pgData.Name)" -Category "Import"
                        }
                        
                        $importStats.PortGroupsCreated++
                        
                    } catch {
                        Write-LogError "Failed to create port group '$($pgData.Name)': $($_.Exception.Message)" -Category "Error"
                        $importStats.Errors++
                    }
                }
            }
            
        } catch {
            Write-LogError "Failed to process vDS '$($vdsData.Name)': $($_.Exception.Message)" -Category "Error"
            $importStats.Errors++
        }
    }
    
    # Generate summary
    $scriptSuccess = ($importStats.Errors -eq 0)
    
    if ($ValidateOnly) {
        $finalSummary = "Validation complete: Would create $($importStats.SwitchesCreated) switches and $($importStats.PortGroupsCreated) port groups"
    }
    else {
        $finalSummary = "Import complete: Created $($importStats.SwitchesCreated) switches and $($importStats.PortGroupsCreated) port groups"
        if ($importStats.SwitchesSkipped -gt 0 -or $importStats.PortGroupsSkipped -gt 0) {
            $finalSummary += " (Skipped: $($importStats.SwitchesSkipped) switches, $($importStats.PortGroupsSkipped) port groups)"
        }
        if ($importStats.Errors -gt 0) {
            $finalSummary += " - $($importStats.Errors) errors occurred"
        }
    }
    
    # Output summary for the application
    if ($scriptSuccess) {
        Write-Output "SUCCESS: $finalSummary"
    }
    else {
        Write-Output "WARNING: $finalSummary"
    }
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Import failed: $($_.Exception.Message)"
    Write-LogError "Import failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        Disconnect-VIServer -Server $viConnection -Confirm:$false -ErrorAction SilentlyContinue
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $importStats
}