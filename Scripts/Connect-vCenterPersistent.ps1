# Connect-vCenterPersistent.ps1
# Establishes a persistent connection to vCenter that remains active

param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory = $false)]
    [System.Management.Automation.PSCredential]$Credential,
    
    [Parameter(Mandatory = $false)]
    [string]$Username,
    
    [Parameter(Mandatory = $false)]
    [string]$Password,
    
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Connect-vCenterPersistent"

try {
    Write-LogInfo "========================================" 
    Write-LogInfo "Establishing PERSISTENT vCenter connection"
    Write-LogInfo "========================================"
    
    # Create PSCredential if not provided
    if (-not $Credential) {
        if ($Username -and $Password) {
            Write-LogDebug "Creating PSCredential from Username/Password"
            $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
            $Credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
            $Password = $null # Clear from memory
        }
        else {
            Write-LogCritical "No valid credentials provided"
            throw "No valid credentials provided"
        }
    }
    
    Write-LogInfo "Target: $VCenterServer" -Category "Connection"
    Write-LogInfo "User: $($Credential.UserName)" -Category "Connection"
    
    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..."
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Write-LogSuccess "PowerCLI modules imported successfully"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)"
            throw $_
        }
    }
    else {
        Write-LogInfo "Bypassing module import (already loaded)"
    }
    
    # Configure PowerCLI
    Write-LogDebug "Configuring PowerCLI settings..."
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    Write-LogDebug "PowerCLI configuration completed"
    
    # Check if already connected
    $existingConnection = $global:DefaultVIServer | Where-Object { $_.Name -eq $VCenterServer }
    if ($existingConnection -and $existingConnection.IsConnected) {
        Write-LogWarning "Already connected to $VCenterServer"
        Write-LogInfo "Disconnecting existing connection..."
        try {
            Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
            Write-LogInfo "Existing connection disconnected"
        }
        catch {
            Write-LogWarning "Could not disconnect existing connection: $($_.Exception.Message)"
        }
    }
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter..." -Category "Connection"
    $connectionStartTime = Get-Date
    
    try {
        $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
        $connectionTime = (Get-Date) - $connectionStartTime
        Write-LogSuccess "Connection established in $($connectionTime.TotalSeconds) seconds" -Category "Connection"
    }
    catch {
        Write-LogCritical "Connection failed: $($_.Exception.Message)" -Category "Connection"
        throw $_
    }
    
    if ($connection -and $connection.IsConnected) {
        Write-LogInfo "========================================" 
        Write-LogSuccess "PERSISTENT CONNECTION ESTABLISHED!"
        Write-LogInfo "========================================"
        
        Write-LogInfo "Connection Details:" -Category "Details"
        Write-LogInfo "  Server: $($connection.Name)"
        Write-LogInfo "  Version: $($connection.Version)"
        Write-LogInfo "  Build: $($connection.Build)"
        Write-LogInfo "  Session ID: $($connection.SessionId)"
        Write-LogInfo "  User: $($connection.User)"
        Write-LogInfo "  Port: $($connection.Port)"
        
        # Store in global scope for persistence
        $global:DefaultVIServer = $connection
        Write-LogDebug "Connection stored in global scope"
        
        # Test the connection
        Write-LogInfo "Testing connection..." -Category "Test"
        try {
            $vmCount = (Get-VM -ErrorAction SilentlyContinue).Count
            Write-LogSuccess "Connection test: Can see $vmCount VMs" -Category "Test"
        }
        catch {
            Write-LogWarning "Connection test failed: $($_.Exception.Message)" -Category "Test"
        }
        
        Write-LogInfo "========================================" 
        Write-LogSuccess "Connection will remain active for operations"
        Write-LogInfo "========================================" 
        
        # Create statistics for logging
        $stats = @{
            "Server" = $VCenterServer
            "Version" = $connection.Version
            "Build" = $connection.Build
            "SessionId" = $connection.SessionId
            "ConnectionTimeSeconds" = [math]::Round($connectionTime.TotalSeconds, 2)
            "VMsVisible" = $vmCount
        }
        
        Stop-ScriptLogging -Success $true -Summary "Persistent connection established to $VCenterServer" -Statistics $stats
        
        # Return success with connection details
        $result = @{
            Success = $true
            Message = "Persistent connection established"
            Server = $VCenterServer
            SessionId = $connection.SessionId
            Version = $connection.Version
            VMCount = $vmCount
        }
        
        $result | ConvertTo-Json -Compress
    }
    else {
        Write-LogCritical "Connection not established - unknown error"
        Stop-ScriptLogging -Success $false -Summary "Connection failed - unknown error"
        
        $result = @{
            Success = $false
            Message = "Connection not established"
            Server = $VCenterServer
        }
        
        $result | ConvertTo-Json -Compress
        throw "Connection not established"
    }
}
catch {
    Write-LogCritical "CONNECTION FAILED"
    Write-LogError "Error: $($_.Exception.Message)" -Category "Connection"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    Stop-ScriptLogging -Success $false -Summary "Connection failed: $($_.Exception.Message)"
    
    $result = @{
        Success = $false
        Message = "Connection failed: $($_.Exception.Message)"
        Server = $VCenterServer
        Error = $_.Exception.Message
    }
    
    $result | ConvertTo-Json -Compress
    throw $_
}