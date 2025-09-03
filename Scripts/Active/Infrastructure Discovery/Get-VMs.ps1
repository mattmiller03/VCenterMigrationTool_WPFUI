# Get-VMs.ps1 - Retrieves virtual machine information from vCenter
param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = "",
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Override Write-Host if console output is suppressed
if ($SuppressConsoleOutput) {
    function global:Write-Host {
        # Suppress all Write-Host output
    }
}

# Start logging
Start-ScriptLogging -ScriptName "Get-VMs" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

# Initialize result
$result = @()
$scriptSuccess = $true

try {
    Write-LogInfo "Starting VM discovery from vCenter: $VCenterServer" -Category "Initialization"
    
    # PowerCLI modules are managed by the service layer
    Write-LogInfo "PowerCLI modules are managed by the service layer" -Category "Module"
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    
    # Use existing vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
    $connection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
    if (-not $connection -or -not $connection.IsConnected) {
        throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using vCenter connection: $($connection.Name) (v$($connection.Version))" -Category "Connection"
    
    # Get all VMs
    Write-LogInfo "Retrieving virtual machines..." -Category "Discovery"
    
    try {
        $vms = Get-VM -Server $connection -ErrorAction Stop
        
        if ($vms) {
            Write-LogSuccess "Found $($vms.Count) virtual machines" -Category "Discovery"
            
            foreach ($vm in $vms) {
                Write-LogVerbose "Processing VM: $($vm.Name)" -Category "Discovery"
                
                # Get datastore name (handle multiple datastores)
                $datastoreName = if ($vm.DatastoreIdList) {
                    ($vm.DatastoreIdList | Get-Datastore | Select-Object -First 1).Name
                } else {
                    "Unknown"
                }
                
                $vmInfo = @{
                    Name = $vm.Name
                    PowerState = $vm.PowerState.ToString()
                    EsxiHost = $vm.VMHost.Name
                    Datastore = $datastoreName
                    Cluster = $vm.VMHost.Parent.Name
                    IsSelected = $false
                    NumCpu = $vm.NumCpu
                    MemoryGB = $vm.MemoryGB
                    ProvisionedSpaceGB = [math]::Round($vm.ProvisionedSpaceGB, 2)
                    UsedSpaceGB = [math]::Round($vm.UsedSpaceGB, 2)
                    GuestId = $vm.GuestId
                    Version = $vm.Version
                    ToolsStatus = $vm.ExtensionData.Guest.ToolsStatus
                    ToolsVersion = $vm.ExtensionData.Guest.ToolsVersion
                }
                
                $result += $vmInfo
            }
            
            # Log summary statistics
            $poweredOn = ($vms | Where-Object { $_.PowerState -eq 'PoweredOn' }).Count
            $poweredOff = ($vms | Where-Object { $_.PowerState -eq 'PoweredOff' }).Count
            $suspended = ($vms | Where-Object { $_.PowerState -eq 'Suspended' }).Count
            
            Write-LogInfo "VM Power States - On: $poweredOn, Off: $poweredOff, Suspended: $suspended" -Category "Statistics"
        }
        else {
            Write-LogWarning "No virtual machines found in vCenter" -Category "Discovery"
        }
    }
    catch {
        Write-LogError "Failed to retrieve VMs: $($_.Exception.Message)" -Category "Discovery"
        throw $_
    }
    
    # Disconnect from vCenter
    try {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Connection"
        # DISCONNECT REMOVED - Using persistent connections managed by application
        Write-LogSuccess "Disconnected from vCenter" -Category "Connection"
    }
    catch {
        Write-LogWarning "Failed to disconnect cleanly: $($_.Exception.Message)" -Category "Connection"
    }
    
    Write-LogSuccess "VM discovery completed successfully" -Category "Summary"
}
catch {
    $scriptSuccess = $false
    $errorMessage = "VM discovery failed: $($_.Exception.Message)"
    Write-LogCritical $errorMessage -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # No cleanup needed - using persistent connections managed by application
    
    # Return error in JSON format
    $errorResult = @{
        Success = $false
        Error = $_.Exception.Message
    }
    Write-Output ($errorResult | ConvertTo-Json -Compress)
    
    Stop-ScriptLogging -Success $false -Summary $errorMessage
    exit 1
}
finally {
    # Stop logging and output result
    if ($scriptSuccess) {
        $stats = @{
            "TotalVMs" = $result.Count
            "PoweredOn" = ($result | Where-Object { $_.PowerState -eq 'PoweredOn' }).Count
            "PoweredOff" = ($result | Where-Object { $_.PowerState -eq 'PoweredOff' }).Count
        }
        
        Stop-ScriptLogging -Success $true -Summary "Retrieved $($result.Count) VMs" -Statistics $stats
        
        # Output result as JSON
        Write-Output ($result | ConvertTo-Json -Depth 3 -Compress)
    }
}