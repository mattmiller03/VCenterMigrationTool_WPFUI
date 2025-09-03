using System;
using System.Text;
using VCenterMigrationTool.Models;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Builds PowerShell scripts for vCenter operations with consistent logging and error handling
/// </summary>
public class PowerShellScriptBuilder
{
    /// <summary>
    /// Creates a script that assumes PowerCLI modules are managed by service layer
    /// </summary>
    public static string BuildPowerCLIImportScript()
    {
        return @"
# ===== POWERCLI MODULES MANAGED BY SERVICE LAYER =====
Write-Output 'DIAGNOSTIC: PowerCLI modules are managed by service layer'
Write-Output 'PROGRESS: PowerCLI import - Assuming modules are available'

# PowerCLI modules are managed by the application service layer
# No module import/installation attempts are made at this level
$moduleType = 'Service Layer Managed'
$importSuccess = $true

Write-Output 'DIAGNOSTIC: Trusting service layer module management'
Write-Output ""DIAGNOSTIC: PowerCLI module management delegated to service layer""
Write-Output ""MODULES_LOADED:$moduleType""";
    }

    /// <summary>
    /// Creates a script for configuring PowerCLI settings
    /// </summary>
    public static string BuildPowerCLIConfigurationScript(string moduleType)
    {
        return $@"
# ===== POWERCLI CONFIGURATION (SERVICE LAYER MANAGED) =====
Write-Output ""DIAGNOSTIC: PowerCLI configuration for {moduleType} is managed by service layer""

# PowerCLI configuration is assumed to be handled by the service layer
# No PowerCLI commands are executed at this level to avoid timeouts
Write-Output ""DIAGNOSTIC: Skipping Set-PowerCLIConfiguration calls - managed by service layer""
Write-Output ""DIAGNOSTIC: Trusting service layer for PowerCLI SSL/TLS and connection settings""

# Only configure .NET level SSL/TLS settings that don't require PowerCLI modules
try {{
    # Force TLS 1.2+ for secure connections
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
    
    # Disable SSL certificate validation at .NET level
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {{$true}}
    
    Write-Output ""DIAGNOSTIC: .NET SSL/TLS configuration applied for {moduleType}""
}} catch {{
    Write-Output ""DIAGNOSTIC: .NET SSL configuration failed for {moduleType}, but continuing""
}}

Write-Output ""DIAGNOSTIC: PowerCLI configuration completed for {moduleType} (service layer managed)""
Write-Output ""CONFIG_SUCCESS""";
    }

    /// <summary>
    /// Creates a comprehensive vCenter connection script with ultra-verbose debugging
    /// </summary>
    public static string BuildVCenterConnectionScript(VCenterConnection connectionInfo, string password, string connectionKey)
    {
        var escapedPassword = password.Replace("'", "''");
        var escapedUsername = connectionInfo.Username.Replace("'", "''");

        return $@"
# ===== ULTRA-VERBOSE POWERCLI CONNECTION DEBUGGING =====
Write-Output ""DEBUG_START: Beginning PowerCLI connection sequence for {connectionKey}""
Write-Output ""DEBUG_TIMESTAMP: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
Write-Output ""DEBUG_POWERSHELL_VERSION: $($PSVersionTable.PSVersion)""
Write-Output ""DEBUG_EXECUTION_POLICY: $(Get-ExecutionPolicy)""

# Environment diagnostics
Write-Output ""DEBUG_ENV: Current working directory: $(Get-Location)""
Write-Output ""DEBUG_ENV: PowerShell process ID: $PID""
Write-Output ""DEBUG_ENV: Current user: $($env:USERNAME)""
Write-Output ""DEBUG_ENV: Domain: $($env:USERDOMAIN)""

{BuildModuleStatusCheckScript()}

{BuildCommandAvailabilityCheckScript()}

{BuildConfigurationDiagnosticsScript()}

{BuildCredentialCreationScript(escapedUsername, escapedPassword)}

{BuildPreConnectionTestsScript(connectionInfo.ServerAddress)}

{BuildConnectionExecutionScript(connectionInfo.ServerAddress, connectionKey)}

Write-Output ""DEBUG_END: PowerCLI connection sequence completed for {connectionKey}""";
    }

    /// <summary>
    /// Creates script section for checking PowerCLI module status
    /// </summary>
    private static string BuildModuleStatusCheckScript()
    {
        return @"
# Check PowerCLI module availability and status
Write-Output ""DEBUG_MODULES: Checking PowerCLI module availability""
try {
    $loadedModules = Get-Module -Name VMware* -ErrorAction SilentlyContinue
    
    if ($loadedModules) {
        Write-Output ""DEBUG_MODULES: Currently loaded VMware modules: $($loadedModules.Name -join ', ')""
        $loadedModules | ForEach-Object {
            Write-Output ""DEBUG_MODULES: - $($_.Name) v$($_.Version) from $($_.ModuleBase)""
        }
    } else {
        Write-Output ""DEBUG_MODULES: No VMware modules currently loaded""
        Write-Output ""DEBUG_MODULES: PowerCLI modules assumed to be managed by service layer""
    }
} catch {
    Write-Output ""DEBUG_MODULES: Module check failed: $($_.Exception.Message)""
}";
    }

    /// <summary>
    /// Creates script section for checking PowerCLI command availability
    /// </summary>
    private static string BuildCommandAvailabilityCheckScript()
    {
        return @"
# Check available PowerCLI commands
Write-Output ""DEBUG_COMMANDS: Checking PowerCLI command availability""
try {
    $connectCmd = Get-Command Connect-VIServer -ErrorAction SilentlyContinue
    $getServerCmd = Get-Command Get-VIServer -ErrorAction SilentlyContinue
    $configCmd = Get-Command Set-PowerCLIConfiguration -ErrorAction SilentlyContinue
    
    Write-Output ""DEBUG_COMMANDS: Connect-VIServer available: $(if ($connectCmd) { 'YES' } else { 'NO' })""
    Write-Output ""DEBUG_COMMANDS: Get-VIServer available: $(if ($getServerCmd) { 'YES' } else { 'NO' })""
    Write-Output ""DEBUG_COMMANDS: Set-PowerCLIConfiguration available: $(if ($configCmd) { 'YES' } else { 'NO' })""
    
    if ($connectCmd) {
        Write-Output ""DEBUG_COMMANDS: Connect-VIServer source: $($connectCmd.Source)""
        Write-Output ""DEBUG_COMMANDS: Connect-VIServer version: $($connectCmd.Version)""
    }
} catch {
    Write-Output ""DEBUG_COMMANDS: Command check failed: $($_.Exception.Message)""
}";
    }

    /// <summary>
    /// Creates script section for PowerCLI configuration diagnostics
    /// </summary>
    private static string BuildConfigurationDiagnosticsScript()
    {
        return @"
# PowerCLI configuration diagnostics (service layer managed)
Write-Output ""DEBUG_CONFIG: PowerCLI configuration managed by service layer""
try {
    Write-Output ""DEBUG_CONFIG: Skipping Get-PowerCLIConfiguration - managed by service layer""
    Write-Output ""DEBUG_CONFIG: Skipping Set-PowerCLIConfiguration calls - managed by service layer""
    
    # Only configure .NET level settings that don't require PowerCLI modules
    Write-Output ""DEBUG_CONFIG: Applying .NET SSL/TLS settings""
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
    
    Write-Output ""DEBUG_CONFIG: .NET SSL/TLS configuration applied successfully""
    Write-Output ""DEBUG_CONFIG: PowerCLI settings assumed to be configured by service layer""
    
} catch {
    Write-Output ""DEBUG_CONFIG: .NET configuration failed: $($_.Exception.Message)""
    Write-Output ""DEBUG_CONFIG: Exception type: $($_.Exception.GetType().Name)""
}";
    }

    /// <summary>
    /// Creates script section for credential creation with diagnostics
    /// </summary>
    private static string BuildCredentialCreationScript(string username, string password)
    {
        return $@"
# Credential creation diagnostics
Write-Output ""DEBUG_CRED: Creating PowerShell credential object""
try {{
    Write-Output ""DEBUG_CRED: Target username: {username}""
    Write-Output ""DEBUG_CRED: Password length: $('{password}'.Length) characters""
    
    $password = '{password}'
    Write-Output ""DEBUG_CRED: Password variable created successfully""
    
    $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
    Write-Output ""DEBUG_CRED: SecureString created successfully""
    
    $credential = New-Object System.Management.Automation.PSCredential('{username}', $securePassword)
    Write-Output ""DEBUG_CRED: PSCredential object created successfully""
    Write-Output ""DEBUG_CRED: Credential username: $($credential.UserName)""
    Write-Output ""DEBUG_CRED: Credential has password: $(if ($credential.Password) {{ 'YES' }} else {{ 'NO' }})""
    
    $password = $null  # Clear from memory
    Write-Output ""DEBUG_CRED: Password variable cleared from memory""
}} catch {{
    Write-Output ""DEBUG_CRED: Credential creation failed: $($_.Exception.Message)""
    Write-Output ""DEBUG_CRED: Exception type: $($_.Exception.GetType().Name)""
    return
}}";
    }

    /// <summary>
    /// Creates script section for pre-connection environment tests
    /// </summary>
    private static string BuildPreConnectionTestsScript(string serverAddress)
    {
        return $@"
# Pre-connection environment check
Write-Output ""DEBUG_PRECON: Checking pre-connection environment""
try {{
    # Check existing connections
    $existingConnections = Get-VIServer -ErrorAction SilentlyContinue
    if ($existingConnections) {{
        Write-Output ""DEBUG_PRECON: Found $($existingConnections.Count) existing vCenter connections""
        $existingConnections | ForEach-Object {{
            Write-Output ""DEBUG_PRECON: - Server: $($_.Name) | Connected: $($_.IsConnected) | Version: $($_.Version) | User: $($_.User)""
        }}
    }} else {{
        Write-Output ""DEBUG_PRECON: No existing vCenter connections found""
    }}
    
    # Network connectivity test
    Write-Output ""DEBUG_PRECON: Testing network connectivity to {serverAddress}:443""
    $ping = Test-NetConnection -ComputerName '{serverAddress}' -Port 443 -WarningAction SilentlyContinue -ErrorAction SilentlyContinue
    if ($ping) {{
        Write-Output ""DEBUG_PRECON: Network test - TcpTestSucceeded: $($ping.TcpTestSucceeded)""
        Write-Output ""DEBUG_PRECON: Network test - RemoteAddress: $($ping.RemoteAddress)""
        Write-Output ""DEBUG_PRECON: Network test - InterfaceAlias: $($ping.InterfaceAlias)""
        Write-Output ""DEBUG_PRECON: Network test - PingSucceeded: $($ping.PingSucceeded)""
    }} else {{
        Write-Output ""DEBUG_PRECON: Network test failed - no response object""
    }}
}} catch {{
    Write-Output ""DEBUG_PRECON: Pre-connection check failed: $($_.Exception.Message)""
}}";
    }

    /// <summary>
    /// Creates script section for the actual Connect-VIServer execution with error capture
    /// </summary>
    private static string BuildConnectionExecutionScript(string serverAddress, string connectionKey)
    {
        return $@"
# THE ACTUAL CONNECTION ATTEMPT WITH MAXIMUM DIAGNOSTICS
Write-Output ""DEBUG_CONNECT: ===== STARTING CONNECT-VISERVER ATTEMPT =====""""
Write-Output ""DEBUG_CONNECT: Connection timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
try {{
    Write-Output ""DEBUG_CONNECT: Calling Connect-VIServer with parameters:""
    Write-Output ""DEBUG_CONNECT: - Server: {serverAddress}""
    Write-Output ""DEBUG_CONNECT: - User: {connectionKey}""
    Write-Output ""DEBUG_CONNECT: - Force: True""
    Write-Output ""DEBUG_CONNECT: - ErrorAction: Stop""
    
    # Execute the connection command with controlled error capture
    Write-Output ""DEBUG_CONNECT: Executing Connect-VIServer now...""
    $connectionError = $null
    $connection = $null
    
    try {{
        # Capture both output and error streams
        Write-Output ""DEBUG_CONNECT: Calling Connect-VIServer with error variable capture""
        $connection = Connect-VIServer -Server '{serverAddress}' -Credential $credential -Force -ErrorAction Stop -ErrorVariable connectionError 2>&1
        Write-Output ""DEBUG_CONNECT: Connect-VIServer command completed without throwing exception""
        Write-Output ""DEBUG_CONNECT: Connection variable type: $($connection.GetType().FullName)""
    }}
    catch {{
        Write-Output ""DEBUG_CONNECT: Connect-VIServer threw exception during execution""
        $connectionError = $_.Exception
        Write-Output ""DEBUG_CONNECT: Exception captured in variable: $($connectionError.GetType().FullName)""
    }}
    
    # Analyze error variable content if populated
    if ($connectionError) {{
        Write-Output ""DEBUG_ERROR_VAR: Connection error variable is populated""
        Write-Output ""DEBUG_ERROR_VAR: Error variable type: $($connectionError.GetType().FullName)""
        
        if ($connectionError -is [System.Exception]) {{
            Write-Output ""DEBUG_ERROR_VAR: Error is Exception type""
            Write-Output ""DEBUG_ERROR_VAR: Exception message: $($connectionError.Message)""
            Write-Output ""DEBUG_ERROR_VAR: Exception type: $($connectionError.GetType().Name)""
            
            if ($connectionError.InnerException) {{
                Write-Output ""DEBUG_ERROR_VAR: Inner exception: $($connectionError.InnerException.Message)""
                Write-Output ""DEBUG_ERROR_VAR: Inner exception type: $($connectionError.InnerException.GetType().Name)""
            }}
        }}
        elseif ($connectionError -is [System.Management.Automation.ErrorRecord]) {{
            Write-Output ""DEBUG_ERROR_VAR: Error is ErrorRecord type""
            Write-Output ""DEBUG_ERROR_VAR: ErrorRecord message: $($connectionError.Exception.Message)""
            Write-Output ""DEBUG_ERROR_VAR: ErrorRecord category: $($connectionError.CategoryInfo.Category)""
            Write-Output ""DEBUG_ERROR_VAR: ErrorRecord reason: $($connectionError.CategoryInfo.Reason)""
            Write-Output ""DEBUG_ERROR_VAR: ErrorRecord target: $($connectionError.TargetObject)""
            Write-Output ""DEBUG_ERROR_VAR: ErrorRecord script stack trace: $($connectionError.ScriptStackTrace)""
        }}
        elseif ($connectionError -is [Array] -and $connectionError.Count -gt 0) {{
            Write-Output ""DEBUG_ERROR_VAR: Error variable contains array with $($connectionError.Count) items""
            for ($i = 0; $i -lt $connectionError.Count; $i++) {{
                $errorItem = $connectionError[$i]
                Write-Output ""DEBUG_ERROR_VAR: Array item [$i] type: $($errorItem.GetType().FullName)""
                Write-Output ""DEBUG_ERROR_VAR: Array item [$i] content: $errorItem""
            }}
        }}
        else {{
            Write-Output ""DEBUG_ERROR_VAR: Error variable content (string): $connectionError""
        }}
    }} else {{
        Write-Output ""DEBUG_ERROR_VAR: No connection error captured in variable""
    }}
    
    Write-Output ""DEBUG_CONNECT: Post-execution analysis complete""
    
    # Analyze the connection result with enhanced error context
    if ($connection) {{
        # Filter out non-connection objects from the result (sometimes includes error messages)
        $actualConnection = $null
        if ($connection -is [Array]) {{
            Write-Output ""DEBUG_CONNECT: Connection result is an array with $($connection.Count) items""
            foreach ($item in $connection) {{
                Write-Output ""DEBUG_CONNECT: Array item type: $($item.GetType().FullName)""
                if ($item.GetType().Name -eq 'VIServer') {{
                    $actualConnection = $item
                    Write-Output ""DEBUG_CONNECT: Found VIServer object in array""
                }}
            }}
        }}
        elseif ($connection.GetType().Name -eq 'VIServer') {{
            $actualConnection = $connection
            Write-Output ""DEBUG_CONNECT: Direct VIServer object returned""
        }}
        else {{
            Write-Output ""DEBUG_CONNECT: Unexpected connection type: $($connection.GetType().FullName)""
            Write-Output ""DEBUG_CONNECT: Connection content: $connection""
        }}
        
        if ($actualConnection) {{
            Write-Output ""DEBUG_CONNECT: Connection object returned: YES""
            Write-Output ""DEBUG_CONNECT: Connection type: $($actualConnection.GetType().Name)""
            Write-Output ""DEBUG_CONNECT: Connection IsConnected: $($actualConnection.IsConnected)""
        
        if ($actualConnection.IsConnected) {{
            Write-Output ""DEBUG_CONNECT: ✅ CONNECTION SUCCESSFUL!""
            Write-Output ""CONNECTION_SUCCESS""
            Write-Output ""SESSION_ID:$($actualConnection.SessionId)""
            Write-Output ""VERSION:$($actualConnection.Version)""
            Write-Output ""BUILD:$($actualConnection.Build)""
            Write-Output ""USER:$($actualConnection.User)""
            Write-Output ""PORT:$($actualConnection.Port)""
            Write-Output ""PRODUCT_LINE:$($actualConnection.ProductLine)""
            
            # Store in global variables
            $global:VIConnection_{connectionKey.Replace("-", "_")} = $actualConnection
            Write-Output ""DEBUG_CONNECT: Stored in global variable: VIConnection_{connectionKey.Replace("-", "_")}""
            
            # Update default server
            if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {{
                $global:PreviousDefaultVIServer = $global:DefaultVIServer
                Write-Output ""DEBUG_CONNECT: Saved previous default server: $($global:PreviousDefaultVIServer.Name)""
            }}
            $global:DefaultVIServer = $actualConnection
            $global:CurrentVCenterConnection = $actualConnection
            Write-Output ""DEBUG_CONNECT: Set as new default server""
            
            # Verify storage
            $storedConnection = $global:VIConnection_{connectionKey.Replace("-", "_")}
            Write-Output ""DEBUG_CONNECT: Verification - stored connection exists: $(if ($storedConnection) {{ 'YES' }} else {{ 'NO' }})""
            if ($storedConnection) {{
                Write-Output ""DEBUG_CONNECT: Verification - stored connection IsConnected: $($storedConnection.IsConnected)""
            }}
            
            Write-Output ""CONNECTION_VERIFIED""
        }} else {{
            Write-Output ""DEBUG_CONNECT: ❌ Connection object returned but IsConnected = False""
            
            # Check if we have error information to explain why IsConnected is false
            if ($connectionError) {{
                Write-Output ""CONNECTION_FAILED: Connection object indicates not connected - Error: $connectionError""
            }} else {{
                Write-Output ""CONNECTION_FAILED: Connection object indicates not connected - No specific error captured""
            }}
        }}
        }} else {{
            Write-Output ""DEBUG_CONNECT: ❌ Connection result contains no VIServer object""
            if ($connectionError) {{
                Write-Output ""CONNECTION_FAILED: No VIServer object found - Error: $connectionError""
            }} else {{
                Write-Output ""CONNECTION_FAILED: No VIServer object found in result""
            }}
        }}
    }} else {{
        Write-Output ""DEBUG_CONNECT: ❌ No connection result returned""
        if ($connectionError) {{
            Write-Output ""CONNECTION_FAILED: No connection result - Error: $connectionError""
        }} else {{
            Write-Output ""CONNECTION_FAILED: No connection result returned""
        }}
    }}
    
}} catch {{
    Write-Output ""DEBUG_CONNECT: ❌ CONNECT-VISERVER THREW EXCEPTION""
    Write-Output ""CONNECTION_FAILED: $($_.Exception.Message)""
    
    # Ultra-detailed exception analysis
    Write-Output ""DEBUG_EXCEPTION: Exception type: $($_.Exception.GetType().FullName)""
    Write-Output ""DEBUG_EXCEPTION: Exception message: $($_.Exception.Message)""
    
    if ($_.Exception.InnerException) {{
        Write-Output ""DEBUG_EXCEPTION: Inner exception type: $($_.Exception.InnerException.GetType().FullName)""
        Write-Output ""DEBUG_EXCEPTION: Inner exception message: $($_.Exception.InnerException.Message)""
    }}
    
    Write-Output ""DEBUG_EXCEPTION: Stack trace:""
    Write-Output $_.Exception.StackTrace
    
    # Category analysis
    Write-Output ""DEBUG_EXCEPTION: Error category: $($_.CategoryInfo.Category)""
    Write-Output ""DEBUG_EXCEPTION: Error reason: $($_.CategoryInfo.Reason)""
    Write-Output ""DEBUG_EXCEPTION: Error target name: $($_.CategoryInfo.TargetName)""
    Write-Output ""DEBUG_EXCEPTION: Error target type: $($_.CategoryInfo.TargetType)""
    
    # Specific error pattern analysis
    $errorMsg = $_.Exception.Message
    if ($errorMsg -like '*certificate*' -or $errorMsg -like '*SSL*' -or $errorMsg -like '*TLS*') {{
        Write-Output ""DEBUG_ANALYSIS: 🔒 CERTIFICATE/SSL ISSUE DETECTED""
        Write-Output ""DEBUG_ANALYSIS: Current InvalidCertificateAction should be 'Ignore'""
        Write-Output ""DEBUG_ANALYSIS: This may be a PowerCLI SSL bypass failure""
    }} elseif ($errorMsg -like '*authentication*' -or $errorMsg -like '*login*' -or $errorMsg -like '*credential*' -or $errorMsg -like '*password*') {{
        Write-Output ""DEBUG_ANALYSIS: 🔑 AUTHENTICATION ISSUE DETECTED""
        Write-Output ""DEBUG_ANALYSIS: Verify username/password are correct""
        Write-Output ""DEBUG_ANALYSIS: Check if account is locked or password expired""
    }} elseif ($errorMsg -like '*timeout*' -or $errorMsg -like '*connection*' -or $errorMsg -like '*network*') {{
        Write-Output ""DEBUG_ANALYSIS: 🌐 NETWORK/TIMEOUT ISSUE DETECTED""
        Write-Output ""DEBUG_ANALYSIS: Check network connectivity and firewall settings""
    }} elseif ($errorMsg -like '*session*' -or $errorMsg -like '*token*') {{
        Write-Output ""DEBUG_ANALYSIS: 🎫 SESSION/TOKEN ISSUE DETECTED""
        Write-Output ""DEBUG_ANALYSIS: This may be a vCenter session limit or token issue""
    }} else {{
        Write-Output ""DEBUG_ANALYSIS: ❓ UNKNOWN ERROR PATTERN""
        Write-Output ""DEBUG_ANALYSIS: This may be a new or unusual connection issue""
    }}
    
    # Final diagnostics
    Write-Output ""DEBUG_FINAL: Connection attempt failed at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')""
    Write-Output ""DEBUG_FINAL: PowerShell session will remain active for further debugging""
}}";
    }

    /// <summary>
    /// Creates a script wrapper with command execution tracking
    /// </summary>
    public static string BuildScriptWithEndMarker(string script, string endMarker)
    {
        return $@"
# Script execution wrapper with end marker
try {{
    {script}
}} catch {{
    Write-Output ""SCRIPT_ERROR: $($_.Exception.Message)""
}}
Write-Output '{endMarker}'";
    }

    /// <summary>
    /// Creates a script for connection validation within a persistent session
    /// </summary>
    public static string BuildConnectionValidationScript(string connectionKey)
    {
        var connectionVarName = $"VIConnection_{connectionKey.Replace("-", "_")}";
        
        return $@"
# Ensure correct vCenter connection is active for this session
if ($global:{connectionVarName} -and $global:{connectionVarName}.IsConnected) {{
    $global:DefaultVIServer = $global:{connectionVarName}
    $global:CurrentVCenterConnection = $global:{connectionVarName}
    Write-Output ""CONNECTION_ACTIVE: {connectionKey}""
}} else {{
    Write-Output ""CONNECTION_INACTIVE: No active connection for {connectionKey}""
}}";
    }
}