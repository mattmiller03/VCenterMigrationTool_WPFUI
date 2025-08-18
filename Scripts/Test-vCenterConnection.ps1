# Test-vCenterConnection.ps1
# Enhanced with standardized logging framework

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
    [string]$LogPath = "",
    
    # Enhanced logging parameters
    [ValidateSet('Debug', 'Verbose', 'Info', 'Warning', 'Error', 'Critical')]
    [string]$LogLevel = 'Info',
    
    [switch]$DisableConsoleOutput,
    [switch]$IncludeStackTrace
)

# Import the enhanced logging framework
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Initialize logging with proper LogPath parameter
$loggingParams = @{
    ScriptName = 'Test-vCenterConnection'
}

# Pass the LogPath if provided (this is the key fix)
if ($LogPath) {
    $loggingParams.LogPath = $LogPath
}

Start-ScriptLogging @loggingParams

# Track statistics
$script:Statistics = @{
    'Connection Tests' = 0
    'Successful Connections' = 0
    'Failed Connections' = 0
    'Module Import Time' = 0
    'Connection Time' = 0
}

try {
    Write-LogInfo "Starting vCenter connection test" -Category "Initialization"
    Write-LogInfo "Target vCenter Server: $VCenterServer" -Category "Configuration"
    Write-LogInfo "Bypass Module Check: $BypassModuleCheck" -Category "Configuration"
    Write-LogDebug "PowerShell Version: $($PSVersionTable.PSVersion)" -Category "Environment"
    Write-LogDebug "PowerShell Edition: $($PSVersionTable.PSEdition)" -Category "Environment"
    
    # Determine credential source and create PSCredential if needed
    Write-LogDebug "Processing authentication credentials" -Category "Authentication"
    
    if ($Credential) {
        Write-LogDebug "Using provided PSCredential object" -Category "Authentication"
        Write-LogInfo "Username from credential: $($Credential.UserName)" -Category "Authentication"
    }
    elseif ($Username -and $Password) {
        Write-LogDebug "Creating PSCredential from Username/Password parameters" -Category "Authentication"
        Write-LogInfo "Username: $Username" -Category "Authentication"
        
        # Convert plain text password to SecureString and create credential
        $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
        $Credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
        
        Write-LogSuccess "PSCredential created successfully" -Category "Authentication"
        
        # Clear the plain text password from memory
        $Password = $null
    }
    else {
        $errorMsg = "No valid credentials provided. Either provide -Credential or both -Username and -Password"
        Write-LogError $errorMsg -Category "Authentication"
        throw $errorMsg
    }
    
    # Check PowerCLI installation status
    Write-LogDebug "Checking PowerCLI installation..." -Category "PowerCLI"
    $powerCLIModules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    
    if ($powerCLIModules) {
        Write-LogSuccess "PowerCLI found: Version $($powerCLIModules[0].Version)" -Category "PowerCLI"
    }
    else {
        Write-LogWarning "PowerCLI modules not found in available modules" -Category "PowerCLI"
    }
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Module bypass NOT enabled - importing PowerCLI modules" -Category "PowerCLI"
        
        $moduleImportStart = Get-Date
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            
            Write-LogDebug "Setting PowerCLI configuration..." -Category "PowerCLI"
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
            
            # Log loaded VMware modules
            $loadedModules = Get-Module -Name "VMware.*"
            Write-LogSuccess "Successfully loaded $($loadedModules.Count) VMware modules" -Category "PowerCLI"
            
            $moduleImportTime = (Get-Date) - $moduleImportStart
            $script:Statistics['Module Import Time'] = [math]::Round($moduleImportTime.TotalSeconds, 2)
        }
        catch {
            Write-LogCritical "PowerCLI module import failed: $($_.Exception.Message)" -Category "PowerCLI"
            Write-Output "Failure: PowerCLI module import failed"
            exit 1
        }
    }
    else {
        Write-LogInfo "Module bypass ENABLED - skipping PowerCLI import" -Category "PowerCLI"
        Write-LogDebug "Assuming PowerCLI modules are already loaded" -Category "PowerCLI"
        
        # Verify modules are actually loaded when bypassing
        $loadedModules = Get-Module -Name "VMware.*"
        if ($loadedModules.Count -eq 0) {
            Write-LogWarning "BypassModuleCheck is true but no VMware modules are loaded!" -Category "PowerCLI"
            
            # Check if Connect-VIServer command is available
            $cmdAvailable = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
            if (-not $cmdAvailable) {
                $errorMsg = "Connect-VIServer command not found - PowerCLI not properly initialized"
                Write-LogError $errorMsg -Category "PowerCLI"
                Write-Output "Failure: PowerCLI not available despite bypass flag"
                exit 1
            }
            else {
                Write-LogDebug "Connect-VIServer command is available" -Category "PowerCLI"
            }
        }
        else {
            Write-LogSuccess "Verified: $($loadedModules.Count) VMware modules already loaded" -Category "PowerCLI"
        }
    }
    
    Write-LogInfo "Attempting connection to vCenter..." -Category "Connection"
    
    # Test network connectivity first
    Write-LogDebug "Testing network connectivity to $VCenterServer..." -Category "Network"
    try {
        $tcpClient = New-Object System.Net.Sockets.TcpClient
        $connectTask = $tcpClient.ConnectAsync($VCenterServer, 443)
        if ($connectTask.Wait(3000)) {
            if ($tcpClient.Connected) {
                $tcpClient.Close()
                Write-LogSuccess "Port 443 is reachable on $VCenterServer" -Category "Network"
            }
        } else {
            Write-LogWarning "Connection to port 443 timed out (may still work)" -Category "Network"
        }
    }
    catch {
        Write-LogDebug "Could not test port connectivity: $_" -Category "Network"
    }
    
    # Connect to vCenter with timing
    $script:Statistics['Connection Tests']++
    
    $connectionStart = Get-Date
    try {
        Write-LogDebug "Executing Connect-VIServer..." -Category "Connection"
        
        $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force -ErrorAction Stop
        
        $connectionTime = (Get-Date) - $connectionStart
        $script:Statistics['Connection Time'] = [math]::Round($connectionTime.TotalSeconds, 2)
        
        if ($connection -and $connection.IsConnected) {
            $script:Statistics['Successful Connections']++
            
            Write-LogSuccess "CONNECTION SUCCESSFUL!" -Category "Connection"
            
            # Log connection details
            Write-LogInfo "vCenter Server: $($connection.Name)" -Category "Connection"
            Write-LogInfo "vCenter Version: $($connection.Version)" -Category "Connection"
            Write-LogInfo "vCenter Build: $($connection.Build)" -Category "Connection"
            Write-LogInfo "Session ID: $($connection.SessionId)" -Category "Connection"
            Write-LogInfo "Connected User: $($connection.User)" -Category "Connection"
            
            # Disconnect cleanly
            Write-LogInfo "Disconnecting from vCenter..." -Category "Connection"
            Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
            Write-LogSuccess "Disconnected successfully" -Category "Connection"
            
            Write-LogSuccess "Test completed successfully" -Category "Summary"
            
            # Return success in a format the app expects
            Write-Output "Success: Connection test passed"
            exit 0
        } 
        else {
            $script:Statistics['Failed Connections']++
            $errorMsg = "Connection object was created but IsConnected is false"
            Write-LogError $errorMsg -Category "Connection"
            Write-Output "Failure: Connection not established"
            exit 1
        }
    }
    catch {
        $connectionTime = (Get-Date) - $connectionStart
        $script:Statistics['Connection Time'] = [math]::Round($connectionTime.TotalSeconds, 2)
        throw
    }
}
catch {
    $script:Statistics['Failed Connections']++
    
    Write-LogCritical "CONNECTION TEST FAILED" -Category "Connection"
    
    $errorMsg = $_.Exception.Message
    Write-LogError "Exception Type: $($_.Exception.GetType().FullName)" -Category "Error"
    Write-LogError "Error Message: $errorMsg" -Category "Error"
    
    if ($IncludeStackTrace) {
        Write-LogDebug "Stack Trace: $($_.ScriptStackTrace)" -Category "Error"
    }
    
    # Provide user-friendly error message
    $userFriendlyMessage = switch -Regex ($errorMsg) {
        "Invalid username or password" { "Failure: Authentication failed - Invalid credentials" }
        "Could not resolve" { "Failure: Could not resolve server address" }
        "PowerCLI" { "Failure: PowerCLI module error" }
        "No valid credentials" { "Failure: No credentials provided" }
        "timeout|timed out" { "Failure: Connection timeout" }
        "certificate" { "Failure: SSL certificate error" }
        default { "Failure: $errorMsg" }
    }
    
    Write-Output $userFriendlyMessage
    exit 1
}
finally {
    # Clear any sensitive variables
    if ($Password) { $Password = $null }
    if ($securePassword) { $securePassword = $null }
    
    # Finalize logging with statistics
    $success = $script:Statistics['Successful Connections'] -gt 0
    $summary = if ($success) {
        "Connection test passed - Connected successfully to $VCenterServer"
    } else {
        "Connection test failed for $VCenterServer"
    }
    
    Stop-ScriptLogging -Success $success -Summary $summary -Statistics $script:Statistics
}