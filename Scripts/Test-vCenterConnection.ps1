param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password,
    
    [string]$LogPath = "Logs"
)

# Function to write to both console and log file
function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    # Write to console for UI display (as verbose/information stream)
    Write-Information $logMessage -InformationAction Continue
    
    # Ensure log directory exists
    if (-not (Test-Path $LogPath)) {
        New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
    }
    
    # Write to log file
    $logFile = Join-Path $LogPath "vCenter-Connection-Test-$(Get-Date -Format 'yyyy-MM-dd').log"
    Add-Content -Path $logFile -Value $logMessage
}

# Suppress errors for the connection attempt so we can handle them gracefully.
$ErrorActionPreference = "SilentlyContinue"

try {
    Write-Log "Starting vCenter connection test" "INFO"
    Write-Log "Log path: $LogPath" "INFO"
    Write-Log "Target vCenter server: $VCenterServer" "INFO"
    Write-Log "Username: $Username" "INFO"
    Write-Log "PowerShell version: $($PSVersionTable.PSVersion.ToString())" "INFO"
    
    # Check if PowerCLI module is loaded
    Write-Log "Checking PowerCLI module availability..." "INFO"
    $powerCliModule = Get-Module -Name VMware.PowerCLI
    if (-not $powerCliModule) {
        Write-Log "PowerCLI module not loaded, attempting to import..." "INFO"
        try {
            Import-Module VMware.PowerCLI -Force
            Write-Log "PowerCLI module imported successfully" "INFO"
        }
        catch {
            Write-Log "Failed to import PowerCLI module: $($_.Exception.Message)" "ERROR"
            Write-Output "Failure: PowerCLI module not available"
            return
        }
    } else {
        Write-Log "PowerCLI module already loaded (version: $($powerCliModule.Version))" "INFO"
    }
    
    # Check PowerCLI configuration settings
    Write-Log "Checking PowerCLI configuration..." "INFO"
    try {
        $powerCliConfig = Get-PowerCLIConfiguration -Scope Session
        Write-Log "PowerCLI invalid certificate action: $($powerCliConfig.InvalidCertificateAction)" "INFO"
        Write-Log "PowerCLI display deprecation warnings: $($powerCliConfig.DisplayDeprecationWarnings)" "INFO"
        Write-Log "PowerCLI participate in CEIP: $($powerCliConfig.ParticipateInCEIP)" "INFO"
    }
    catch {
        Write-Log "Unable to retrieve PowerCLI configuration: $($_.Exception.Message)" "WARN"
    }
    
    Write-Log "Creating secure credential object..." "INFO"
    # Create a secure PSCredential object. This is the key change.
    # It converts the plain-text password into a SecureString.
    $credential = New-Object System.Management.Automation.PSCredential($Username, (ConvertTo-SecureString -String $Password -AsPlainText -Force))
    Write-Log "Credential object created successfully" "INFO"
    
    # Check if already connected to this server
    $existingConnection = Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue
    if ($existingConnection) {
        Write-Log "Existing connection found to $VCenterServer, disconnecting first..." "INFO"
        Disconnect-VIServer -Server $VCenterServer -Confirm:$false -Force
        Write-Log "Disconnected from existing session" "INFO"
    }
    
    Write-Log "Attempting to connect to vCenter server: $VCenterServer" "INFO"
    $connectionStartTime = Get-Date
    
    # Attempt to connect using the explicit credential object.
    $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force
    
    $connectionEndTime = Get-Date
    $connectionDuration = ($connectionEndTime - $connectionStartTime).TotalMilliseconds
    
    if ($connection) {
        # If the connection object is not null, it was successful.
        Write-Log "Connection successful to $VCenterServer" "INFO"
        Write-Log "Connection duration: $connectionDuration ms" "INFO"
        Write-Log "Connected as: $($connection.User)" "INFO"
        Write-Log "vCenter version: $($connection.Version)" "INFO"
        Write-Log "vCenter build: $($connection.Build)" "INFO"
        Write-Log "Server type: $($connection.ProductLine)" "INFO"
        Write-Log "Connection state: $($connection.IsConnected)" "INFO"
        Write-Log "Session ID: $($connection.SessionId)" "INFO"
        
        # Test basic functionality by getting server time
        try {
            $serverInfo = Get-View ServiceInstance
            $serverTime = $serverInfo.CurrentTime()
            Write-Log "Server current time: $serverTime" "INFO"
            
            # Get datacenter count as a basic functionality test
            $datacenters = Get-Datacenter
            Write-Log "Available datacenters: $($datacenters.Count)" "INFO"
            if ($datacenters.Count -gt 0) {
                foreach ($dc in $datacenters) {
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
        # If the connection object is null, it failed. Grab the last error message.
        $errorMessage = if ($Error.Count -gt 0) { $Error[0].ToString() } else { "Unknown connection failure" }
        Write-Log "Connection failed: $errorMessage" "ERROR"
        Write-Log "Connection attempt duration: $connectionDuration ms" "INFO"
        
        # Check for common error patterns and provide specific guidance
        if ($errorMessage -match "incorrect user name or password") {
            Write-Log "Authentication failure detected - verify username and password" "ERROR"
        }
        elseif ($errorMessage -match "connection refused" -or $errorMessage -match "network") {
            Write-Log "Network connectivity issue detected - verify server address and network connectivity" "ERROR"
        }
        elseif ($errorMessage -match "certificate") {
            Write-Log "SSL certificate issue detected - server may have invalid or untrusted certificate" "ERROR"
        }
        
        Write-Output "Failure: $errorMessage"
    }
}
catch {
    # Catch any unexpected script-terminating errors.
    $errorMessage = $_.Exception.Message
    Write-Log "Unexpected error during connection test: $errorMessage" "ERROR"
    Write-Log "Error category: $($_.CategoryInfo.Category)" "ERROR"
    Write-Log "Error target: $($_.TargetObject)" "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    
    Write-Output "Failure: $errorMessage"
}
finally {
    Write-Log "Cleaning up connection..." "INFO"
    # Always attempt to disconnect to clean up the session.
    $cleanupConnection = Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue
    if ($cleanupConnection) {
        Write-Log "Disconnecting from $VCenterServer (Session ID: $($cleanupConnection.SessionId))" "INFO"
        Disconnect-VIServer -Server $VCenterServer -Confirm:$false -Force
        Write-Log "Successfully disconnected from vCenter server" "INFO"
    } else {
        Write-Log "No active connection found to clean up" "INFO"
    }
    
    Write-Log "vCenter connection test script execution completed" "INFO"
}