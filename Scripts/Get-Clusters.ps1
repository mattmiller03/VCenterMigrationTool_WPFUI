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
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)" -Category "Module"
            throw $_
        }
    }
    else {
        Write-LogInfo "Bypassing PowerCLI module check" -Category "Module"
    }
    
    # Check if already connected or establish new connection
    $connectionEstablished = $false
    
    # First, check for existing connection
    if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {
        Write-LogInfo "Using existing vCenter connection: $($global:DefaultVIServer.Name)" -Category "Connection"
        $connectionEstablished = $true
    }
    # If no existing connection and credentials provided, establish new connection
    elseif ($VCenterServer -and $Username -and $Password) {
        Write-LogInfo "Establishing new vCenter connection to: $VCenterServer" -Category "Connection"
        try {
            $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
            $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
            $viConnection = Connect-VIServer -Server $VCenterServer -Credential $credential -ErrorAction Stop
            Write-LogSuccess "Connected to vCenter: $($viConnection.Name)" -Category "Connection"
            $connectionEstablished = $true
        }
        catch {
            Write-LogCritical "Failed to connect to vCenter: $($_.Exception.Message)" -Category "Connection"
            throw "Failed to establish vCenter connection: $($_.Exception.Message)"
        }
    }
    else {
        # Check if we have any VI connections at all
        $allConnections = Get-VIServer -ErrorAction SilentlyContinue
        if ($allConnections -and ($allConnections | Where-Object { $_.IsConnected })) {
            $activeConnection = $allConnections | Where-Object { $_.IsConnected } | Select-Object -First 1
            Write-LogInfo "Using active vCenter connection: $($activeConnection.Name)" -Category "Connection"
            $global:DefaultVIServer = $activeConnection
            $connectionEstablished = $true
        }
        else {
            Write-LogWarning "No active vCenter connection found and no credentials provided" -Category "Connection"
            throw "No vCenter connection available. Please connect to vCenter first or provide connection credentials."
        }
    }
    
    if (-not $connectionEstablished) {
        throw "Unable to establish or find vCenter connection"
    }
    
    # Get all clusters
    Write-LogInfo "Retrieving clusters..." -Category "Discovery"
    $clusters = Get-Cluster -ErrorAction Stop
    
    if ($clusters) {
        Write-LogSuccess "Found $($clusters.Count) clusters" -Category "Discovery"
        
        foreach ($cluster in $clusters) {
            Write-LogDebug "Processing cluster: $($cluster.Name)" -Category "Discovery"
            
            $clusterInfo = @{
                Name = $cluster.Name
                Id = $cluster.Id
                HAEnabled = $cluster.HAEnabled
                DrsEnabled = $cluster.DrsEnabled
                EVCMode = $cluster.EVCMode
            }
            
            $result += $clusterInfo
        }
    }
    else {
        Write-LogWarning "No clusters found in vCenter" -Category "Discovery"
    }
    
    Write-LogSuccess "Cluster discovery completed successfully" -Category "Summary"
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