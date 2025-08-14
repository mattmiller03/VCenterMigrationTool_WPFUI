# In Scripts/Install-PowerCli.ps1
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
    Write-Output $logMessage
    
    # Ensure log directory exists
    if (-not (Test-Path $LogPath)) {
        New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
    }
    
    # Write to log file
    $logFile = Join-Path $LogPath "PowerCLI-Install-$(Get-Date -Format 'yyyy-MM-dd').log"
    Add-Content -Path $logFile -Value $logMessage
}

try {
    Write-Log "Starting PowerCLI installation process" "INFO"
    Write-Log "Log path: $LogPath" "INFO"
    
    Write-Log "Setting up PowerShell Gallery repository..." "INFO"
    
    # Set the repository to trust it for this session to avoid prompts
    #Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
    
    Write-Log "Starting VMware.PowerCLI module installation..." "INFO"
    Write-Log "This may take several minutes depending on your internet connection..." "INFO"

    # Install the module for the current user, which doesn't require admin rights
    # Use -Verbose to get more detailed output
    Install-Module -Name VMware.PowerCLI -Scope CurrentUser -Force -AllowClobber -Verbose
    
    Write-Log "Installation completed. Verifying module availability..." "INFO"
    
    # Check if it was successful
    $module = Get-Module -ListAvailable -Name VMware.PowerCLI
    if ($module) {
        Write-Log "Success: VMware.PowerCLI module installed successfully." "INFO"
        Write-Log "Module version: $($module.Version)" "INFO"
        Write-Log "Module location: $($module.ModuleBase)" "INFO"
        Write-Output "Success: VMware.PowerCLI module installed successfully."
    }
    else {
        Write-Log "Failure: Module installation failed. Please check your internet connection and try running as an administrator if issues persist." "ERROR"
        Write-Output "Failure: Module installation failed. Please check your internet connection and try running as an administrator if issues persist."
    }
}
catch {
    $errorMessage = "Failure: An error occurred during installation. Details: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    Write-Output $errorMessage
}