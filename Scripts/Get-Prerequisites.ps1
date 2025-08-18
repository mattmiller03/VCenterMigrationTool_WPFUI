# Get-Prerequisites.ps1 - Complete working version with integrated logging
param(
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Get-Prerequisites"

# Initialize result object with default values
$result = [PSCustomObject]@{
    PowerShellVersion   = "Unknown"
    IsPowerCliInstalled = $false
}

try {
    Write-LogInfo "Starting PowerShell prerequisites check"

    # 1. Get PowerShell Version - this should always work
    try {
        if ($PSVersionTable -and $PSVersionTable.PSVersion) {
            $result.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Write-LogInfo "PowerShell Version: $($result.PowerShellVersion)" -Category "Version"
        }
        else {
            $result.PowerShellVersion = "Unable to determine"
            Write-LogWarning "Could not determine PowerShell version"
        }
    }
    catch {
        $result.PowerShellVersion = "Error: $($_.Exception.Message)"
        Write-LogError "Error getting PowerShell version: $($_.Exception.Message)"
    }

    # 2. Check for PowerCLI Module using multiple methods
    Write-LogInfo "Checking for VMware.PowerCLI module..." -Category "PowerCLI"
    
    $powerCliFound = $false
    $powerCliVersion = "Unknown"
    
    # Method 1: Get-Module -ListAvailable
    try {
        Write-LogDebug "Checking with Get-Module -ListAvailable..."
        $availableModules = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($availableModules) {
            $powerCliFound = $true
            $powerCliVersion = $availableModules[0].Version.ToString()
            Write-LogSuccess "PowerCLI found via Get-Module -ListAvailable. Version: $powerCliVersion"
        }
        else {
            Write-LogDebug "PowerCLI not found via Get-Module -ListAvailable"
        }
    }
    catch {
        Write-LogWarning "Error with Get-Module -ListAvailable: $($_.Exception.Message)"
    }
    
    # Method 2: Get-InstalledModule (if PowerShellGet is available)
    if (-not $powerCliFound) {
        try {
            Write-LogDebug "Checking with Get-InstalledModule..."
            if (Get-Command "Get-InstalledModule" -ErrorAction SilentlyContinue) {
                $installedModule = Get-InstalledModule -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
                
                if ($installedModule) {
                    $powerCliFound = $true
                    $powerCliVersion = $installedModule.Version.ToString()
                    Write-LogSuccess "PowerCLI found via Get-InstalledModule. Version: $powerCliVersion"
                }
                else {
                    Write-LogDebug "PowerCLI not found via Get-InstalledModule"
                }
            }
            else {
                Write-LogDebug "Get-InstalledModule command not available"
            }
        }
        catch {
            Write-LogWarning "Error with Get-InstalledModule: $($_.Exception.Message)"
        }
    }
    
    # Method 3: Try Import-Module test
    if (-not $powerCliFound) {
        try {
            Write-LogDebug "Testing module import..."
            $importTest = Import-Module -Name "VMware.PowerCLI" -PassThru -ErrorAction SilentlyContinue
            
            if ($importTest) {
                $powerCliFound = $true
                $powerCliVersion = $importTest.Version.ToString()
                Write-LogSuccess "PowerCLI successfully imported for testing. Version: $powerCliVersion"
                
                # Remove the module since we just wanted to test
                Remove-Module -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
                Write-LogDebug "Test module removed"
            }
            else {
                Write-LogDebug "Could not import VMware.PowerCLI module"
            }
        }
        catch {
            Write-LogWarning "Error testing module import: $($_.Exception.Message)"
        }
    }
    
    # Set final result
    $result.IsPowerCliInstalled = $powerCliFound
    
    if ($powerCliFound) {
        Write-LogSuccess "VMware.PowerCLI module is installed and available" -Category "PowerCLI"
        Write-LogInfo "PowerCLI Version: $powerCliVersion"
    }
    else {
        Write-LogWarning "VMware.PowerCLI module NOT found" -Category "PowerCLI"
        Write-LogInfo "To install: Install-Module -Name VMware.PowerCLI -Force"
    }
    
    # Additional system information
    Write-LogInfo "Collecting additional system information..." -Category "System"
    Write-LogInfo "  OS: $($env:OS)"
    Write-LogInfo "  Computer: $($env:COMPUTERNAME)"
    Write-LogInfo "  User: $($env:USERNAME)"
    Write-LogInfo "  PowerShell Execution Policy: $(Get-ExecutionPolicy -Scope CurrentUser)"
    
    # Create statistics for logging
    $stats = @{
        "PowerShellVersion" = $result.PowerShellVersion
        "PowerCLIInstalled" = $powerCliFound
        "PowerCLIVersion" = $powerCliVersion
        "ExecutionPolicy" = (Get-ExecutionPolicy -Scope CurrentUser).ToString()
        "OS" = $env:OS
        "Computer" = $env:COMPUTERNAME
    }
    
    Stop-ScriptLogging -Success $true -Summary "Prerequisites check completed - PowerShell: $($result.PowerShellVersion), PowerCLI: $powerCliFound" -Statistics $stats
    
    Write-LogInfo "Prerequisites check completed successfully"
}
catch {
    $errorMessage = "Fatal error during prerequisites check: $($_.Exception.Message)"
    Write-LogCritical $errorMessage
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    # Ensure we have some values even on error
    if ($result.PowerShellVersion -eq "Unknown") {
        $result.PowerShellVersion = "Error occurred"
    }
    $result.IsPowerCliInstalled = $false
    
    Stop-ScriptLogging -Success $false -Summary $errorMessage
}
finally {
    # Always output JSON result
    try {
        # Output the JSON result for the C# application
        $jsonResult = $result | ConvertTo-Json -Compress
        Write-LogDebug "Returning result: $jsonResult"
        
        # This is the ONLY output that C# should capture
        Write-Output $jsonResult
    }
    catch {
        # Fallback if ConvertTo-Json fails
        $manualJson = "{`"PowerShellVersion`":`"$($result.PowerShellVersion)`",`"IsPowerCliInstalled`":$($result.IsPowerCliInstalled.ToString().ToLower())}"
        Write-LogWarning "ConvertTo-Json failed, using manual JSON: $manualJson"
        Write-Output $manualJson
    }
}