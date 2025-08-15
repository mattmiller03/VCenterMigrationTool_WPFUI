# Get-Prerequisites.ps1 - Enhanced version with better error handling
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
    
    try {
        $logFile = Join-Path $LogPath "Prerequisites-Check-$(Get-Date -Format 'yyyy-MM-dd').log"
        Add-Content -Path $logFile -Value $logMessage -ErrorAction SilentlyContinue
    }
    catch {
        # If logging fails, continue without breaking the script
        Write-Information "Warning: Could not write to log file" -InformationAction Continue
    }
}

# Initialize a result object with default values
$result = [PSCustomObject]@{
    PowerShellVersion   = "Unknown"
    IsPowerCliInstalled = $false
}

try {
    Write-Log "Starting PowerShell prerequisites check"

    # 1. Get PowerShell Version (this should always work)
    try {
        if ($PSVersionTable -and $PSVersionTable.PSVersion) {
            $result.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Write-Log "PowerShell Version: $($result.PowerShellVersion)"
        }
        else {
            $result.PowerShellVersion = "Unable to determine"
            Write-Log "Warning: Could not determine PowerShell version" "WARN"
        }
    }
    catch {
        $result.PowerShellVersion = "Error: $($_.Exception.Message)"
        Write-Log "Error getting PowerShell version: $($_.Exception.Message)" "ERROR"
    }

    # 2. Check for PowerCLI Module - Multiple detection methods
    Write-Log "Checking for VMware.PowerCLI module..."
    
    $powerCliFound = $false
    $powerCliVersion = "Not Found"
    
    try {
        # Method 1: Try Get-Module -ListAvailable (most reliable)
        Write-Log "Method 1: Checking with Get-Module -ListAvailable..."
        $listAvailableModules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($listAvailableModules) {
            $powerCliFound = $true
            $powerCliVersion = $listAvailableModules[0].Version.ToString()
            Write-Log "PowerCLI found via Get-Module -ListAvailable. Version: $powerCliVersion"
        }
        else {
            Write-Log "PowerCLI not found via Get-Module -ListAvailable"
        }
    }
    catch {
        Write-Log "Error with Get-Module -ListAvailable: $($_.Exception.Message)" "WARN"
    }
    
    # Method 2: Try Get-InstalledModule (if available)
    if (-not $powerCliFound) {
        try {
            Write-Log "Method 2: Checking with Get-InstalledModule..."
            $installedModule = Get-InstalledModule -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
            
            if ($installedModule) {
                $powerCliFound = $true
                $powerCliVersion = $installedModule.Version.ToString()
                Write-Log "PowerCLI found via Get-InstalledModule. Version: $powerCliVersion"
            }
            else {
                Write-Log "PowerCLI not found via Get-InstalledModule"
            }
        }
        catch {
            Write-Log "Get-InstalledModule not available or error: $($_.Exception.Message)" "WARN"
        }
    }
    
    # Method 3: Try to import and check (last resort)
    if (-not $powerCliFound) {
        try {
            Write-Log "Method 3: Attempting to import VMware.PowerCLI..."
            $importResult = Import-Module -Name "VMware.PowerCLI" -PassThru -ErrorAction SilentlyContinue
            
            if ($importResult) {
                $powerCliFound = $true
                $powerCliVersion = $importResult.Version.ToString()
                Write-Log "PowerCLI successfully imported. Version: $powerCliVersion"
                
                # Clean up - remove the module since we just wanted to test
                Remove-Module -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
            }
            else {
                Write-Log "Could not import VMware.PowerCLI module"
            }
        }
        catch {
            Write-Log "Error importing VMware.PowerCLI: $($_.Exception.Message)" "WARN"
        }
    }
    
    # Set the final result
    $result.IsPowerCliInstalled = $powerCliFound
    
    if ($powerCliFound) {
        Write-Log "VMware.PowerCLI module found. Version: $powerCliVersion"
    }
    else {
        Write-Log "VMware.PowerCLI module NOT found." "WARN"
        Write-Log "To install PowerCLI, run: Install-Module -Name VMware.PowerCLI -Force" "INFO"
    }
    
    Write-Log "Prerequisites check completed successfully."
}
catch {
    $errorMessage = "A fatal error occurred during prerequisites check: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    
    # Update the result object with error information, but don't fail completely
    if ($result.PowerShellVersion -eq "Unknown") {
        $result.PowerShellVersion = "Error: $($_.Exception.Message)"
    }
    $result.IsPowerCliInstalled = $false
}
finally {
    # Always output the result object as JSON
    try {
        Write-Log "Returning JSON result: $($result | ConvertTo-Json -Compress)" "DEBUG"
        
        # Use the built-in ConvertTo-Json if available, otherwise manually create JSON
        if (Get-Command ConvertTo-Json -ErrorAction SilentlyContinue) {
            $result | ConvertTo-Json -Compress
        }
        else {
            # Manual JSON creation as fallback
            $manualJson = "{`"PowerShellVersion`":`"$($result.PowerShellVersion)`",`"IsPowerCliInstalled`":$($result.IsPowerCliInstalled.ToString().ToLower())}"
            Write-Information $manualJson -InformationAction Continue
        }
    }
    catch {
        Write-Log "Error converting to JSON: $($_.Exception.Message)" "ERROR"
        # Output a basic JSON structure manually
        Write-Information "{`"PowerShellVersion`":`"Error-JSON-Conversion`",`"IsPowerCliInstalled`":false}" -InformationAction Continue
    }
}