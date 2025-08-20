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
    
    # Check for existing vCenter connections first
    Write-LogInfo "Checking for existing vCenter connections..." -Category "Connection"
    $connectionUsed = $null
    
    try {
        # Check all existing connections
        $allConnections = @(Get-VIServer)
        Write-LogInfo "Found $($allConnections.Count) existing vCenter connection(s)" -Category "Connection"
        
        if ($allConnections.Count -gt 0) {
            # Find first connected server
            $activeConnection = $allConnections | Where-Object { $_.IsConnected } | Select-Object -First 1
            
            if ($activeConnection) {
                $connectionUsed = $activeConnection
                Write-LogSuccess "Using existing vCenter connection: $($activeConnection.Name)" -Category "Connection"
                Write-LogInfo "  Server: $($activeConnection.Name)" -Category "Connection"
                Write-LogInfo "  User: $($activeConnection.User)" -Category "Connection"
                Write-LogInfo "  Version: $($activeConnection.Version)" -Category "Connection"
            }
            else {
                Write-LogWarning "Found vCenter connections but none are active" -Category "Connection"
            }
        }
        else {
            Write-LogInfo "No existing vCenter connections found" -Category "Connection"
        }
    }
    catch {
        Write-LogWarning "Error checking existing connections: $($_.Exception.Message)" -Category "Connection"
    }
    
    # If no existing connection and credentials provided, create new connection
    if (-not $connectionUsed -and $VCenterServer -and $Username -and $Password) {
        Write-LogInfo "Creating new vCenter connection to: $VCenterServer" -Category "Connection"
        try {
            $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
            $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
            $connectionUsed = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
            Write-LogSuccess "Successfully connected to vCenter: $($connectionUsed.Name)" -Category "Connection"
        }
        catch {
            Write-LogError "Failed to connect to vCenter $VCenterServer : $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Final validation
    if (-not $connectionUsed) {
        $errorMsg = "No vCenter connection available. Please connect to vCenter first or provide connection parameters."
        Write-LogCritical $errorMsg -Category "Connection"
        throw $errorMsg
    }
    
    # Retrieve clusters from vCenter with timeout
    Write-LogInfo "Retrieving clusters from vCenter..." -Category "Discovery"
    
    try {
        # Use a simple Get-Cluster call with timeout
        $clusters = Get-Cluster -ErrorAction Stop
        
        if ($clusters) {
            $clusterCount = if ($clusters.Count) { $clusters.Count } else { 1 }
            Write-LogSuccess "Found $clusterCount cluster(s)" -Category "Discovery"
            
            # Process clusters - keep it simple
            foreach ($cluster in @($clusters)) {
                Write-LogInfo "Processing cluster: $($cluster.Name)" -Category "Discovery"
                
                $clusterInfo = @{
                    Name = $cluster.Name
                    Id = $cluster.Id
                    HAEnabled = $cluster.HAEnabled
                    DrsEnabled = $cluster.DrsEnabled
                    EVCMode = if ($cluster.EVCMode) { $cluster.EVCMode } else { "" }
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