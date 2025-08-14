# In Scripts/Get-Prerequisites.ps1
param(
    [string]$LogPath = "Logs"
)

# Function to write to both console and log file
function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    # Write to console for UI display
    Write-Information $logMessage -InformationAction Continue
    
    # Ensure log directory exists
    if (-not (Test-Path $LogPath)) {
        New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
    }
    
    $logFile = Join-Path $LogPath "Prerequisites-Check-$(Get-Date -Format 'yyyy-MM-dd').log"
    Add-Content -Path $logFile -Value $logMessage
}

# Initialize a result object with default failure values
$result = [PSCustomObject]@{
    PowerShellVersion   = "Unknown"
    IsPowerCliInstalled = $false
}

try {
    Write-Log "Starting PowerShell prerequisites check"

    # 1. Get PowerShell Version (this is generally reliable)
    $result.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    Write-Log "PowerShell Version: $($result.PowerShellVersion)"

    # 2. Check for PowerCLI Module
    Write-Log "Checking for VMware.PowerCLI module..."
    try {
        # Use Get-Command as a faster, more reliable alternative to Get-Module for checking availability
        $powerCliCommand = Get-Command -Module VMware.PowerCLI -ErrorAction Stop
        if ($powerCliCommand) {
            $result.IsPowerCliInstalled = $true
            Write-Log "VMware.PowerCLI module found."
        }
    }
    catch {
        # This will catch any error if the module is not found
        $result.IsPowerCliInstalled = $false
        Write-Log "VMware.PowerCLI module NOT found. Reason: $($_.Exception.Message)" "WARN"
    }
    
    Write-Log "Prerequisites check completed."

}
catch {
    $errorMessage = "A fatal error occurred during prerequisites check: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    # Update the result object with error information
    $result.PowerShellVersion = "Error: $($_.Exception.Message)"
    $result.IsPowerCliInstalled = $false
}
finally {
    # 3. Always output the result object as JSON
    # This ensures the C# application always gets a valid object to deserialize.
    Write-Log "Returning JSON result: $($result | ConvertTo-Json -Compress)" "DEBUG"
    $result | ConvertTo-Json -Compress
}