# Get-Clusters.ps1 - Retrieves cluster information from vCenter
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password,
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
    
    # Ensure PowerCLI modules are available
    Write-LogInfo "Checking PowerCLI module availability..." -Category "Module"
    
    # Check if PowerCLI commands are already available
    $powerCLIAvailable = $false
    try {
        $powerCLIAvailable = Get-Command "Get-VIServer" -ErrorAction SilentlyContinue -ne $null
        if ($powerCLIAvailable) {
            Write-LogSuccess "PowerCLI commands are already available" -Category "Module"
        }
    }
    catch {
        $powerCLIAvailable = $false
    }
    
    # Import PowerCLI modules if needed
    if (-not $powerCLIAvailable) {
        if ($BypassModuleCheck) {
            Write-LogWarning "BypassModuleCheck is true but PowerCLI commands not available - forcing import" -Category "Module"
        }
        
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        try {
            # Try to import the core VMware modules
            $modulesToImport = @("VMware.VimAutomation.Core", "VMware.VimAutomation.Common")
            
            foreach ($module in $modulesToImport) {
                $moduleInfo = Get-Module -Name $module -ListAvailable -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($moduleInfo) {
                    Write-LogInfo "Importing module: $module" -Category "Module"
                    Import-Module $module -Force -ErrorAction Stop
                }
            }
            
            # Try PowerCLI as fallback
            if (-not (Get-Command "Get-VIServer" -ErrorAction SilentlyContinue)) {
                Write-LogInfo "Core modules not sufficient, trying VMware.PowerCLI..." -Category "Module"
                Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            }
            
            # Configure PowerCLI
            if (Get-Command "Set-PowerCLIConfiguration" -ErrorAction SilentlyContinue) {
                Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
            }
            
            Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)" -Category "Module"
            throw "PowerCLI modules are required but could not be imported: $($_.Exception.Message)"
        }
    }
    
    # Final verification
    if (-not (Get-Command "Get-VIServer" -ErrorAction SilentlyContinue)) {
        throw "PowerCLI commands are not available. Please ensure VMware PowerCLI is installed."
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
    if (-not $connectionEstablished -and $VCenterServer -and $Username -and $Password) {
        Write-LogInfo "Attempting to establish new vCenter connection to: $VCenterServer" -Category "Connection"
        try {
            $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
            $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
            $connectionUsed = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
            Write-LogSuccess "Successfully connected to vCenter: $($connectionUsed.Name)" -Category "Connection"
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
            $errorMsg += "Please connect to vCenter first or provide connection parameters (VCenterServer, Username, Password)."
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
    
    # Retrieve clusters from vCenter
    Write-LogInfo "Retrieving clusters from vCenter..." -Category "Discovery"
    
    try {
        $clusters = Get-Cluster -ErrorAction Stop
        
        if ($clusters) {
            $clusterCount = if ($clusters.Count) { $clusters.Count } else { 1 }
            Write-LogSuccess "Found $clusterCount cluster(s)" -Category "Discovery"
            
            # Ensure $clusters is always an array for consistent processing
            if ($clusters -isnot [array]) {
                $clusters = @($clusters)
            }
            
            foreach ($cluster in $clusters) {
                Write-LogInfo "Processing cluster: $($cluster.Name)" -Category "Discovery"
                
                try {
                    $clusterInfo = @{
                        Name = if ($cluster.Name) { $cluster.Name } else { "Unknown" }
                        Id = if ($cluster.Id) { $cluster.Id } else { "" }
                        HAEnabled = if ($null -ne $cluster.HAEnabled) { $cluster.HAEnabled } else { $false }
                        DrsEnabled = if ($null -ne $cluster.DrsEnabled) { $cluster.DrsEnabled } else { $false }
                        EVCMode = if ($cluster.EVCMode) { $cluster.EVCMode } else { "" }
                    }
                    
                    $result += $clusterInfo
                    Write-LogDebug "Cluster '$($cluster.Name)' processed successfully" -Category "Discovery"
                }
                catch {
                    Write-LogWarning "Error processing cluster '$($cluster.Name)': $($_.Exception.Message)" -Category "Discovery"
                    # Continue processing other clusters
                    continue
                }
            }
            
            Write-LogSuccess "Successfully processed $($result.Count) cluster(s)" -Category "Discovery"
        }
        else {
            Write-LogWarning "No clusters found in vCenter '$($connectionUsed.Name)'" -Category "Discovery"
            Write-LogInfo "This could be normal if the connected user doesn't have permissions to view clusters" -Category "Discovery"
        }
    }
    catch {
        Write-LogError "Failed to retrieve clusters: $($_.Exception.Message)" -Category "Discovery"
        Write-LogError "This might be due to insufficient permissions or connection issues" -Category "Discovery"
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