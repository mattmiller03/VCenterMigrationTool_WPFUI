# Write-ScriptLog.ps1 - PowerShell Script Logging Functions
# Fixed version without Export-ModuleMember for dot-sourcing

# Global variables for session tracking
$Global:ScriptLogFile = $null
$Global:ScriptSessionId = $null
$Global:ScriptStartTime = $null

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
    
    # Use global log file or create a default one
    if (-not $Global:ScriptLogFile) {
        $logDir = Join-Path $PSScriptRoot "..\Logs\PowerShell"
        if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
        $Global:ScriptLogFile = Join-Path $logDir "powershell_$(Get-Date -Format 'yyyy-MM-dd').log"
    }
    
    # Create timestamp
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    
    # Build log entry
    $sessionPart = if ($Global:ScriptSessionId) { "[$Global:ScriptSessionId]" } else { "[UNKNOWN]" }
    $categoryPart = if ($Category) { "[$Category]" } else { "" }
    $scriptName = if ($MyInvocation.ScriptName) { Split-Path $MyInvocation.ScriptName -Leaf } else { "Console" }
    
    $logEntry = "$timestamp [$Level] $sessionPart [$scriptName] $categoryPart $Message"
    
    # Write to console (unless suppressed)
    if (-not $NoConsole) {
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
            Write-Host "Failed to write to log file: $_" -ForegroundColor Yellow
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

# Initialize script logging
function Start-ScriptLogging {
    param(
        [string]$ScriptName = '',
        [string]$LogFile = $null
    )
    
    # Generate session ID
    $Global:ScriptSessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
    $Global:ScriptStartTime = Get-Date
    
    # Set custom log file if provided
    if ($LogFile) {
        $Global:ScriptLogFile = $LogFile
    }
    
    # Get script name
    if (-not $ScriptName) {
        $ScriptName = if ($MyInvocation.ScriptName) { 
            Split-Path $MyInvocation.ScriptName -Leaf 
        } else { 
            "PowerShell-Session" 
        }
    }
    
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
            Write-ScriptLog -Message "  $key = $($Statistics[$key])"
        }
    }
    
    Write-ScriptLog -Message "Duration: $($duration.ToString('hh\:mm\:ss\.fff'))"
    Write-ScriptLog -Message "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ScriptLog -Message "Session ID: $Global:ScriptSessionId"
    Write-ScriptLog -Message $separator -NoConsole
}