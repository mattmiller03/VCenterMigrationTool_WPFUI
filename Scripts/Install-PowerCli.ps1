# Install-PowerCli.ps1 - Enhanced version with better error handling
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
    
    try {
        # Write to log file
        $logFile = Join-Path $LogPath "PowerCLI-Install-$(Get-Date -Format 'yyyy-MM-dd').log"
        Add-Content -Path $logFile -Value $logMessage -ErrorAction SilentlyContinue
    }
    catch {
        # Continue if logging fails
        Write-Output "Warning: Could not write to log file"
    }
}

try {
    Write-Log "Starting PowerCLI installation process" "INFO"
    Write-Log "Log path: $LogPath" "INFO"
    Write-Log "PowerShell version: $($PSVersionTable.PSVersion.ToString())" "INFO"
    Write-Log "Execution policy: $(Get-ExecutionPolicy)" "INFO"
    
    # Check if running as administrator
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Log "Running as administrator: $isAdmin" "INFO"
    
    # Check if PowerCLI is already installed
    Write-Log "Checking if PowerCLI is already installed..." "INFO"
    $existingModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    
    if ($existingModule) {
        Write-Log "PowerCLI is already installed. Version: $($existingModule.Version)" "INFO"
        Write-Log "Checking for updates..." "INFO"
        
        try {
            # Try to update if already installed
            Update-Module -Name "VMware.PowerCLI" -Force -ErrorAction Stop
            Write-Log "PowerCLI updated successfully" "INFO"
            Write-Output "Success: PowerCLI updated successfully"
            return
        }
        catch {
            Write-Log "Update failed, continuing with current installation: $($_.Exception.Message)" "WARN"
            Write-Output "Warning: Update failed but PowerCLI is already installed"
            return
        }
    }
    
    Write-Log "PowerCLI not found. Starting installation..." "INFO"
    
    # Check PowerShell Gallery repository
    Write-Log "Checking PowerShell Gallery repository..." "INFO"
    try {
        $psGallery = Get-PSRepository -Name "PSGallery" -ErrorAction Stop
        Write-Log "PowerShell Gallery status: $($psGallery.InstallationPolicy)" "INFO"
        
        if ($psGallery.InstallationPolicy -ne "Trusted") {
            Write-Log "Setting PowerShell Gallery to trusted for this session..." "INFO"
            Set-PSRepository -Name "PSGallery" -InstallationPolicy "Trusted" -ErrorAction Stop
        }
    }
    catch {
        Write-Log "Error configuring PowerShell Gallery: $($_.Exception.Message)" "ERROR"
        Write-Output "Error: Could not configure PowerShell Gallery: $($_.Exception.Message)"
        return
    }
    
    Write-Log "Starting VMware.PowerCLI module installation..." "INFO"
    Write-Log "This may take several minutes depending on your internet connection..." "INFO"

    # Determine scope based on admin privileges
    $installScope = if ($isAdmin) { "AllUsers" } else { "CurrentUser" }
    Write-Log "Installing for scope: $installScope" "INFO"

    try {
        # Install the module with progress tracking
        Write-Log "Installing VMware.PowerCLI module..." "INFO"
        
        Install-Module -Name "VMware.PowerCLI" -Scope $installScope -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
        
        Write-Log "Installation command completed successfully" "INFO"
    }
    catch {
        Write-Log "Installation failed: $($_.Exception.Message)" "ERROR"
        Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
        
        # Try alternative installation method
        Write-Log "Trying alternative installation method..." "INFO"
        try {
            Install-Module -Name "VMware.PowerCLI" -Scope "CurrentUser" -Force -AllowClobber -AcceptLicense -ErrorAction Stop
            Write-Log "Alternative installation method succeeded" "INFO"
        }
        catch {
            Write-Log "Alternative installation also failed: $($_.Exception.Message)" "ERROR"
            Write-Output "Failure: Installation failed. Error: $($_.Exception.Message)"
            Write-Output "Please try running PowerShell as administrator or check your internet connection."
            return
        }
    }
    
    Write-Log "Verifying installation..." "INFO"
    
    # Verify installation
    $installedModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    
    if ($installedModule) {
        Write-Log "Success: VMware.PowerCLI module installed successfully" "INFO"
        Write-Log "Module version: $($installedModule.Version)" "INFO"
        Write-Log "Module location: $($installedModule.ModuleBase)" "INFO"
        
        # Try to import the module to make sure it works
        try {
            Write-Log "Testing module import..." "INFO"
            Import-Module -Name "VMware.PowerCLI" -Force -ErrorAction Stop
            Write-Log "Module imported successfully" "INFO"
            
            # Clean up by removing the module (we just wanted to test)
            Remove-Module -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
            
            Write-Output "Success: VMware.PowerCLI module installed and verified successfully"
        }
        catch {
            Write-Log "Module installed but import test failed: $($_.Exception.Message)" "WARN"
            Write-Output "Warning: Module installed but import test failed. You may need to restart PowerShell."
        }
    }
    else {
        Write-Log "Installation verification failed - module not found after installation" "ERROR"
        Write-Output "Failure: Module installation verification failed"
    }
}
catch {
    $errorMessage = "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    Write-Output $errorMessage
    Write-Output "Please check the log files for more details and try running as administrator."
}
finally {
    # Reset repository policy if we changed it
    try {
        Set-PSRepository -Name "PSGallery" -InstallationPolicy "Untrusted" -ErrorAction SilentlyContinue
        Write-Log "Reset PowerShell Gallery to untrusted" "INFO"
    }
    catch {
        # Ignore errors when resetting
    }
    
    Write-Log "PowerCLI installation script completed" "INFO"
}