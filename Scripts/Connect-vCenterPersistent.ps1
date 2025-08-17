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
    
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = ""
)

# Function to write log messages
function Write-Log {
    param(
        [string]$Message, 
        [string]$Level = "Info"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logMessage = "[$timestamp] [$Level] [Connect-Persistent] $Message"
    
    switch ($Level) {
        "Error" { Write-Host $logMessage -ForegroundColor Red }
        "Warning" { Write-Host $logMessage -ForegroundColor Yellow }
        "Debug" { Write-Host $logMessage -ForegroundColor Gray }
        "Success" { Write-Host $logMessage -ForegroundColor Green }
        default { Write-Host $logMessage }
    }
    
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        try {
            $logMessage | Out-File -FilePath $LogPath -Append -Encoding UTF8
        }
        catch {
            # Ignore log errors
        }
    }
}

try {
    Write-Log "========================================" "Info"
    Write-Log "Establishing PERSISTENT vCenter connection" "Info"
    Write-Log "========================================" "Info"
    
    # Create PSCredential if not provided
    if (-not $Credential) {
        if ($Username -and $Password) {
            Write-Log "Creating PSCredential from Username/Password" "Debug"
            $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
            $Credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
            $Password = $null # Clear from memory
        }
        else {
            throw "No valid credentials provided"
        }
    }
    
    Write-Log "Target: $VCenterServer" "Info"
    Write-Log "User: $($Credential.UserName)" "Info"
    
    # Import PowerCLI if needed
    if (-not $BypassModuleCheck) {
        Write-Log "Importing PowerCLI modules..." "Info"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    }
    else {
        Write-Log "Bypassing module import (already loaded)" "Debug"
    }
    
    # Configure PowerCLI
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Check if already connected
    $existingConnection = $global:DefaultVIServer | Where-Object { $_.Name -eq $VCenterServer }
    if ($existingConnection -and $existingConnection.IsConnected) {
        Write-Log "Already connected to $VCenterServer" "Warning"
        Write-Log "Disconnecting existing connection..." "Info"
        Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
    }
    
    # Connect to vCenter
    Write-Log "Connecting to vCenter..." "Info"
    $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
    
    if ($connection -and $connection.IsConnected) {
        Write-Log "========================================" "Success"
        Write-Log "PERSISTENT CONNECTION ESTABLISHED!" "Success"
        Write-Log "========================================" "Success"
        
        Write-Log "Connection Details:" "Info"
        Write-Log "  Server: $($connection.Name)" "Info"
        Write-Log "  Version: $($connection.Version)" "Info"
        Write-Log "  Build: $($connection.Build)" "Info"
        Write-Log "  Session ID: $($connection.SessionId)" "Info"
        Write-Log "  User: $($connection.User)" "Info"
        Write-Log "  Port: $($connection.Port)" "Info"
        
        # Store in global scope for persistence
        $global:DefaultVIServer = $connection
        Write-Log "Connection stored in global scope" "Debug"
        
        # Test the connection
        $vmCount = (Get-VM -ErrorAction SilentlyContinue).Count
        Write-Log "Connection test: Can see $vmCount VMs" "Info"
        
        Write-Log "========================================" "Success"
        Write-Log "Connection will remain active for operations" "Success"
        Write-Log "========================================" "Success"
        
        # Return success with connection details
        Write-Output "SUCCESS:PERSISTENT:$($connection.SessionId)"
    }
    else {
        Write-Output "FAILURE:Connection not established"
        exit 1
    }
}
catch {
    Write-Log "CONNECTION FAILED" "Error"
    Write-Log "Error: $($_.Exception.Message)" "Error"
    Write-Output "FAILURE:$($_.Exception.Message)"
    exit 1
}