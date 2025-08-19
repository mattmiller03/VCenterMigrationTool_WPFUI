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
    
    # Check if already connected
    if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {
        Write-LogInfo "Using existing vCenter connection: $($global:DefaultVIServer.Name)" -Category "Connection"
    }
    else {
        Write-LogWarning "No active vCenter connection found" -Category "Connection"
        throw "No vCenter connection available"
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