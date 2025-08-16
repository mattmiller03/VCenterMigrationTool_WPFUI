param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory = $true)]
    [System.Management.Automation.PSCredential]$Credential,
    
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = ""
)

# Function to write log messages
function Write-Log {
    param([string]$Message, [string]$Level = "Info")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Host $logMessage
    
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        try {
            $logMessage | Out-File -FilePath $LogPath -Append -Encoding UTF8
        }
        catch {
            # Ignore log file errors
        }
    }
}

try {
    Write-Log "Starting vCenter connection test for server: $VCenterServer" "Info"
    Write-Log "Username: $($Credential.UserName)" "Info"
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Importing PowerCLI modules..." "Info"
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-Log "PowerCLI modules imported successfully" "Info"
        }
        catch {
            Write-Error "Failed to import PowerCLI modules: $($_.Exception.Message)"
            exit 1
        }
    }
    else {
        Write-Log "Bypassing PowerCLI module check (already confirmed installed)" "Info"
    }
    
    Write-Log "Attempting connection to vCenter..." "Info"
    
    # Connect using the PSCredential object
    $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
    
    if ($connection -and $connection.IsConnected) {
        Write-Log "SUCCESS: Connected to $VCenterServer" "Info"
        Write-Log "vCenter Version: $($connection.Version)" "Info"
        Write-Log "Connection ID: $($connection.SessionId)" "Info"
        
        # Disconnect cleanly
        Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
        Write-Log "Disconnected from vCenter" "Info"
        
        Write-Host "SUCCESS: vCenter connection test completed successfully"
        exit 0
    } 
    else {
        Write-Error "Failed to establish connection to $VCenterServer"
        exit 1
    }
}
catch {
    $errorMsg = "vCenter connection test failed: $($_.Exception.Message)"
    Write-Log $errorMsg "Error"
    Write-Error $errorMsg
    exit 1
}