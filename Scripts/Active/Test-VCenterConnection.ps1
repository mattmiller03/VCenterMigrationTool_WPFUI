# Test-VCenterConnection.ps1 - Diagnostic script for vCenter connections
param(
    [string]$LogPath = "",
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Test-VCenterConnection" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $true
$connectionInfo = @{}

try {
    Write-LogInfo "Starting vCenter connection diagnostics" -Category "Diagnostics"
    
    # Check PowerCLI module
    Write-LogInfo "Checking PowerCLI modules..." -Category "Module"
    $powerCLIModules = Get-Module -Name "VMware.*" -ListAvailable
    if ($powerCLIModules) {
        Write-LogSuccess "PowerCLI modules found: $($powerCLIModules.Count)" -Category "Module"
        $connectionInfo["PowerCLIAvailable"] = $true
        $connectionInfo["PowerCLIModules"] = $powerCLIModules.Count
    } else {
        Write-LogWarning "No PowerCLI modules found" -Category "Module"
        $connectionInfo["PowerCLIAvailable"] = $false
    }
    
    # Check loaded modules
    $loadedModules = Get-Module -Name "VMware.*"
    Write-LogInfo "Loaded PowerCLI modules: $($loadedModules.Count)" -Category "Module"
    $connectionInfo["PowerCLILoaded"] = $loadedModules.Count
    
    # Check global DefaultVIServer
    Write-LogInfo "Checking global DefaultVIServer..." -Category "Connection"
    if ($global:DefaultVIServer) {
        Write-LogInfo "DefaultVIServer exists: $($global:DefaultVIServer.Name)" -Category "Connection"
        Write-LogInfo "DefaultVIServer IsConnected: $($global:DefaultVIServer.IsConnected)" -Category "Connection"
        $connectionInfo["DefaultVIServer"] = @{
            Name = $global:DefaultVIServer.Name
            IsConnected = $global:DefaultVIServer.IsConnected
            Version = $global:DefaultVIServer.Version
            Build = $global:DefaultVIServer.Build
        }
    } else {
        Write-LogWarning "No global DefaultVIServer found" -Category "Connection"
        $connectionInfo["DefaultVIServer"] = $null
    }
    
    # Check all VI connections
    Write-LogInfo "Checking all vCenter connections..." -Category "Connection"
    try {
        $allConnections = Get-VIServer -ErrorAction Stop
        if ($allConnections) {
            Write-LogSuccess "Found $($allConnections.Count) total vCenter connections" -Category "Connection"
            $activeConnections = $allConnections | Where-Object { $_.IsConnected }
            Write-LogInfo "Active connections: $($activeConnections.Count)" -Category "Connection"
            
            $connectionInfo["TotalConnections"] = $allConnections.Count
            $connectionInfo["ActiveConnections"] = $activeConnections.Count
            $connectionInfo["Connections"] = @()
            
            foreach ($conn in $allConnections) {
                $connInfo = @{
                    Name = $conn.Name
                    IsConnected = $conn.IsConnected
                    Version = $conn.Version
                    Build = $conn.Build
                    User = $conn.User
                }
                Write-LogInfo "Connection: $($conn.Name) - Connected: $($conn.IsConnected)" -Category "Connection"
                $connectionInfo["Connections"] += $connInfo
            }
        } else {
            Write-LogWarning "No vCenter connections found" -Category "Connection"
            $connectionInfo["TotalConnections"] = 0
            $connectionInfo["ActiveConnections"] = 0
        }
    }
    catch {
        Write-LogError "Error checking vCenter connections: $($_.Exception.Message)" -Category "Connection"
        $connectionInfo["ConnectionError"] = $_.Exception.Message
    }
    
    # Test basic PowerCLI commands
    Write-LogInfo "Testing basic PowerCLI functionality..." -Category "Test"
    try {
        if ($activeConnections -and $activeConnections.Count -gt 0) {
            Write-LogInfo "Testing Get-Cluster command..." -Category "Test"
            $clusters = Get-Cluster -ErrorAction Stop
            Write-LogSuccess "Successfully retrieved $($clusters.Count) clusters" -Category "Test"
            $connectionInfo["ClustersFound"] = $clusters.Count
            
            if ($clusters.Count -gt 0) {
                $connectionInfo["SampleCluster"] = @{
                    Name = $clusters[0].Name
                    Id = $clusters[0].Id
                    HAEnabled = $clusters[0].HAEnabled
                    DrsEnabled = $clusters[0].DrsEnabled
                }
            }
        } else {
            Write-LogWarning "No active connections to test PowerCLI commands" -Category "Test"
        }
    }
    catch {
        Write-LogError "Error testing PowerCLI commands: $($_.Exception.Message)" -Category "Test"
        $connectionInfo["PowerCLITestError"] = $_.Exception.Message
    }
    
    $connectionInfo["Success"] = $true
    $connectionInfo["Timestamp"] = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    
    Write-LogSuccess "vCenter connection diagnostics completed" -Category "Summary"
}
catch {
    $scriptSuccess = $false
    $finalSummary = "Connection diagnostics failed: $($_.Exception.Message)"
    Write-LogCritical $finalSummary -Category "Error"
    $connectionInfo["Success"] = $false
    $connectionInfo["Error"] = $_.Exception.Message
}
finally {
    $finalStats = @{
        "PowerCLIAvailable" = $connectionInfo.ContainsKey("PowerCLIAvailable")
        "TotalConnections" = $connectionInfo.GetValue("TotalConnections", 0)
        "ActiveConnections" = $connectionInfo.GetValue("ActiveConnections", 0)
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary "Diagnostics completed" -Statistics $finalStats
    
    # Output result as JSON
    Write-Output ($connectionInfo | ConvertTo-Json -Depth 5 -Compress)
}