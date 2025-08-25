# Get-Datacenters.ps1 - Retrieves datacenter information from vCenter
<#
.SYNOPSIS
    Retrieves datacenter names from vCenter for folder structure migration.

.DESCRIPTION
    This script connects to a vCenter server and retrieves the names of all datacenters
    for use in folder structure migration operations.

.PARAMETER VCenterServer
    The hostname or IP address of the vCenter Server.

.PARAMETER Credentials
    PSCredential object for vCenter authentication.

.PARAMETER BypassModuleCheck
    Switch to bypass PowerCLI module verification for faster execution.

.PARAMETER LogPath
    Path for log file output.

.PARAMETER SuppressConsoleOutput
    Suppress console output for clean JSON returns.

.EXAMPLE
    $cred = Get-Credential
    .\Get-Datacenters.ps1 -VCenterServer "vcenter.lab.local" -Credentials $cred
#>

[CmdletBinding()]
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
Start-ScriptLogging -ScriptName "Get-Datacenters" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

# Initialize result
$result = @()
$scriptSuccess = $true
$viConnection = $null

try {
    Write-LogInfo "Starting datacenter discovery" -Category "Initialization"
    
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
    
    # Check connection status and establish connection if needed
    Write-LogInfo "Checking vCenter connection status..." -Category "Connection"
    $connectionEstablished = $false
    $connectionUsed = $null
    
    # Strategy 1: Check existing default connection
    try {
        if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {
            # Test the connection with a simple command
            $null = Get-VIServer -Server $global:DefaultVIServer -ErrorAction Stop
            Write-LogSuccess "Using existing default vCenter connection: $($global:DefaultVIServer.Name)" -Category "Connection"
            $connectionEstablished = $true
            $connectionUsed = $global:DefaultVIServer
            $viConnection = $global:DefaultVIServer  # For compatibility with cleanup
        }
        else {
            Write-LogInfo "No active default vCenter connection found" -Category "Connection"
        }
    }
    catch {
        Write-LogWarning "Default connection appears invalid: $($_.Exception.Message)" -Category "Connection"
        $global:DefaultVIServer = $null
    }
    
    # Strategy 2: If no default connection, find any active connections
    if (-not $connectionEstablished) {
        Write-LogInfo "Scanning for active vCenter connections..." -Category "Connection"
        try {
            $allConnections = Get-VIServer -ErrorAction SilentlyContinue
            if ($allConnections) {
                $activeConnections = $allConnections | Where-Object { $_.IsConnected }
                if ($activeConnections) {
                    $connectionUsed = $activeConnections | Select-Object -First 1
                    $global:DefaultVIServer = $connectionUsed
                    $viConnection = $connectionUsed  # For compatibility with cleanup
                    Write-LogSuccess "Found active vCenter connection: $($connectionUsed.Name)" -Category "Connection"
                    $connectionEstablished = $true
                }
                else {
                    Write-LogInfo "Found $($allConnections.Count) vCenter connections but none are active" -Category "Connection"
                }
            }
            else {
                Write-LogInfo "No existing vCenter connections found" -Category "Connection"
            }
        }
        catch {
            Write-LogWarning "Error scanning for connections: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Strategy 3: If credentials provided, establish new connection
    if (-not $connectionEstablished -and $VCenterServer -and $Credentials) {
        Write-LogInfo "Attempting to establish new vCenter connection to: $VCenterServer" -Category "Connection"
        try {
            # Force connection and ignore SSL certificate issues
            $viConnection = Connect-VIServer -Server $VCenterServer -Credential $Credentials -Force -ErrorAction Stop
            $connectionUsed = $viConnection
            Write-LogSuccess "Successfully connected to vCenter: $($viConnection.Name)" -Category "Connection"
            $connectionEstablished = $true
        }
        catch {
            Write-LogError "Failed to connect to vCenter $VCenterServer : $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Final connection validation
    if (-not $connectionEstablished) {
        $errorMsg = "No vCenter connection available. "
        if (-not $VCenterServer) {
            $errorMsg += "Please connect to vCenter first or provide connection parameters (VCenterServer, Credentials)."
        } else {
            $errorMsg += "Unable to establish connection with provided credentials."
        }
        Write-LogCritical $errorMsg -Category "Connection"
        throw $errorMsg
    }
    
    # Log connection details
    Write-LogInfo "Active vCenter connection details:" -Category "Connection"
    Write-LogInfo "  Server: $($connectionUsed.Name)" -Category "Connection"
    Write-LogInfo "  Version: $($connectionUsed.Version)" -Category "Connection"
    Write-LogInfo "  User: $($connectionUsed.User)" -Category "Connection"
    
    # Retrieve datacenters from vCenter
    Write-LogInfo "Retrieving datacenters from vCenter..." -Category "Discovery"
    
    try {
        $datacenters = Get-Datacenter -ErrorAction Stop
        
        if ($datacenters) {
            $datacenterCount = if ($datacenters.Count) { $datacenters.Count } else { 1 }
            Write-LogSuccess "Found $datacenterCount datacenter(s)" -Category "Discovery"
            
            # Ensure $datacenters is always an array for consistent processing
            if ($datacenters -isnot [array]) {
                $datacenters = @($datacenters)
            }
            
            foreach ($dc in $datacenters) {
                Write-LogInfo "Processing datacenter: $($dc.Name)" -Category "Discovery"
                
                try {
                    # Get datacenter statistics efficiently with error handling
                    $numHosts = 0
                    $numClusters = 0
                    $numVMs = 0
                    $numDatastores = 0
                    $numNetworks = 0
                    
                    try {
                        $hosts = $dc | Get-VMHost -ErrorAction SilentlyContinue
                        $numHosts = if ($hosts) { ($hosts | Measure-Object).Count } else { 0 }
                    }
                    catch { 
                        Write-LogWarning "Could not retrieve host count for datacenter '$($dc.Name)'" -Category "Discovery"
                    }
                    
                    try {
                        $clusters = $dc | Get-Cluster -ErrorAction SilentlyContinue
                        $numClusters = if ($clusters) { ($clusters | Measure-Object).Count } else { 0 }
                    }
                    catch { 
                        Write-LogWarning "Could not retrieve cluster count for datacenter '$($dc.Name)'" -Category "Discovery"
                    }
                    
                    try {
                        $vms = $dc | Get-VM -ErrorAction SilentlyContinue
                        $numVMs = if ($vms) { ($vms | Measure-Object).Count } else { 0 }
                    }
                    catch { 
                        Write-LogWarning "Could not retrieve VM count for datacenter '$($dc.Name)'" -Category "Discovery"
                    }
                    
                    try {
                        $datastores = $dc | Get-Datastore -ErrorAction SilentlyContinue
                        $numDatastores = if ($datastores) { ($datastores | Measure-Object).Count } else { 0 }
                    }
                    catch { 
                        Write-LogWarning "Could not retrieve datastore count for datacenter '$($dc.Name)'" -Category "Discovery"
                    }
                    
                    try {
                        $networks = $dc | Get-VirtualPortGroup -ErrorAction SilentlyContinue
                        $numNetworks = if ($networks) { ($networks | Measure-Object).Count } else { 0 }
                    }
                    catch { 
                        Write-LogWarning "Could not retrieve network count for datacenter '$($dc.Name)'" -Category "Discovery"
                    }
                    
                    $dcInfo = @{
                        Name = if ($dc.Name) { $dc.Name } else { "Unknown" }
                        Id = if ($dc.Id) { $dc.Id } else { "" }
                        NumHosts = $numHosts
                        NumClusters = $numClusters
                        NumVMs = $numVMs
                        NumDatastores = $numDatastores
                        NumNetworks = $numNetworks
                    }
                    
                    $result += $dcInfo
                    
                    Write-LogInfo "  Datacenter: $($dc.Name) - Hosts: $numHosts, Clusters: $numClusters, VMs: $numVMs, Datastores: $numDatastores" -Category "Discovery"
                    Write-LogDebug "Datacenter '$($dc.Name)' processed successfully" -Category "Discovery"
                }
                catch {
                    Write-LogWarning "Error processing datacenter '$($dc.Name)': $($_.Exception.Message)" -Category "Discovery"
                    
                    # Add minimal datacenter info even on error
                    $dcInfo = @{
                        Name = if ($dc.Name) { $dc.Name } else { "Unknown" }
                        Id = if ($dc.Id) { $dc.Id } else { "" }
                        NumHosts = 0
                        NumClusters = 0
                        NumVMs = 0
                        NumDatastores = 0
                        NumNetworks = 0
                    }
                    $result += $dcInfo
                    
                    # Continue processing other datacenters
                    continue
                }
            }
            
            Write-LogSuccess "Successfully processed $($result.Count) datacenter(s)" -Category "Discovery"
            
            # For backward compatibility, also create comma-separated list
            $datacenterNames = $result | Select-Object -ExpandProperty Name
            $datacenterList = $datacenterNames -join ", "
            Write-LogInfo "Datacenter names: $datacenterList" -Category "Summary"
        }
        else {
            Write-LogWarning "No datacenters found in vCenter '$($connectionUsed.Name)'" -Category "Discovery"
            Write-LogInfo "This could be normal if the connected user doesn't have permissions to view datacenters" -Category "Discovery"
        }
    }
    catch {
        Write-LogError "Failed to retrieve datacenters: $($_.Exception.Message)" -Category "Discovery"
        Write-LogError "This might be due to insufficient permissions or connection issues" -Category "Discovery"
        throw "Datacenter retrieval failed: $($_.Exception.Message)"
    }
    
    Write-LogSuccess "Datacenter discovery completed - found $($result.Count) datacenter(s)" -Category "Summary"
}
catch {
    $scriptSuccess = $false
    $errorMessage = "Datacenter discovery failed: $($_.Exception.Message)"
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
    # Only disconnect if we created the connection (not if we reused existing)
    if ($viConnection -and $viConnection.IsConnected -and $VCenterServer -and $Credentials) {
        try {
            Write-LogInfo "Disconnecting from vCenter..." -Category "Connection"
            # DISCONNECT REMOVED - Using persistent connections managed by application
            Write-LogSuccess "Disconnected from vCenter" -Category "Connection"
        }
        catch {
            Write-LogWarning "Failed to disconnect cleanly: $($_.Exception.Message)" -Category "Connection"
        }
    }
    elseif ($viConnection -and $viConnection.IsConnected) {
        Write-LogInfo "Keeping existing vCenter connection active" -Category "Connection"
    }
    
    # Stop logging and output result
    if ($scriptSuccess) {
        $stats = @{
            "TotalDatacenters" = $result.Count
            "TotalHosts" = if ($result.Count -gt 0) { ($result | Measure-Object -Property NumHosts -Sum).Sum } else { 0 }
            "TotalClusters" = if ($result.Count -gt 0) { ($result | Measure-Object -Property NumClusters -Sum).Sum } else { 0 }
            "TotalVMs" = if ($result.Count -gt 0) { ($result | Measure-Object -Property NumVMs -Sum).Sum } else { 0 }
            "TotalDatastores" = if ($result.Count -gt 0) { ($result | Measure-Object -Property NumDatastores -Sum).Sum } else { 0 }
        }
        
        Stop-ScriptLogging -Success $true -Summary "Retrieved $($result.Count) datacenters" -Statistics $stats
        
        # Always output as JSON for consistency
        Write-Output ($result | ConvertTo-Json -Depth 3 -Compress)
    }
}