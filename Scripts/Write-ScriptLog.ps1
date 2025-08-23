# Write-ScriptLog.ps1 - PowerShell Script Logging Functions
# Creates individual log files per script in the PowerShell subfolder
# Does NOT write to the main application log

# Global variables for session tracking
$Global:ScriptLogFile = $null
$Global:ScriptSessionId = $null
$Global:ScriptStartTime = $null
$Global:ConfiguredLogPath = $null
$Global:SuppressConsoleOutput = $false

# Function to initialize log path from parameters or environment
function Initialize-LogPath {
    param(
        [string]$LogPath = $null,
        [string]$ScriptName = $null,
        [bool]$SuppressConsoleOutput = $false
    )
    
    # Determine the base log directory
    # The app log goes here, PowerShell logs go in a PowerShell subfolder
    if ($LogPath -and $LogPath -ne "") {
        # Always use the LogPath as the base directory
        # If it points to a file, get the directory; otherwise use as-is
        if ([System.IO.Path]::HasExtension($LogPath)) {
            # It's a file path (probably the app log), extract the directory
            $Global:ConfiguredLogPath = [System.IO.Path]::GetDirectoryName($LogPath)
        } else {
            # It's a directory path, use it directly
            $Global:ConfiguredLogPath = $LogPath
        }
        
        # Ensure we have a valid path
        if ([string]::IsNullOrEmpty($Global:ConfiguredLogPath)) {
            $Global:ConfiguredLogPath = [System.IO.Path]::GetDirectoryName($LogPath)
        }
        if (-not $SuppressConsoleOutput) {
            Write-Host "PowerShell logs will be saved in: $Global:ConfiguredLogPath\PowerShell" -ForegroundColor Cyan
        }
    } else {
        # Fallback to application's default location
        $Global:ConfiguredLogPath = Join-Path $env:LOCALAPPDATA "VCenterMigrationTool\Logs"
        if (-not $SuppressConsoleOutput) {
            Write-Host "Using default log path: $Global:ConfiguredLogPath\PowerShell" -ForegroundColor Yellow
        }
    }
    
    # Ensure the directory exists and is writable
    try {
        if (-not (Test-Path $Global:ConfiguredLogPath)) {
            New-Item -ItemType Directory -Path $Global:ConfiguredLogPath -Force | Out-Null
        }
        
        # Test write permission by creating a temp file
        $testFile = Join-Path $Global:ConfiguredLogPath "test_$(Get-Random).tmp"
        "test" | Out-File -FilePath $testFile -ErrorAction Stop
        Remove-Item $testFile -ErrorAction SilentlyContinue
        
        if (-not $SuppressConsoleOutput) {
            Write-Host "Log directory verified as writable: $Global:ConfiguredLogPath" -ForegroundColor Green
        }
        
    } catch {
        # If we can't write to the configured path, use a fallback
        if (-not $SuppressConsoleOutput) {
            Write-Host "Cannot write to configured log path: $Global:ConfiguredLogPath. Error: $($_.Exception.Message)" -ForegroundColor Red
        }
        $Global:ConfiguredLogPath = Join-Path $env:LOCALAPPDATA "VCenterMigrationTool\Logs"
        if (-not (Test-Path $Global:ConfiguredLogPath)) {
            New-Item -ItemType Directory -Path $Global:ConfiguredLogPath -Force | Out-Null
        }
        if (-not $SuppressConsoleOutput) {
            Write-Host "Using fallback log path: $Global:ConfiguredLogPath" -ForegroundColor Yellow
        }
    }
    
    # ALWAYS create logs in the PowerShell subdirectory
    # This keeps PowerShell logs separate from the main application log
    $psLogDir = Join-Path $Global:ConfiguredLogPath "PowerShell"
    if (-not (Test-Path $psLogDir)) {
        New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
    }
    
    # Create individual log file based on script name and timestamp
    if ($ScriptName) {
        # Remove file extension and clean up the name
        $cleanScriptName = [System.IO.Path]::GetFileNameWithoutExtension($ScriptName)
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $sessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir "${cleanScriptName}_${timestamp}_${sessionId}.log"
    } else {
        # Fallback for unknown scripts
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $sessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir "Unknown_${timestamp}_${sessionId}.log"
    }
    
    if (-not $SuppressConsoleOutput) {
        Write-Host "Individual script log file: $Global:ScriptLogFile" -ForegroundColor Green
    }
}

# Main logging function
function Write-ScriptLog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,
        
        [ValidateSet('Info', 'Warning', 'Error', 'Success', 'Debug', 'Verbose', 'Critical')]
        [string]$Level = 'Info',
        
        [string]$Category = '',
        
        [switch]$NoConsole,
        
        [switch]$NoFile
    )
    
    # Initialize log path if not already done
    if (-not $Global:ScriptLogFile) {
        Initialize-LogPath -ScriptName "Unknown"
    }
    
    # Create timestamp
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    
    # Build log entry
    $sessionPart = if ($Global:ScriptSessionId) { "[$Global:ScriptSessionId]" } else { "[UNKNOWN]" }
    $categoryPart = if ($Category) { "[$Category]" } else { "" }
    $scriptName = if ($MyInvocation.ScriptName) { Split-Path $MyInvocation.ScriptName -Leaf } else { "Write-ScriptLog.ps1" }
    
    $logEntry = "$timestamp [$Level] $sessionPart [$scriptName] $categoryPart $Message"
    
    # Write to console (unless suppressed globally or by parameter)
    if (-not $NoConsole -and -not $Global:SuppressConsoleOutput) {
        switch ($Level) {
            'Info' { Write-Host $logEntry -ForegroundColor White }
            'Error' { 
                Write-Host $logEntry -ForegroundColor Red
                # Add exception details if available
                if ($Error[0]) {
                    Write-Host "  Exception: $($Error[0].Exception.Message)" -ForegroundColor DarkRed
                    Write-Host "  Stack: $($Error[0].ScriptStackTrace)" -ForegroundColor DarkRed
                }
            }
            'Critical' {
                Write-Host $logEntry -ForegroundColor Magenta
                if ($Error[0]) {
                    Write-Host "  Exception: $($Error[0].Exception.Message)" -ForegroundColor DarkRed
                    Write-Host "  Stack: $($Error[0].ScriptStackTrace)" -ForegroundColor DarkRed
                }
            }
            'Warning' { Write-Host $logEntry -ForegroundColor Yellow }
            'Success' { Write-Host $logEntry -ForegroundColor Green }
            'Debug' { Write-Host $logEntry -ForegroundColor Gray }
            'Verbose' { Write-Host $logEntry -ForegroundColor Cyan }
            default { Write-Host $logEntry }
        }
    }
    
    # Write to file (unless suppressed)
    if (-not $NoFile) {
        try {
            $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            
            # Also write exception details for errors
            if ($Level -eq 'Error' -and $Error[0]) {
                "  Exception: $($Error[0].Exception.Message)" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
                "  Stack: $($Error[0].ScriptStackTrace)" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            }
        } catch {
            # Show file write errors for debugging
            Write-Host "Failed to write to log file: $Global:ScriptLogFile - $_" -ForegroundColor Red
        }
    }
    
    # Return the formatted entry for potential further processing
    return $logEntry
}

# Helper functions for common log levels
function Write-LogInfo { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Info -Category $Category
}

function Write-LogWarning { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Warning -Category $Category
}

function Write-LogError { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Error -Category $Category
}

function Write-LogCritical { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Critical -Category $Category
}

function Write-LogSuccess { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Success -Category $Category
}

function Write-LogDebug { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Debug -Category $Category
}

function Write-LogVerbose { 
    param([string]$Message, [string]$Category = '')
    Write-ScriptLog -Message $Message -Level Verbose -Category $Category
}

# Initialize script logging - creates individual log file per script execution
function Start-ScriptLogging {
    param(
        [string]$ScriptName = '',
        [string]$LogPath = $null,
        [bool]$SuppressConsoleOutput = $false
    )
    
    # Get script name from calling script, not from this logging script
    if (-not $ScriptName) {
        # Try to get the script name from the call stack
        $callStack = Get-PSCallStack
        if ($callStack.Count -gt 1) {
            $callingScript = $callStack[1].ScriptName
            if ($callingScript) {
                $ScriptName = Split-Path $callingScript -Leaf
            }
        }
        
        # Final fallback
        if (-not $ScriptName) {
            $ScriptName = "PowerShell-Session"
        }
    }
    
    # Store suppression state globally
    $Global:SuppressConsoleOutput = $SuppressConsoleOutput
    
    # Initialize log path with script name for individual file creation
    Initialize-LogPath -LogPath $LogPath -ScriptName $ScriptName -SuppressConsoleOutput $SuppressConsoleOutput
    
    # Generate session ID
    $Global:ScriptSessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
    $Global:ScriptStartTime = Get-Date
    
    # Write header to the individual log file
    $separator = "=" * 80
    Write-ScriptLog -Message $separator -NoConsole
    Write-ScriptLog -Message "SCRIPT START: $ScriptName"
    Write-ScriptLog -Message "Session ID: $Global:ScriptSessionId"
    Write-ScriptLog -Message "User: $env:USERNAME@$env:COMPUTERNAME"
    Write-ScriptLog -Message "PowerShell Version: $($PSVersionTable.PSVersion)"
    Write-ScriptLog -Message "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ScriptLog -Message $separator -NoConsole
}

# Finalize script logging
function Stop-ScriptLogging {
    param(
        [bool]$Success = $true,
        [string]$Summary = "",
        [hashtable]$Statistics = @{}
    )
    
    $duration = if ($Global:ScriptStartTime) { 
        (Get-Date) - $Global:ScriptStartTime 
    } else { 
        [TimeSpan]::Zero 
    }
    
    $separator = "=" * 80
    Write-ScriptLog -Message $separator -NoConsole
    if ($Success) {
        Write-ScriptLog -Message "SCRIPT COMPLETED SUCCESSFULLY" -Level Success
    } else {
        Write-ScriptLog -Message "SCRIPT FAILED" -Level Error
    }
    
    if ($Summary) {
        Write-ScriptLog -Message "Summary: $Summary"
    }
    
    if ($Statistics.Count -gt 0) {
        Write-ScriptLog -Message "Statistics:"
        foreach ($key in $Statistics.Keys) {
            Write-ScriptLog -Message "    $key = $($Statistics[$key])"
        }
    }
    
    Write-ScriptLog -Message "Duration: $($duration.ToString('hh\:mm\:ss\.fff'))"
    Write-ScriptLog -Message "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ScriptLog -Message "Session ID: $Global:ScriptSessionId"
    Write-ScriptLog -Message $separator -NoConsole
}