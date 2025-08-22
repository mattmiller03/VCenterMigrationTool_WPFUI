# Get-Clusters.ps1 - Retrieves cluster information from vCenter
param(
    [string]$VCenterServer,
    [System.Management.Automation.PSCredential]$Credentials,
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
Start-ScriptLogging -ScriptName "Get-Clusters" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

# Initialize result
$result = @()
$scriptSuccess = $true

try {
    Write-LogInfo "Starting cluster discovery" -Category "Initialization"
    
    # Handle PowerCLI module loading based on bypass setting
    if ($BypassModuleCheck) {
        Write-LogInfo "BypassModuleCheck is enabled - skipping PowerCLI module import" -Category "Module"
        
        # Quick check if PowerCLI commands are available
        if (-not (Get-Command "Get-VIServer" -ErrorAction SilentlyContinue)) {
            Write-LogError "PowerCLI commands not available but BypassModuleCheck is enabled" -Category "Module"
            throw "PowerCLI commands are required. Either import PowerCLI modules first or set BypassModuleCheck to false."
        }
        
        Write-LogSuccess "PowerCLI commands are available" -Category "Module"
    }
    else {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
            Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)" -Category "Module"
            throw "PowerCLI modules are required but could not be imported: $($_.Exception.Message)"
        }
    }
    
    # Connect to vCenter (scripts run in isolated sessions, so no existing connections available)
    Write-LogInfo "Establishing vCenter connection..." -Category "Connection"
    
    if (-not $VCenterServer -or -not $Credentials) {
        $errorMsg = "vCenter connection parameters are required (VCenterServer, Credentials) since scripts run in isolated sessions."
        Write-LogCritical $errorMsg -Category "Connection"
        throw $errorMsg
    }
    
    try {
        Write-LogInfo "Connecting to vCenter: $VCenterServer" -Category "Connection"
        $connectionUsed = Connect-VIServer -Server $VCenterServer -Credential $Credentials -ErrorAction Stop
        Write-LogSuccess "Successfully connected to vCenter: $($connectionUsed.Name)" -Category "Connection"
        Write-LogInfo "  Server: $($connectionUsed.Name)" -Category "Connection"
        Write-LogInfo "  User: $($connectionUsed.User)" -Category "Connection"
        Write-LogInfo "  Version: $($connectionUsed.Version)" -Category "Connection"
    }
    catch {
        Write-LogError "Failed to connect to vCenter $VCenterServer : $($_.Exception.Message)" -Category "Connection"
        throw "Failed to establish vCenter connection: $($_.Exception.Message)"
    }
    
    # Retrieve clusters from vCenter with timeout
    Write-LogInfo "Retrieving clusters from vCenter..." -Category "Discovery"
    
    try {
        # Use a simple Get-Cluster call with timeout
        $clusters = Get-Cluster -ErrorAction Stop
        
        if ($clusters) {
            $clusterCount = if ($clusters.Count) { $clusters.Count } else { 1 }
            Write-LogSuccess "Found $clusterCount cluster(s)" -Category "Discovery"
            
            # Process clusters - get comprehensive information
            foreach ($cluster in @($clusters)) {
                Write-LogInfo "Processing cluster: $($cluster.Name)" -Category "Discovery"
                
                try {
                    # Get cluster view for more detailed information
                    $clusterView = Get-View -VIObject $cluster -ErrorAction SilentlyContinue
                    
                    # Get hosts and VMs information
                    $hosts = Get-VMHost -Location $cluster -ErrorAction SilentlyContinue
                    $vms = Get-VM -Location $cluster -ErrorAction SilentlyContinue
                    $datastores = Get-Datastore -Location $cluster -ErrorAction SilentlyContinue
                    
                    # Calculate totals
                    $totalCpuGhz = if ($hosts) { ($hosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000 } else { 0 }
                    $totalMemoryGB = if ($hosts) { ($hosts | Measure-Object -Property MemoryTotalGB -Sum).Sum } else { 0 }
                    
                    # Get datacenter name
                    $datacenter = Get-Datacenter -Cluster $cluster -ErrorAction SilentlyContinue
                    $datacenterName = if ($datacenter) { $datacenter.Name } else { "" }
                    
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = if ($hosts) { $hosts.Count } else { 0 }
                        VmCount = if ($vms) { $vms.Count } else { 0 }
                        DatastoreCount = if ($datastores) { $datastores.Count } else { 0 }
                        TotalCpuGhz = [math]::Round($totalCpuGhz, 1)
                        TotalMemoryGB = [math]::Round($totalMemoryGB, 0)
                        HAEnabled = $cluster.HAEnabled
                        DrsEnabled = $cluster.DrsEnabled
                        EVCMode = if ($cluster.EVCMode) { $cluster.EVCMode } else { "" }
                        DatacenterName = $datacenterName
                        FullName = if ($datacenterName) { "$datacenterName/$($cluster.Name)" } else { $cluster.Name }
                        Hosts = @() # Empty array for now - can be populated later if needed
                    }
                    
                    Write-LogInfo "  Hosts: $($clusterInfo.HostCount), VMs: $($clusterInfo.VmCount), CPU: $($clusterInfo.TotalCpuGhz) GHz, Memory: $($clusterInfo.TotalMemoryGB) GB" -Category "Discovery"
                }
                catch {
                    Write-LogWarning "Failed to get detailed info for cluster $($cluster.Name): $($_.Exception.Message)" -Category "Discovery"
                    
                    # Fallback to basic information
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = 0
                        VmCount = 0
                        DatastoreCount = 0
                        TotalCpuGhz = 0.0
                        TotalMemoryGB = 0.0
                        HAEnabled = $cluster.HAEnabled
                        DrsEnabled = $cluster.DrsEnabled
                        EVCMode = if ($cluster.EVCMode) { $cluster.EVCMode } else { "" }
                        DatacenterName = ""
                        FullName = $cluster.Name
                        Hosts = @()
                    }
                }
                
                $result += $clusterInfo
            }
            
            Write-LogSuccess "Successfully processed $($result.Count) cluster(s)" -Category "Discovery"
        }
        else {
            Write-LogWarning "No clusters found in vCenter" -Category "Discovery"
        }
    }
    catch {
        Write-LogError "Failed to retrieve clusters: $($_.Exception.Message)" -Category "Discovery"
        throw "Cluster retrieval failed: $($_.Exception.Message)"
    }
    
    Write-LogSuccess "Cluster discovery completed - found $($result.Count) cluster(s)" -Category "Summary"
}
catch {
    $scriptSuccess = $false
    $errorMessage = "Cluster discovery failed: $($_.Exception.Message)"
    Write-LogCritical $errorMessage -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
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
    # Stop logging
    if ($scriptSuccess) {
        Stop-ScriptLogging -Success $true -Summary "Retrieved $($result.Count) clusters"
        
        # Output result as JSON
        Write-Output ($result | ConvertTo-Json -Depth 3 -Compress)
    }
}