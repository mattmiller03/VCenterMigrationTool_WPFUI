param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password,
    
    [string]$LogPath = "Logs",
    
    [switch]$BypassModuleCheck = $false  # NEW: Allow bypassing PowerCLI checks
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
    Write-Log "Target vCenter server: $VCenterServer" "INFO"
    Write-Log "Username: $Username" "INFO"
    Write-Log "PowerShell version: $($PSVersionTable.PSVersion.ToString())" "INFO"
    
    # OPTIMIZED: Only check PowerCLI if not bypassed
    if (-not $BypassModuleCheck) {
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
    } else {
        Write-Log "Bypassing PowerCLI module check (assumed available)" "INFO"
        # Still try to import silently in case it's not loaded
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction SilentlyContinue
        } catch {
            # Ignore import errors when bypassing
        }
    }
    
    Write-Log "Creating secure credential object..." "INFO"
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
        Write-Log "Connection successful to $VCenterServer" "INFO"
        Write-Log "Connection duration: $connectionDuration ms" "INFO"
        Write-Log "Connected as: $($connection.User)" "INFO"
        Write-Log "vCenter version: $($connection.Version)" "INFO"
        Write-Log "vCenter build: $($connection.Build)" "INFO"
        
        # Test basic functionality by getting server time
        try {
            $serverInfo = Get-View ServiceInstance
            $serverTime = $serverInfo.CurrentTime()
            Write-Log "Server current time: $serverTime" "INFO"
            
            # Get datacenter count as a basic functionality test
            $datacenters = Get-Datacenter
            Write-Log "Available datacenters: $($datacenters.Count)" "INFO"
        }
        catch {
            Write-Log "Warning: Connected but unable to retrieve server information: $($_.Exception.Message)" "WARN"
        }
        
        Write-Log "vCenter connection test completed successfully" "INFO"
        Write-Output "Success"
    }
    else {
        $errorMessage = if ($Error.Count -gt 0) { $Error[0].ToString() } else { "Unknown connection failure" }
        Write-Log "Connection failed: $errorMessage" "ERROR"
        Write-Output "Failure: $errorMessage"
    }
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Log "Unexpected error during connection test: $errorMessage" "ERROR"
    Write-Output "Failure: $errorMessage"
}
finally {
    Write-Log "Cleaning up connection..." "INFO"
    $cleanupConnection = Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue
    if ($cleanupConnection) {
        Write-Log "Disconnecting from $VCenterServer" "INFO"
        Disconnect-VIServer -Server $VCenterServer -Confirm:$false -Force
        Write-Log "Successfully disconnected from vCenter server" "INFO"
    }
    
    Write-Log "vCenter connection test script execution completed" "INFO"
}