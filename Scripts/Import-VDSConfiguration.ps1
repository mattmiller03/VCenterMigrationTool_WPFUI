<#
.SYNOPSIS
    Imports Virtual Distributed Switch (VDS) configurations from JSON or CSV format.
.DESCRIPTION
    Connects to vCenter and recreates VDS configurations from exported files.
    Supports both JSON and CSV import formats and can handle both distributed and standard switches.
.NOTES
    Version: 1.0 - Initial VDS import implementation
#>
param(
    [Parameter(Mandatory=$true)] [string]$VCenterServer,
    [Parameter(Mandatory=$true)] [System.Management.Automation.PSCredential]$Credentials,
    [Parameter(Mandatory=$true)] [string]$ImportFilePath,
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
Start-ScriptLogging -ScriptName "Import-VDSConfiguration" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$stats = @{ "VDSSwitchesCreated" = 0; "StandardSwitchesCreated" = 0; "PortGroupsCreated" = 0; "Errors" = 0 }

try {
    Write-LogInfo "Starting VDS configuration import..." -Category "Initialization"
    
    # Validate import file
    if (-not (Test-Path $ImportFilePath)) {
        throw "Import file not found: $ImportFilePath"
    }
    
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
    
    # Load configuration data
    Write-LogInfo "Loading configuration from: $ImportFilePath" -Category "Import"
    $importData = @()
    
    if ($ImportFilePath -like "*.json") {
        $importData = Get-Content $ImportFilePath -Raw | ConvertFrom-Json
    } elseif ($ImportFilePath -like "*.csv") {
        $csvData = Import-Csv $ImportFilePath
        # Convert CSV back to hierarchical structure
        $switchGroups = $csvData | Group-Object SwitchName
        foreach ($group in $switchGroups) {
            $switch = $group.Group[0]
            $portGroups = $group.Group | ForEach-Object {
                @{
                    Name = $_.PortGroupName
                    VlanId = [int]$_.VlanId
                    VlanType = $_.VlanType
                    NumPorts = if ($_.NumPorts) { [int]$_.NumPorts } else { 128 }
                }
            }
            
            $importData += @{
                Type = $switch.SwitchType
                Name = $switch.SwitchName
                HostName = $switch.HostName
                Version = $switch.Version
                Mtu = if ($switch.Mtu) { [int]$switch.Mtu } else { 1500 }
                PortGroups = $portGroups
            }
        }
    } else {
        throw "Unsupported file format. Please use .json or .csv files."
    }
    
    Write-LogInfo "Loaded configuration for $($importData.Count) switches" -Category "Import"
    
    if ($ValidateOnly) {
        Write-LogInfo "VALIDATION ONLY mode. No changes will be made." -Category "Validation"
        $scriptSuccess = $true
        $finalSummary = "Validation completed. Configuration file contains $($importData.Count) switches."
        return
    }
    
    # Get target hosts for switch assignment
    $targetHosts = Get-VMHost
    if ($targetHosts.Count -eq 0) {
        throw "No hosts found in target vCenter"
    }
    
    # Import switches
    foreach ($switchConfig in $importData) {
        try {
            if ($switchConfig.Type -eq "DistributedSwitch") {
                # Import distributed switch
                Write-LogInfo "Processing distributed switch: $($switchConfig.Name)" -Category "Import"
                
                $existingVds = Get-VDSwitch -Name $switchConfig.Name -ErrorAction SilentlyContinue
                if ($existingVds) {
                    if ($RecreateIfExists) {
                        Write-LogInfo "Removing existing VDS '$($switchConfig.Name)' before recreating." -Category "Import"
                        Remove-VDSwitch -VDSwitch $existingVds -Confirm:$false
                    } else {
                        Write-LogInfo "VDS '$($switchConfig.Name)' already exists, skipping." -Category "Import"
                        continue
                    }
                }
                
                # Create the distributed switch
                Write-LogInfo "Creating distributed switch: $($switchConfig.Name)" -Category "Import"
                $datacenter = Get-Datacenter | Select-Object -First 1
                $newVds = New-VDSwitch -Name $switchConfig.Name -Location $datacenter
                
                # Configure VDS settings
                $setParams = @{
                    VDSwitch = $newVds
                }
                if ($switchConfig.Mtu -and $switchConfig.Mtu -gt 0) { $setParams.Mtu = $switchConfig.Mtu }
                if ($switchConfig.MaxPorts -and $switchConfig.MaxPorts -gt 0) { $setParams.MaxPorts = $switchConfig.MaxPorts }
                if ($switchConfig.NumStandalonePorts -and $switchConfig.NumStandalonePorts -ge 0) { $setParams.NumStandalonePorts = $switchConfig.NumStandalonePorts }
                
                if ($setParams.Keys.Count -gt 1) {
                    Set-VDSwitch @setParams -ErrorAction SilentlyContinue
                }
                
                # Add all hosts to the distributed switch
                foreach ($targetHost in $targetHosts) {
                    try {
                        Write-LogInfo "Adding host '$($targetHost.Name)' to VDS '$($switchConfig.Name)'" -Category "Import"
                        Add-VDSwitchVMHost -VDSwitch $newVds -VMHost $targetHost -ErrorAction Stop
                    } catch {
                        Write-LogWarning "Failed to add host '$($targetHost.Name)' to VDS: $($_.Exception.Message)" -Category "Import"
                    }
                }
                
                $stats.VDSSwitchesCreated++
                
                # Create distributed port groups
                foreach ($portGroup in $switchConfig.PortGroups) {
                    try {
                        $targetPortGroupName = $NetworkMappings[$portGroup.Name] ?? $portGroup.Name
                        
                        if (Get-VDPortgroup -VDSwitch $newVds -Name $targetPortGroupName -ErrorAction SilentlyContinue) {
                            Write-LogInfo "Distributed port group '$targetPortGroupName' already exists, skipping." -Category "Import"
                            continue
                        }
                        
                        Write-LogInfo "Creating distributed port group: $targetPortGroupName" -Category "Import"
                        $numPorts = if ($portGroup.NumPorts -and $portGroup.NumPorts -gt 0) { $portGroup.NumPorts } else { 128 }
                        $vlanId = if ($portGroup.VlanId -and $portGroup.VlanId -gt 0) { $portGroup.VlanId } else { 0 }
                        
                        New-VDPortgroup -VDSwitch $newVds -Name $targetPortGroupName -NumPorts $numPorts -VlanId $vlanId
                        $stats.PortGroupsCreated++
                        
                    } catch {
                        Write-LogError "Failed to create distributed port group '$($portGroup.Name)': $($_.Exception.Message)" -Category "Import"
                        $stats.Errors++
                    }
                }
                
            } elseif ($switchConfig.Type -eq "StandardSwitch") {
                # Import standard switch
                Write-LogInfo "Processing standard switch: $($switchConfig.Name)" -Category "Import"
                
                $targetHost = $targetHosts | Where-Object { $_.Name -eq $switchConfig.HostName } | Select-Object -First 1
                if (-not $targetHost) {
                    $targetHost = $targetHosts[0]
                    Write-LogWarning "Host '$($switchConfig.HostName)' not found, using '$($targetHost.Name)' instead." -Category "Import"
                }
                
                $existingSwitch = Get-VirtualSwitch -VMHost $targetHost -Name $switchConfig.Name -Standard -ErrorAction SilentlyContinue
                if ($existingSwitch) {
                    if ($RecreateIfExists) {
                        Write-LogInfo "Removing existing standard switch '$($switchConfig.Name)' before recreating." -Category "Import"
                        Remove-VirtualSwitch -VirtualSwitch $existingSwitch -Confirm:$false
                    } else {
                        Write-LogInfo "Standard switch '$($switchConfig.Name)' already exists on host '$($targetHost.Name)', skipping." -Category "Import"
                        continue
                    }
                }
                
                Write-LogInfo "Creating standard switch '$($switchConfig.Name)' on host '$($targetHost.Name)'" -Category "Import"
                $numPorts = if ($switchConfig.NumPorts -and $switchConfig.NumPorts -gt 0) { $switchConfig.NumPorts } else { 128 }
                $mtu = if ($switchConfig.Mtu -and $switchConfig.Mtu -gt 0) { $switchConfig.Mtu } else { 1500 }
                
                $newSwitch = New-VirtualSwitch -VMHost $targetHost -Name $switchConfig.Name -NumPorts $numPorts -Mtu $mtu
                $stats.StandardSwitchesCreated++
                
                # Create standard port groups
                foreach ($portGroup in $switchConfig.PortGroups) {
                    try {
                        $targetPortGroupName = $NetworkMappings[$portGroup.Name] ?? $portGroup.Name
                        
                        if (Get-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -ErrorAction SilentlyContinue) {
                            Write-LogInfo "Standard port group '$targetPortGroupName' already exists, skipping." -Category "Import"
                            continue
                        }
                        
                        Write-LogInfo "Creating standard port group: $targetPortGroupName" -Category "Import"
                        $vlanId = if ($portGroup.VlanId -and $portGroup.VlanId -gt 0) { $portGroup.VlanId } else { 0 }
                        
                        New-VirtualPortGroup -VMHost $targetHost -Name $targetPortGroupName -VirtualSwitch $newSwitch -VLanId $vlanId
                        $stats.PortGroupsCreated++
                        
                    } catch {
                        Write-LogError "Failed to create standard port group '$($portGroup.Name)': $($_.Exception.Message)" -Category "Import"
                        $stats.Errors++
                    }
                }
            }
            
        } catch {
            Write-LogError "Failed to process switch '$($switchConfig.Name)': $($_.Exception.Message)" -Category "Import"
            $stats.Errors++
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "VDS configuration import completed. Created $($stats.VDSSwitchesCreated) VDS, $($stats.StandardSwitchesCreated) standard switches, and $($stats.PortGroupsCreated) port groups."

} catch {
    $scriptSuccess = $false
    $finalSummary = "VDS configuration import failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary
    Write-LogError "Stack Trace: $($_.ScriptStackTrace)"
    throw $_
} finally {
    Write-LogInfo "Disconnecting from vCenter server..." -Category "Cleanup"
    Disconnect-VIServer -Server $VCenterServer -Confirm:$false -ErrorAction SilentlyContinue
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $stats
}

# Output import summary
Write-Output "Import completed: $($stats.VDSSwitchesCreated) VDS, $($stats.StandardSwitchesCreated) standard switches, $($stats.PortGroupsCreated) port groups created"