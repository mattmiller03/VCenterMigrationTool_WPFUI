param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password,
    
    [string]$LogPath = "Logs",
    
    [switch]$BypassModuleCheck = $false
)

# Function to write to both console and log file
function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    Write-Information $logMessage -InformationAction Continue
    
    if (-not (Test-Path $LogPath)) {
        New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
    }
    
    $logFile = Join-Path $LogPath "vCenter-Connection-Test-$(Get-Date -Format 'yyyy-MM-dd').log"
    Add-Content -Path $logFile -Value $logMessage
}

# Set proper error handling for immediate failures
$ErrorActionPreference = "Stop"

try {
    Write-Log "Starting vCenter connection test" "INFO"
    Write-Log "Target vCenter server: $VCenterServer" "INFO"
    Write-Log "Username: $Username" "INFO"
    Write-Log "PowerShell version: $($PSVersionTable.PSVersion.ToString())" "INFO"
    
    # DEBUG: Log the bypass parameter status
    Write-Log "BypassModuleCheck parameter: $BypassModuleCheck" "DEBUG"
    
    # OPTIMIZED: Only check PowerCLI if not bypassed
    if (-not $BypassModuleCheck) {
        Write-Log "Checking PowerCLI module availability..." "INFO"
        $powerCliModule = Get-Module -Name VMware.PowerCLI -ListAvailable
        if (-not $powerCliModule) {
            Write-Log "PowerCLI module not found" "ERROR"
            Write-Output "Failure: PowerCLI module not available"
            exit 1
        }
        
        Write-Log "PowerCLI module found, importing..." "INFO"
        Import-Module VMware.PowerCLI -Force
        Write-Log "PowerCLI module imported successfully" "INFO"
    } else {
        Write-Log "Bypassing PowerCLI module check (assumed available)" "INFO"
        # Still try to import silently
        Import-Module VMware.PowerCLI -Force -ErrorAction SilentlyContinue
    }
    
    # Configure PowerCLI to suppress certificate warnings and prompts
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -ParticipateInCEIP $false -Scope Session -Confirm:$false | Out-Null
    Write-Log "PowerCLI configuration set to ignore invalid certificates" "INFO"
    
    Write-Log "Creating secure credential object..." "INFO"
    $credential = New-Object System.Management.Automation.PSCredential($Username, (ConvertTo-SecureString -String $Password -AsPlainText -Force))
    
    # Check if already connected to this server and disconnect
    $existingConnections = Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue
    if ($existingConnections) {
        Write-Log "Found $($existingConnections.Count) existing connection(s) to $VCenterServer, disconnecting..." "INFO"
        $existingConnections | Disconnect-VIServer -Confirm:$false -Force
        Write-Log "Disconnected from existing sessions" "INFO"
    }
    
    Write-Log "Attempting to connect to vCenter server: $VCenterServer" "INFO"
    $connectionStartTime = Get-Date
    
    # Attempt connection with Force parameter and proper error handling
    $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force -ErrorAction Stop
    
    $connectionEndTime = Get-Date
    $connectionDuration = ($connectionEndTime - $connectionStartTime).TotalMilliseconds
    
    if ($connection) {
        Write-Log "Connection successful to $VCenterServer" "INFO"
        Write-Log "Connection duration: $([math]::Round($connectionDuration, 0)) ms" "INFO"
        Write-Log "Connected as: $($connection.User)" "INFO"
        Write-Log "vCenter version: $($connection.Version)" "INFO"
        Write-Log "vCenter build: $($connection.Build)" "INFO"
        Write-Log "Connection ID: $($connection.SessionId)" "INFO"
        
        # Test basic functionality by getting server info
        try {
            $serverInfo = Get-View ServiceInstance -ErrorAction Stop
            $serverTime = $serverInfo.CurrentTime()
            Write-Log "Server current time: $serverTime" "INFO"
            
            # Get datacenter count as a basic functionality test
            $datacenters = Get-Datacenter -ErrorAction Stop
            Write-Log "Available datacenters: $($datacenters.Count)" "INFO"
            if ($datacenters.Count -gt 0) {
                foreach ($dc in $datacenters | Select-Object -First 3) {
                    Write-Log "  - Datacenter: $($dc.Name)" "INFO"
                }
            }
        }
        catch {
            Write-Log "Warning: Connected but unable to retrieve server information: $($_.Exception.Message)" "WARN"
        }
        
        Write-Log "vCenter connection test completed successfully" "INFO"
        Write-Output "Success"
    }
    else {
        Write-Log "Connection object is null - unknown failure" "ERROR"
        Write-Output "Failure: Connection returned null"
    }
}
catch {
    $errorMessage = $_.Exception.Message
    $connectionEndTime = Get-Date
    $connectionDuration = ($connectionEndTime - $connectionStartTime).TotalMilliseconds
    
    Write-Log "Connection failed after $([math]::Round($connectionDuration, 0)) ms" "ERROR"
    Write-Log "Error details: $errorMessage" "ERROR"
    Write-Log "Error type: $($_.Exception.GetType().Name)" "ERROR"
    
    # Provide specific error guidance based on error content
    if ($errorMessage -match "SSL|certificate|TLS") {
        Write-Log "SSL/Certificate issue detected" "ERROR"
        $friendlyError = "SSL connection could not be established. Server may have invalid certificate."
    }
    elseif ($errorMessage -match "timeout|timed out") {
        Write-Log "Connection timeout detected" "ERROR"
        $friendlyError = "Connection timed out. Verify server address and network connectivity."
    }
    elseif ($errorMessage -match "authentication|login|password|credential") {
        Write-Log "Authentication failure detected" "ERROR"
        $friendlyError = "Authentication failed. Verify username and password."
    }
    elseif ($errorMessage -match "network|host|resolve") {
        Write-Log "Network connectivity issue detected" "ERROR"
        $friendlyError = "Cannot reach server. Verify server address and network connectivity."
    }
    else {
        $friendlyError = $errorMessage
    }
    
    Write-Output "Failure: $friendlyError"
}
finally {
    Write-Log "Cleaning up connections..." "INFO"
    # Clean up any connections to this specific server
    try {
        $connectionsToCleanup = Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue
        if ($connectionsToCleanup) {
            $connectionsToCleanup | Disconnect-VIServer -Confirm:$false -Force
            Write-Log "Successfully cleaned up $($connectionsToCleanup.Count) connection(s)" "INFO"
        } else {
            Write-Log "No connections to clean up" "INFO"
        }
    }
    catch {
        Write-Log "Error during cleanup: $($_.Exception.Message)" "WARN"
    }
    
    Write-Log "vCenter connection test script execution completed" "INFO"
}