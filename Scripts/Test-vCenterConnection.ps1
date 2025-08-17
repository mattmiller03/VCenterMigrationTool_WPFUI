param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    # Accept EITHER a PSCredential OR Username/Password combination
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
# At the start of any script
. "$PSScriptRoot\Write-ScriptLog.ps1"
Start-ScriptLogging -ScriptName "Test-vCenterConnection"
    
 
try {
    Write-Log "========================================" "Info"
    Write-Log "Starting vCenter connection test (Direct Parameters)" "Info"
    Write-Log "========================================" "Info"
    
    # Determine credential source and create PSCredential if needed
    if ($Credential) {
        Write-Log "Using provided PSCredential object" "Debug"
        Write-Log "Username from credential: $($Credential.UserName)" "Info"
    }
    elseif ($Username -and $Password) {
        Write-Log "Creating PSCredential from Username/Password parameters" "Debug"
        Write-Log "Username: $Username" "Info"
        
        # Convert plain text password to SecureString and create credential
        $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
        $Credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
        
        Write-Log "PSCredential created successfully" "Debug"
        
        # Clear the plain text password from memory
        $Password = $null
    }
    else {
        throw "No valid credentials provided. Either provide -Credential or both -Username and -Password"
    }
    
    # Log execution parameters
    Write-Log "Target vCenter Server: $VCenterServer" "Info"
    Write-Log "BypassModuleCheck: $BypassModuleCheck" "Info"
    Write-Log "Execution Method: Direct parameter passing (no temp files)" "Success"
    Write-Log "LogPath: $(if ([string]::IsNullOrEmpty($LogPath)) { 'Not specified' } else { $LogPath })" "Info"
    
    # Log system information for debugging
    Write-Log "PowerShell Version: $($PSVersionTable.PSVersion.ToString())" "Debug"
    Write-Log "PowerShell Edition: $($PSVersionTable.PSEdition)" "Debug"
    
    # Check PowerCLI installation status
    Write-Log "Checking PowerCLI installation..." "Debug"
    $powerCLIModules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    if ($powerCLIModules) {
        Write-Log "PowerCLI found: Version $($powerCLIModules[0].Version)" "Debug"
    }
    else {
        Write-Log "PowerCLI modules not found in available modules" "Warning"
    }
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Module bypass NOT enabled - importing PowerCLI modules" "Info"
        
        $importResult = Measure-Duration -OperationName "PowerCLI Module Import" -ScriptBlock {
            try {
                Write-Log "Importing VMware.PowerCLI module..." "Info"
                Import-Module VMware.PowerCLI -Force -ErrorAction Stop
                
                Write-Log "Setting PowerCLI configuration..." "Info"
                Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                
                # Log loaded VMware modules
                $loadedModules = Get-Module -Name "VMware.*"
                Write-Log "Successfully loaded $($loadedModules.Count) VMware modules" "Debug"
                
                Write-Log "PowerCLI modules imported successfully" "Success"
                return $true
            }
            catch {
                Write-Log "Failed to import PowerCLI modules: $($_.Exception.Message)" "Error"
                throw
            }
        }
        
        if (-not $importResult) {
            Write-Output "Failure: PowerCLI module import failed"
            exit 1
        }
    }
    else {
        Write-Log "Module bypass ENABLED - skipping PowerCLI import" "Success"
        Write-Log "Assuming PowerCLI modules are already loaded" "Info"
        
        # Verify modules are actually loaded when bypassing
        $loadedModules = Get-Module -Name "VMware.*"
        if ($loadedModules.Count -eq 0) {
            Write-Log "WARNING: BypassModuleCheck is true but no VMware modules are loaded!" "Warning"
            
            # Check if Connect-VIServer command is available
            $cmdAvailable = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
            if (-not $cmdAvailable) {
                Write-Log "Connect-VIServer command not found - PowerCLI not properly initialized" "Error"
                Write-Output "Failure: PowerCLI not available despite bypass flag"
                exit 1
            }
            else {
                Write-Log "Connect-VIServer command is available" "Debug"
            }
        }
        else {
            Write-Log "Verified: $($loadedModules.Count) VMware modules already loaded" "Success"
        }
    }
    
    Write-Log "========================================" "Info"
    Write-Log "Attempting connection to vCenter..." "Info"
    
    # Test network connectivity first
    Write-Log "Testing network connectivity to $VCenterServer..." "Debug"
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $connectTask = $tcpClient.ConnectAsync($VCenterServer, 443)
        if ($connectTask.Wait(3000)) {
            if ($tcpClient.Connected) {
                Write-Log "Port 443 is reachable on $VCenterServer" "Debug"
                $tcpClient.Close()
            }
        }
        else {
            Write-Log "Connection to port 443 timed out (may still work)" "Warning"
        }
    }
    catch {
        Write-Log "Could not test port connectivity: $_" "Debug"
    }
    
    # Connect to vCenter with timing
    $connectionResult = Measure-Duration -OperationName "vCenter Connection" -ScriptBlock {
        Write-Log "Executing Connect-VIServer..." "Debug"
        
        try {
            $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
            return $connection
        }
        catch {
            Write-Log "Connection attempt failed: $($_.Exception.GetType().FullName)" "Error"
            Write-Log "Error Message: $($_.Exception.Message)" "Error"
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
    elseif ($errorMsg -match "No valid credentials") {
        Write-Output "Failure: No credentials provided"
    }
    else {
        Write-Output "Failure: $errorMsg"
    }
    
    exit 1
}
finally {
    Write-Log "Script execution completed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')" "Debug"
    Stop-ScriptLogging -Success $true
    # Clear any sensitive variables
    if ($Password) { $Password = $null }
    if ($securePassword) { $securePassword = $null }
}