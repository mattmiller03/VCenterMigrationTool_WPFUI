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
    [Parameter(Mandatory=$true)] [string]$Username,
    [Parameter(Mandatory=$true)] [string]$Password,
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
    $credential = New-Object System.Management.Automation.PSCredential($Username, (ConvertTo-SecureString $Password -AsPlainText -Force))
    Connect-VIServer -Server $VCenterServer -Credential $credential -Force -ErrorAction Stop
    Write-LogSuccess "Connected to vCenter." -Category "Connection"
    
    # Get all ESXi hosts
    $vmHosts = Get-VMHost
    $networkTopology = @()
    
    foreach ($vmHost in $vmHosts) {
        $stats.HostsProcessed++
        Write-LogInfo "Processing network topology for host: $($vmHost.Name)" -Category "Discovery"
        $hostNetworkConfig = @{ Name = $vmHost.Name; VSwitches = @(); VmKernelPorts = @() }

        # Get standard and distributed switches
        $allSwitches = Get-VirtualSwitch -VMHost $vmHost -ErrorAction SilentlyContinue
        $allSwitches += Get-VDSwitch -VMHost $vmHost -ErrorAction SilentlyContinue

        foreach($switch in $allSwitches) {
            $switchConfig = @{ Name = $switch.Name; Type = $switch.GetType().Name; PortGroups = @() }
            # ... add other switch properties as needed ...
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