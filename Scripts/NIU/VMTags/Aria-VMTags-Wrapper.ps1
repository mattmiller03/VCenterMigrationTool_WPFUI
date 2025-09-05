<#
.SYNOPSIS
    Aria Operations wrapper for VM Tags and Permissions automation
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('DEV', 'PROD', 'KLEB', 'OT')]
    [string]$Environment,
    
    [Parameter(Mandatory = $false)]
    [string]$vCenterServer,
    
    [Parameter(Mandatory = $false)]
    [switch]$EnableDebug
)

#region Aria Operations Functions
function Write-AriaLog {
    <#
    .SYNOPSIS
        Writes log entries in Aria Operations compatible format
    .PARAMETER Message
        Log message
    .PARAMETER Level
        Log level (INFO, WARNING, ERROR, SUCCESS)
    .PARAMETER Category
        Log category for Aria classification
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [Parameter(Mandatory = $false)]
        [ValidateSet('INFO', 'WARNING', 'ERROR', 'SUCCESS', 'DEBUG')]
        [string]$Level = "INFO",
        
        [Parameter(Mandatory = $false)]
        [string]$Category = "VMTags-Automation"
    )
    
    $ariaLogEntry = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        Level = $Level
        Source = $Category
        Environment = $script:Environment
        Message = $Message
        Machine = $env:COMPUTERNAME
        User = $env:USERNAME
        ProcessId = $PID
        ScriptName = $MyInvocation.ScriptName
    }
    
    # Output for Aria Operations to parse
    $jsonLog = $ariaLogEntry | ConvertTo-Json -Compress
    Write-Host $jsonLog
    
    # Also write to Aria-specific log file
    try {
        $ariaLogFile = "C:\Scripts\VMTags\Logs\Aria\AriaOperations_$script:Environment.log"
        $logLine = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$Level] $Message"
        Add-Content -Path $ariaLogFile -Value $logLine -ErrorAction SilentlyContinue
    }
    catch {
        # Ignore file logging errors in Aria context
    }
}

function Write-AriaResult {
    <#
    .SYNOPSIS
        Writes execution results in Aria Operations format
    #>
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Result
    )
    
    $ariaResult = @{
        Timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ"
        Environment = $script:Environment
        ExecutionStatus = if ($Result.ExitCode -eq 0) { "SUCCESS" } else { "FAILED" }
        ExitCode = $Result.ExitCode
        ExecutionTime = $Result.ExecutionTime
        Machine = $env:COMPUTERNAME
        User = $env:USERNAME
        Details = @{
            vCenterServer = $script:vCenterServer
            ConfigPath = $script:ConfigPath
            LauncherScript = $script:LauncherScript
        }
    }
    
    # Output structured result for Aria
    Write-Host "ARIA_RESULT: $($ariaResult | ConvertTo-Json -Compress)"
}
#endregion

#region Main Execution
$script:Environment = $Environment

# Aria Operations specific settings
$AriaConfig = @{
    LauncherScript = "C:\Temp\Scripts\VMTags\VM_TagPermissions_Launcher_v2.ps1"
    ConfigPath = "C:\Temp\Scripts\VMTags\ConfigFiles"
    LogPath = "C:\Temp\Scripts\VMTags\Logs\Aria"
    TempPath = "C:\Temp\Scripts\VMTags\Temp"
}

try {
    Write-AriaLog "Aria Operations VM Tags automation started" -Level "INFO"
    Write-AriaLog "PowerShell Version: $($PSVersionTable.PSVersion)" -Level "INFO"
    Write-AriaLog "Target Environment: $Environment" -Level "INFO"
    
    if ($vCenterServer) {
        Write-AriaLog "vCenter Override: $vCenterServer" -Level "INFO"
    }
    
    # Ensure required directories exist
    @($AriaConfig.LogPath, $AriaConfig.TempPath) | ForEach-Object {
        if (-not (Test-Path $_)) {
            New-Item -Path $_ -ItemType Directory -Force | Out-Null
            Write-AriaLog "Created directory: $_" -Level "INFO"
        }
    }
    
    # Check if launcher script exists
    if (-not (Test-Path $AriaConfig.LauncherScript)) {
        Write-AriaLog "Launcher script not found: $($AriaConfig.LauncherScript)" -Level "ERROR"
        throw "Launcher script not found: $($AriaConfig.LauncherScript)"
    }
    
    # Build launcher arguments using hashtable for proper splatting
    $launcherArgs = @{
        Environment = $Environment
        ConfigPath = $AriaConfig.ConfigPath
    }

    if ($vCenterServer) {
        $launcherArgs.OverrideVCenter = $vCenterServer
    }

    if ($EnableDebug) {
        $launcherArgs.ForceDebug = $true
    }

    # Log the command being executed
    $argString = ($launcherArgs.GetEnumerator() | ForEach-Object { "-$($_.Key) $($_.Value)" }) -join ' '
    Write-AriaLog "Launching VM Tags automation with args: $argString" -Level "INFO"

    # Execute the launcher script with proper splatting
    $result = & $AriaConfig.LauncherScript @launcherArgs
    
    # Process results
    if ($result -and $result.ExitCode -ne $null) {
        Write-AriaResult -Result $result
        
        if ($result.ExitCode -eq 0) {
            Write-AriaLog "VM Tags automation completed successfully" -Level "SUCCESS"
        } else {
            Write-AriaLog "VM Tags automation completed with errors (Exit Code: $($result.ExitCode))" -Level "WARNING"
        }
    } else {
        Write-AriaLog "VM Tags automation completed (no result object returned)" -Level "INFO"
    }
    
    return $result
}
catch {
    Write-AriaLog "FATAL ERROR: $($_.Exception.Message)" -Level "ERROR"
    Write-AriaLog "Stack Trace: $($_.ScriptStackTrace)" -Level "ERROR"
    
    # Write error result for Aria
    $errorResult = @{
        ExitCode = 1
        ExecutionTime = "ERROR"
        ErrorMessage = $_.Exception.Message
    }
    Write-AriaResult -Result $errorResult
    
    throw
}
#endregion