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
    param(
        [string]$Message, 
        [string]$Level = "Info"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logMessage = "[$timestamp] [$Level] [Test-vCenterConnection] $Message"
    
    # Color-code console output based on level
    switch ($Level) {
        "Error" { Write-Host $logMessage -ForegroundColor Red }
        "Warning" { Write-Host $logMessage -ForegroundColor Yellow }
        "Debug" { Write-Host $logMessage -ForegroundColor Gray }
        "Success" { Write-Host $logMessage -ForegroundColor Green }
        default { Write-Host $logMessage }
    }
    
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        try {
            # Ensure log directory exists
            $logDir = Split-Path -Path $LogPath -Parent
            if (-not [string]::IsNullOrEmpty($logDir) -and -not (Test-Path $logDir)) {
                New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            }
            
            $logMessage | Out-File -FilePath $LogPath -Append -Encoding UTF8
        }
        catch {
            Write-Host "Failed to write to log file: $_" -ForegroundColor Yellow
        }
    }
}

# Function to get system information for debugging
function Get-SystemDebugInfo {
    try {
        $psVersion = $PSVersionTable.PSVersion.ToString()
        $psEdition = $PSVersionTable.PSEdition
        $os = [System.Environment]::OSVersion.ToString()
        $is64Bit = [System.Environment]::Is64BitProcess
        
        Write-Log "PowerShell Version: $psVersion ($psEdition)" "Debug"
        Write-Log "Operating System: $os" "Debug"
        Write-Log "Process Architecture: $(if ($is64Bit) { '64-bit' } else { '32-bit' })" "Debug"
        Write-Log "Current User: $env:USERNAME" "Debug"
        Write-Log "Computer Name: $env:COMPUTERNAME" "Debug"
    }
    catch {
        Write-Log "Could not gather all system information: $_" "Debug"
    }
}

# Function to check PowerCLI installation status
function Test-PowerCLIInstallation {
    Write-Log "Checking PowerCLI installation status..." "Debug"
    
    try {
        $modules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($modules) {
            Write-Log "PowerCLI found at: $($modules[0].ModuleBase)" "Debug"
            Write-Log "PowerCLI Version: $($modules[0].Version)" "Debug"
            
            # Check for all VMware modules
            $vmwareModules = Get-Module -ListAvailable -Name "VMware.*" -ErrorAction SilentlyContinue
            Write-Log "Found $($vmwareModules.Count) VMware modules installed" "Debug"
            
            return $true
        }
        else {
            Write-Log "PowerCLI modules not found in available modules" "Warning"
            return $false
        }
    }
    catch {
        Write-Log "Error checking PowerCLI installation: $_" "Warning"
        return $false
    }
}

# Function to measure operation duration
function Measure-Duration {
    param(
        [scriptblock]$ScriptBlock,
        [string]$OperationName
    )
    
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $ScriptBlock
        $stopwatch.Stop()
        Write-Log "$OperationName completed in $($stopwatch.ElapsedMilliseconds)ms" "Debug"
        return $result
    }
    catch {
        $stopwatch.Stop()
        Write-Log "$OperationName failed after $($stopwatch.ElapsedMilliseconds)ms" "Error"
        throw
    }
}

# Main script execution
try {
    Write-Log "========================================" "Info"
    Write-Log "Starting vCenter connection test" "Info"
    Write-Log "========================================" "Info"
    
    # Log parameters
    Write-Log "Target vCenter Server: $VCenterServer" "Info"
    Write-Log "Username: $($Credential.UserName)" "Info"
    Write-Log "BypassModuleCheck: $BypassModuleCheck" "Info"
    Write-Log "LogPath: $(if ([string]::IsNullOrEmpty($LogPath)) { 'Not specified' } else { $LogPath })" "Info"
    
    # Log system information
    Get-SystemDebugInfo
    
    # Check PowerCLI installation
    $powerCLIInstalled = Test-PowerCLIInstallation
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Module bypass NOT enabled - will import PowerCLI modules" "Info"
        
        $importResult = Measure-Duration -OperationName "PowerCLI Module Import" -ScriptBlock {
            try {
                Write-Log "Importing VMware.PowerCLI module..." "Info"
                Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                
                Write-Log "Setting PowerCLI configuration..." "Info"
                Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                
                # Log loaded VMware modules
                $loadedModules = Get-Module -Name "VMware.*"
                Write-Log "Loaded $($loadedModules.Count) VMware modules" "Debug"
                
                Write-Log "PowerCLI modules imported successfully" "Success"
                return $true
            }
            catch {
                Write-Log "Failed to import PowerCLI modules: $($_.Exception.Message)" "Error"
                Write-Log "Stack Trace: $($_.ScriptStackTrace)" "Debug"
                throw
            }
        }
        
        if (-not $importResult) {
            Write-Output "Failure: PowerCLI module import failed"
            exit 1
        }
    }
    else {
        Write-Log "Module bypass ENABLED - skipping PowerCLI import (assumed already loaded)" "Info"
        
        # Verify modules are actually loaded when bypassing
        $loadedModules = Get-Module -Name "VMware.*"
        if ($loadedModules.Count -eq 0) {
            Write-Log "WARNING: BypassModuleCheck is true but no VMware modules are loaded!" "Warning"
            Write-Log "Attempting to verify PowerCLI availability..." "Warning"
            
            # Check if Connect-VIServer command is available
            $cmdAvailable = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
            if (-not $cmdAvailable) {
                Write-Log "Connect-VIServer command not found - PowerCLI may not be properly initialized" "Error"
                Write-Output "Failure: PowerCLI not available despite bypass flag"
                exit 1
            }
            else {
                Write-Log "Connect-VIServer command is available" "Debug"
            }
        }
        else {
            Write-Log "Verified: $($loadedModules.Count) VMware modules already loaded" "Debug"
        }
    }
    
    Write-Log "========================================" "Info"
    Write-Log "Attempting connection to vCenter..." "Info"
    
    # Test network connectivity first
    Write-Log "Testing network connectivity to $VCenterServer..." "Debug"
    $pingResult = Test-Connection -ComputerName $VCenterServer -Count 1 -Quiet -ErrorAction SilentlyContinue
    if ($pingResult) {
        Write-Log "Network connectivity confirmed (ping successful)" "Debug"
    }
    else {
        Write-Log "Ping failed - server may still be reachable on HTTPS port" "Warning"
    }
    
    # Test HTTPS port (443)
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $connectTask = $tcpClient.ConnectAsync($VCenterServer, 443)
        $waitResult = $connectTask.Wait(5000)  # 5 second timeout
        
        if ($waitResult -and $tcpClient.Connected) {
            Write-Log "Port 443 is reachable on $VCenterServer" "Debug"
            $tcpClient.Close()
        }
        else {
            Write-Log "Cannot reach port 443 on $VCenterServer" "Warning"
        }
    }
    catch {
        Write-Log "Error testing port connectivity: $_" "Warning"
    }
    
    # Connect using the PSCredential object with timing
    $connectionResult = Measure-Duration -OperationName "vCenter Connection" -ScriptBlock {
        Write-Log "Executing Connect-VIServer..." "Debug"
        
        try {
            # Log the actual connection attempt
            Write-Log "Connection Parameters:" "Debug"
            Write-Log "  Server: $VCenterServer" "Debug"
            Write-Log "  User: $($Credential.UserName)" "Debug"
            Write-Log "  Force: True" "Debug"
            
            $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
            
            return $connection
        }
        catch {
            Write-Log "Connection attempt failed: $($_.Exception.GetType().FullName)" "Error"
            Write-Log "Error Message: $($_.Exception.Message)" "Error"
            
            # Check for specific error types
            if ($_.Exception.Message -match "Invalid username or password") {
                Write-Log "Authentication failed - check credentials" "Error"
            }
            elseif ($_.Exception.Message -match "Could not resolve the requested service") {
                Write-Log "DNS resolution failed - check server name" "Error"
            }
            elseif ($_.Exception.Message -match "The attempt to connect was unsuccessful") {
                Write-Log "Network connection failed - check firewall and network connectivity" "Error"
            }
            
            throw
        }
    }
    
    if ($connectionResult -and $connectionResult.IsConnected) {
        Write-Log "========================================" "Success"
        Write-Log "CONNECTION SUCCESSFUL!" "Success"
        Write-Log "========================================" "Success"
        
        # Log connection details
        Write-Log "vCenter Details:" "Info"
        Write-Log "  Server: $($connectionResult.Name)" "Info"
        Write-Log "  Version: $($connectionResult.Version)" "Info"
        Write-Log "  Build: $($connectionResult.Build)" "Info"
        Write-Log "  Session ID: $($connectionResult.SessionId)" "Info"
        Write-Log "  User: $($connectionResult.User)" "Info"
        Write-Log "  Connection Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" "Info"
        
        # Get additional vCenter information
        try {
            $vcAbout = $connectionResult.ExtensionData.Content.About
            Write-Log "  Product: $($vcAbout.FullName)" "Debug"
            Write-Log "  OS Type: $($vcAbout.OsType)" "Debug"
            Write-Log "  API Version: $($vcAbout.ApiVersion)" "Debug"
        }
        catch {
            Write-Log "Could not retrieve extended vCenter information" "Debug"
        }
        
        # Disconnect cleanly
        Write-Log "Disconnecting from vCenter..." "Info"
        Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
        Write-Log "Disconnected successfully" "Info"
        
        Write-Log "========================================" "Success"
        Write-Log "Test completed successfully" "Success"
        Write-Log "========================================" "Success"
        
        # Return success in a format the app expects
        Write-Output "Success"
        exit 0
    } 
    else {
        Write-Log "Connection object was created but IsConnected is false" "Error"
        Write-Output "Failure: Connection not established"
        exit 1
    }
}
catch {
    Write-Log "========================================" "Error"
    Write-Log "CONNECTION TEST FAILED" "Error"
    Write-Log "========================================" "Error"
    
    $errorMsg = $_.Exception.Message
    Write-Log "Exception Type: $($_.Exception.GetType().FullName)" "Error"
    Write-Log "Error Message: $errorMsg" "Error"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" "Debug"
    
    # Provide user-friendly error message
    if ($errorMsg -match "Invalid username or password") {
        Write-Output "Failure: Authentication failed - Invalid credentials"
    }
    elseif ($errorMsg -match "Could not resolve") {
        Write-Output "Failure: Could not resolve server address"
    }
    elseif ($errorMsg -match "PowerCLI") {
        Write-Output "Failure: PowerCLI module error"
    }
    else {
        Write-Output "Failure: $errorMsg"
    }
    
    exit 1
}
finally {
    Write-Log "Script execution completed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')" "Debug"
}