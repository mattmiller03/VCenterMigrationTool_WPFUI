# Get-Prerequisites.ps1 - Complete working version
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
        try {
            New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
        }
        catch {
            # Continue if can't create log directory
        }
    }
    
    try {
        $logFile = Join-Path $LogPath "Prerequisites-Check-$(Get-Date -Format 'yyyy-MM-dd').log"
        Add-Content -Path $logFile -Value $logMessage -ErrorAction SilentlyContinue
    }
    catch {
        # Continue if logging fails
    }
}

# Initialize result object with default values
$result = [PSCustomObject]@{
    PowerShellVersion   = "Unknown"
    IsPowerCliInstalled = $false
}

try {
    Write-Log "Starting PowerShell prerequisites check"

    # 1. Get PowerShell Version - this should always work
    try {
        if ($PSVersionTable -and $PSVersionTable.PSVersion) {
            $result.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Write-Log "PowerShell Version: $($result.PowerShellVersion)"
        }
        else {
            $result.PowerShellVersion = "Unable to determine"
            Write-Log "Could not determine PowerShell version" "WARN"
        }
    }
    catch {
        $result.PowerShellVersion = "Error: $($_.Exception.Message)"
        Write-Log "Error getting PowerShell version: $($_.Exception.Message)" "ERROR"
    }

    # 2. Check for PowerCLI Module using multiple methods
    Write-Log "Checking for VMware.PowerCLI module..."
    
    $powerCliFound = $false
    
    # Method 1: Get-Module -ListAvailable
    try {
        Write-Log "Checking with Get-Module -ListAvailable..."
        $availableModules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($availableModules) {
            $powerCliFound = $true
            $version = $availableModules[0].Version.ToString()
            Write-Log "PowerCLI found via Get-Module -ListAvailable. Version: $version"
        }
        else {
            Write-Log "PowerCLI not found via Get-Module -ListAvailable"
        }
    }
    catch {
        Write-Log "Error with Get-Module -ListAvailable: $($_.Exception.Message)" "WARN"
    }
    
    # Method 2: Get-InstalledModule (if PowerShellGet is available)
    if (-not $powerCliFound) {
        try {
            Write-Log "Checking with Get-InstalledModule..."
            if (Get-Command "Get-InstalledModule" -ErrorAction SilentlyContinue) {
                $installedModule = Get-InstalledModule -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
                
                if ($installedModule) {
                    $powerCliFound = $true
                    Write-Log "PowerCLI found via Get-InstalledModule. Version: $($installedModule.Version)"
                }
                else {
                    Write-Log "PowerCLI not found via Get-InstalledModule"
                }
            }
            else {
                Write-Log "Get-InstalledModule command not available"
            }
        }
        catch {
            Write-Log "Error with Get-InstalledModule: $($_.Exception.Message)" "WARN"
        }
    }
    
    # Method 3: Try Import-Module test
    if (-not $powerCliFound) {
        try {
            Write-Log "Testing module import..."
            $importTest = Import-Module -Name "VMware.PowerCLI" -PassThru -ErrorAction SilentlyContinue
            
            if ($importTest) {
                $powerCliFound = $true
                Write-Log "PowerCLI successfully imported for testing"
                
                # Remove the module since we just wanted to test
                Remove-Module -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
            }
            else {
                Write-Log "Could not import VMware.PowerCLI module"
            }
        }
        catch {
            Write-Log "Error testing module import: $($_.Exception.Message)" "WARN"
        }
    }
    
    # Set final result
    $result.IsPowerCliInstalled = $powerCliFound
    
    if ($powerCliFound) {
        Write-Log "VMware.PowerCLI module is installed and available"
    }
    else {
        Write-Log "VMware.PowerCLI module NOT found" "WARN"
        Write-Log "To install: Install-Module -Name VMware.PowerCLI -Force" "INFO"
    }
    
    Write-Log "Prerequisites check completed"
}
catch {
    $errorMessage = "Fatal error during prerequisites check: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    
    # Ensure we have some values even on error
    if ($result.PowerShellVersion -eq "Unknown") {
        $result.PowerShellVersion = "Error occurred"
    }
    $result.IsPowerCliInstalled = $false
}
finally {
    # Always output JSON result
    try {
        # Output the JSON result for the C# application
        $jsonResult = $result | ConvertTo-Json -Compress
        Write-Log "Returning result: $jsonResult" "DEBUG"
        
        # This is the ONLY output that C# should capture
        Write-Output $jsonResult
    }
    catch {
        # Fallback if ConvertTo-Json fails
        $manualJson = "{`"PowerShellVersion`":`"$($result.PowerShellVersion)`",`"IsPowerCliInstalled`":$($result.IsPowerCliInstalled.ToString().ToLower())}"
        Write-Log "ConvertTo-Json failed, using manual JSON: $manualJson" "WARN"
        Write-Output $manualJson
    }
}