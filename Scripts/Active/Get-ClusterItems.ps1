<#
.SYNOPSIS
    Retrieves key items (Resource Pools, VDS) from a vCenter cluster.
.DESCRIPTION
    Connects to vCenter, finds a specific cluster, and lists its Resource Pools and
    Virtual Distributed Switches. Returns data in JSON format.
    Requires Write-ScriptLog.ps1 in the same directory.
.NOTES
    Version: 2.0 (Integrated with standard logging)
#>
param(
    [string]$VCenterServer,
    [string]$ClusterName,
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = "",
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# --- Main Script Logic ---
Start-ScriptLogging -ScriptName "Get-ClusterItems" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$viConnection = $null
$result = @()
$connectionUsed = $null

try {
    Write-LogInfo "Starting cluster items discovery" -Category "Initialization"
    
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
    
    # Set PowerCLI configuration (modules managed by service layer)
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Connect to vCenter (scripts run in isolated sessions, so no existing connections available)
    Write-LogInfo "Establishing vCenter connection..." -Category "Connection"
    
    if (-not $VCenterServer -or -not $Username -or -not $Password) {
        $errorMsg = "vCenter connection parameters are required (VCenterServer, Username, Password) since scripts run in isolated sessions."
        Write-LogCritical $errorMsg -Category "Connection"
        throw $errorMsg
    }
    
    try {
        # Use existing vCenter connection established by PersistentVcenterConnectionService
        Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
        $viConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
        if (-not $viConnection -or -not $viConnection.IsConnected) {
            throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
        }
        $connectionUsed = $viConnection
        Write-LogSuccess "Using vCenter connection: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    }
    catch {
        Write-LogError "Failed to connect to vCenter $VCenterServer : $($_.Exception.Message)" -Category "Connection"
        throw "Failed to establish vCenter connection: $($_.Exception.Message)"
    }

    # Get Cluster if specified
    $cluster = $null
    if ($ClusterName) {
        Write-LogInfo "Retrieving cluster '$ClusterName'..." -Category "Discovery"
        try {
            $cluster = Get-Cluster -Name $ClusterName -ErrorAction Stop
            Write-LogSuccess "Found cluster '$($cluster.Name)'" -Category "Discovery"
        }
        catch {
            Write-LogError "Cluster '$ClusterName' not found: $($_.Exception.Message)" -Category "Discovery"
            throw "Cluster '$ClusterName' not found"
        }
    }
    else {
        Write-LogInfo "No cluster specified - retrieving items from all clusters" -Category "Discovery"
    }

    # Get cluster items with enhanced error handling
    Write-LogInfo "Retrieving items from cluster..." -Category "Discovery"
    
    # Get Resource Pools
    try {
        if ($cluster) {
            $resourcePools = $cluster | Get-ResourcePool -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "Resources" }
        } else {
            $resourcePools = Get-ResourcePool -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "Resources" }
        }
        
        if ($resourcePools) {
            Write-LogSuccess "Found $($resourcePools.Count) resource pools" -Category "Discovery"
            foreach ($rp in $resourcePools) {
                $result += @{
                    Id = $rp.Id
                    Name = $rp.Name
                    Type = "ResourcePool"
                    Path = "/ResourcePools/$($rp.Name)"
                    ItemCount = 0
                    IsSelected = $true
                    Status = "Ready"
                }
            }
        } else {
            Write-LogInfo "No resource pools found" -Category "Discovery"
        }
    }
    catch {
        Write-LogWarning "Error retrieving resource pools: $($_.Exception.Message)" -Category "Discovery"
    }
    
    # Get Virtual Distributed Switches
    try {
        if ($cluster) {
            $vdSwitches = $cluster | Get-VMHost | Get-VDSwitch -ErrorAction SilentlyContinue | Select-Object -Unique
        } else {
            $vdSwitches = Get-VDSwitch -ErrorAction SilentlyContinue
        }
        
        if ($vdSwitches) {
            Write-LogSuccess "Found $($vdSwitches.Count) virtual distributed switches" -Category "Discovery"
            foreach ($vds in $vdSwitches) {
                $result += @{
                    Id = $vds.Id
                    Name = $vds.Name
                    Type = "VDS"
                    Path = "/VDS/$($vds.Name)"
                    ItemCount = ($vds | Get-VDPortgroup -ErrorAction SilentlyContinue).Count
                    IsSelected = $true
                    Status = "Ready"
                }
            }
        } else {
            Write-LogInfo "No virtual distributed switches found" -Category "Discovery"
        }
    }
    catch {
        Write-LogWarning "Error retrieving virtual distributed switches: $($_.Exception.Message)" -Category "Discovery"
    }
    
    # Get VM Folders (Note: Folders are datacenter-level, not cluster-level)
    try {
        if ($cluster) {
            # Get the datacenter that contains this cluster
            $datacenter = Get-Datacenter -Cluster $cluster -ErrorAction SilentlyContinue
            if ($datacenter) {
                $vmFolders = Get-Folder -Type VM -Location $datacenter -ErrorAction SilentlyContinue | Where-Object { 
                    $_.Name -ne "vm" -and $_.Name -ne "Datacenters" 
                }
            } else {
                # Fallback to all folders if datacenter can't be determined
                $vmFolders = Get-Folder -Type VM -ErrorAction SilentlyContinue | Where-Object { 
                    $_.Name -ne "vm" -and $_.Name -ne "Datacenters" 
                }
            }
        } else {
            $vmFolders = Get-Folder -Type VM -ErrorAction SilentlyContinue | Where-Object { 
                $_.Name -ne "vm" -and $_.Name -ne "Datacenters" 
            }
        }
        
        if ($vmFolders) {
            Write-LogSuccess "Found $($vmFolders.Count) VM folders" -Category "Discovery"
            foreach ($folder in $vmFolders) {
                $result += @{
                    Id = $folder.Id
                    Name = $folder.Name
                    Type = "Folder"
                    Path = "/vm/$($folder.Name)"
                    ItemCount = ($folder | Get-ChildItem -ErrorAction SilentlyContinue).Count
                    IsSelected = $true
                    Status = "Ready"
                }
            }
        } else {
            Write-LogInfo "No VM folders found" -Category "Discovery"
        }
    }
    catch {
        Write-LogWarning "Error retrieving VM folders: $($_.Exception.Message)" -Category "Discovery"
    }

    $scriptSuccess = $true
    $finalSummary = "Successfully retrieved $($result.Count) items" + $(if ($ClusterName) { " from cluster '$ClusterName'" } else { " from vCenter" })

}
catch {
    $scriptSuccess = $false
    $finalSummary = "Cluster items discovery failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Return error in JSON format
    $errorResult = @{
        Success = $false
        Error = $_.Exception.Message
    }
    Write-Output ($errorResult | ConvertTo-Json -Compress)
    
    Stop-ScriptLogging -Success $false -Summary $finalSummary
    exit 1
}
finally {
    # Always disconnect since we created our own connection
    if ($viConnection -and $viConnection.IsConnected) {
        try {
            Write-LogInfo "Disconnecting from vCenter..." -Category "Connection"
            # DISCONNECT REMOVED - Using persistent connections managed by application
            Write-LogSuccess "Disconnected from vCenter" -Category "Connection"
        }
        catch {
            Write-LogWarning "Failed to disconnect cleanly: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Stop logging and output result
    if ($scriptSuccess) {
        $stats = @{
            "ClusterName" = if ($ClusterName) { $ClusterName } else { "All Clusters" }
            "TotalItems" = $result.Count
            "ResourcePools" = ($result | Where-Object { $_.Type -eq "ResourcePool" }).Count
            "VDSwitches" = ($result | Where-Object { $_.Type -eq "VDS" }).Count
            "Folders" = ($result | Where-Object { $_.Type -eq "Folder" }).Count
        }
        
        Stop-ScriptLogging -Success $true -Summary $finalSummary -Statistics $stats
        
        # Always output as JSON for consistency
        Write-Output ($result | ConvertTo-Json -Depth 3 -Compress)
    }
}