# Write-ScriptLog.ps1
# Common logging function for all PowerShell scripts

function Write-ScriptLog {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        
        [Parameter(Mandatory=$false)]
        [ValidateSet('Info', 'Warning', 'Error', 'Debug', 'Success', 'Verbose')]
        [string]$Level = 'Info',
        
        [Parameter(Mandatory=$false)]
        [string]$LogFile = $null,
        
        [Parameter(Mandatory=$false)]
        [switch]$NoConsole,
        
        [Parameter(Mandatory=$false)]
        [switch]$NoFile
    )
    
    # Format timestamp
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'
    
    # Determine log file path
    if (-not $LogFile) {
        # Use default log directory
        $logDir = Join-Path $PSScriptRoot "..\Logs\PowerShell"
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
        
        $dateStamp = Get-Date -Format 'yyyy-MM-dd'
        $scriptName = if ($MyInvocation.ScriptName) { 
            [System.IO.Path]::GetFileNameWithoutExtension($MyInvocation.ScriptName) 
        } else { 
            "PowerShell" 
        }
        $LogFile = Join-Path $logDir "${scriptName}_${dateStamp}.log"
    }
    
    # Format log entry
    $logEntry = "[$timestamp] [$($Level.ToUpper().PadRight(7))] $Message"
    
    # Write to console with color coding (unless suppressed)
    if (-not $NoConsole) {
        switch ($Level) {
            'Error' { 
                Write-Host $logEntry -ForegroundColor Red 
                if ($Error[0]) {
                    Write-Host "  Exception: $($Error[0].Exception.Message)" -ForegroundColor Red
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
            $logEntry | Out-File -FilePath $LogFile -Append -Encoding UTF8
            
            # Also write exception details for errors
            if ($Level -eq 'Error' -and $Error[0]) {
                "  Exception: $($Error[0].Exception.Message)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
                "  Stack: $($Error[0].ScriptStackTrace)" | Out-File -FilePath $LogFile -Append -Encoding UTF8
            }
        } catch {
            Write-Host "Failed to write to log file: $_" -ForegroundColor Yellow
        }
    }
    
    # Return the formatted entry for potential further processing
    return $logEntry
}

# Helper functions for common log levels
function Write-LogInfo { Write-ScriptLog -Message $args[0] -Level Info }
function Write-LogWarning { Write-ScriptLog -Message $args[0] -Level Warning }
function Write-LogError { Write-ScriptLog -Message $args[0] -Level Error }
function Write-LogSuccess { Write-ScriptLog -Message $args[0] -Level Success }
function Write-LogDebug { Write-ScriptLog -Message $args[0] -Level Debug }
function Write-LogVerbose { Write-ScriptLog -Message $args[0] -Level Verbose }

# Initialize script logging
function Start-ScriptLogging {
    param(
        [string]$ScriptName = $MyInvocation.ScriptName,
        [string]$LogFile = $null
    )
    
    Write-ScriptLog -Message "=" * 80 -NoConsole
    Write-ScriptLog -Message "SCRIPT START: $ScriptName"
    Write-ScriptLog -Message "User: $env:USERNAME@$env:COMPUTERNAME"
    Write-ScriptLog -Message "PowerShell Version: $($PSVersionTable.PSVersion)"
    Write-ScriptLog -Message "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ScriptLog -Message "=" * 80 -NoConsole
}

# Finalize script logging
function Stop-ScriptLogging {
    param(
        [bool]$Success = $true,
        [string]$Summary = ""
    )
    
    Write-ScriptLog -Message "=" * 80 -NoConsole
    if ($Success) {
        Write-ScriptLog -Message "SCRIPT COMPLETED SUCCESSFULLY" -Level Success
    } else {
        Write-ScriptLog -Message "SCRIPT FAILED" -Level Error
    }
    
    if ($Summary) {
        Write-ScriptLog -Message "Summary: $Summary"
    }
    
    Write-ScriptLog -Message "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-ScriptLog -Message "=" * 80 -NoConsole
}

# Export functions for use in other scripts
Export-ModuleMember -Function Write-ScriptLog, Write-LogInfo, Write-LogWarning, Write-LogError, Write-LogSuccess, Write-LogDebug, Write-LogVerbose, Start-ScriptLogging, Stop-ScriptLogging