# Get-Prerequisites.ps1 - Enhanced prerequisites check with integrated logging
# This script checks for PowerShell version and PowerCLI installation status
# Returns JSON result for C# application consumption

param(
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = ""
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging with LogPath parameter
Start-ScriptLogging -ScriptName "Get-Prerequisites" -LogPath $LogPath

# Initialize result object with default values
$result = [PSCustomObject]@{
    PowerShellVersion   = "Unknown"
    IsPowerCliInstalled = $false
}

# Track if an error occurred
$scriptSucceeded = $true

try {
    Write-LogInfo "Starting PowerShell prerequisites check" -Category "Initialization"
    Write-LogInfo "Script parameters: BypassModuleCheck=$BypassModuleCheck, LogPath=$LogPath" -Category "Initialization"

    # 1. Get PowerShell Version - this should always work
    try {
        Write-LogInfo "Checking PowerShell version..." -Category "PowerShell"
        if ($PSVersionTable -and $PSVersionTable.PSVersion) {
            $result.PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            Write-LogSuccess "PowerShell Version: $($result.PowerShellVersion)" -Category "PowerShell"
            
            # Check minimum version requirement (5.1 recommended for PowerCLI)
            $majorVersion = $PSVersionTable.PSVersion.Major
            $minorVersion = $PSVersionTable.PSVersion.Minor
            if ($majorVersion -lt 5 -or ($majorVersion -eq 5 -and $minorVersion -lt 1)) {
                Write-LogWarning "PowerShell version $($result.PowerShellVersion) is below recommended 5.1" -Category "PowerShell"
            }
        }
        else {
            $result.PowerShellVersion = "Unable to determine"
            Write-LogWarning "Could not determine PowerShell version" -Category "PowerShell"
        }
    }
    catch {
        $result.PowerShellVersion = "Error: $($_.Exception.Message)"
        Write-LogError "Error getting PowerShell version: $($_.Exception.Message)" -Category "PowerShell"
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
    
    # OS Information
    try {
        $osInfo = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction SilentlyContinue
        if ($osInfo) {
            Write-LogInfo "Operating System: $($osInfo.Caption) $($osInfo.Version)" -Category "System"
            Write-LogInfo "Architecture: $($osInfo.OSArchitecture)" -Category "System"
        } else {
            Write-LogInfo "OS: $($env:OS)" -Category "System"
        }
    }
    catch {
        Write-LogInfo "OS: $($env:OS)" -Category "System"
    }
    
    Write-LogInfo "Computer Name: $($env:COMPUTERNAME)" -Category "System"
    Write-LogInfo "User: $($env:USERNAME)" -Category "System"
    Write-LogInfo "Domain: $($env:USERDOMAIN)" -Category "System"
    
    # Execution Policy
    try {
        $execPolicy = Get-ExecutionPolicy -Scope CurrentUser -ErrorAction SilentlyContinue
        if ($execPolicy) {
            Write-LogInfo "PowerShell Execution Policy (CurrentUser): $execPolicy" -Category "System"
            
            # Warn if execution policy might block scripts
            if ($execPolicy -eq "Restricted" -or $execPolicy -eq "AllSigned") {
                Write-LogWarning "Execution policy '$execPolicy' may prevent script execution" -Category "System"
            }
        }
        else {
            Write-LogInfo "PowerShell Execution Policy (CurrentUser): Unknown/Default" -Category "System"
        }
    }
    catch {
        Write-LogDebug "Could not determine execution policy: $($_.Exception.Message)" -Category "System"
    }
    
    # Create statistics for logging
    $executionPolicy = try { 
        $policy = Get-ExecutionPolicy -Scope CurrentUser -ErrorAction SilentlyContinue
        if ($policy) { $policy.ToString() } else { "Unknown" }
    } catch { "Unknown" }
    
    $stats = @{
        "PowerShellVersion" = $result.PowerShellVersion
        "PowerCLIInstalled" = $powerCliFound
        "PowerCLIVersion" = if ($powerCliFound) { $powerCliVersion } else { "Not Installed" }
        "ExecutionPolicy" = $executionPolicy
        "OS" = $env:OS
        "Computer" = $env:COMPUTERNAME
        "User" = $env:USERNAME
        "Domain" = $env:USERDOMAIN
    }
    
    # Log success
    Write-LogSuccess "Prerequisites check completed successfully" -Category "Summary"
    Write-LogInfo "  PowerShell Version: $($result.PowerShellVersion)" -Category "Summary"
    Write-LogInfo "  PowerCLI Installed: $powerCliFound" -Category "Summary"
    if ($powerCliFound) {
        Write-LogInfo "  PowerCLI Version: $powerCliVersion" -Category "Summary"
    }
    
    Stop-ScriptLogging -Success $true -Summary "Prerequisites check completed - PowerShell: $($result.PowerShellVersion), PowerCLI: $powerCliFound" -Statistics $stats
}
catch {
    $scriptSucceeded = $false
    $errorMessage = "Fatal error during prerequisites check: $($_.Exception.Message)"
    Write-LogCritical $errorMessage -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Ensure we have some values even on error
    if ($result.PowerShellVersion -eq "Unknown") {
        $result.PowerShellVersion = "Error occurred"
    }
    $result.IsPowerCliInstalled = $false
    
    Stop-ScriptLogging -Success $false -Summary $errorMessage
}
finally {
    # Always output JSON result for C# consumption
    try {
        # Prepare final result object with additional metadata
        $finalResult = [PSCustomObject]@{
            Success = $scriptSucceeded
            PowerShellVersion = $result.PowerShellVersion
            IsPowerCliInstalled = $result.IsPowerCliInstalled
            Timestamp = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
        }
        
        # Add PowerCLI version if found
        if ($powerCliFound -and $powerCliVersion -ne "Unknown") {
            $finalResult | Add-Member -NotePropertyName "PowerCliVersion" -NotePropertyValue $powerCliVersion
        }
        
        # Convert to JSON
        $jsonResult = $finalResult | ConvertTo-Json -Compress
        Write-LogDebug "Outputting JSON result: $jsonResult" -Category "Output"
        
        # This is the ONLY output that C# should capture (stdout)
        Write-Output $jsonResult
    }
    catch {
        # Fallback if ConvertTo-Json fails - create minimal valid JSON
        $fallbackJson = "{`"Success`":false,`"PowerShellVersion`":`"$($result.PowerShellVersion -replace '"','\"')`",`"IsPowerCliInstalled`":$($result.IsPowerCliInstalled.ToString().ToLower())}"
        Write-LogError "ConvertTo-Json failed: $($_.Exception.Message)" -Category "Output"
        Write-LogDebug "Using fallback JSON: $fallbackJson" -Category "Output"
        Write-Output $fallbackJson
    }
}