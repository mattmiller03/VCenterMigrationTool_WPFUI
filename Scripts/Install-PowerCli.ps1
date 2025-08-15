# Install-PowerCli.ps1 - Using external PowerShell process for installation
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
        try {
            New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
        }
        catch {
            # Continue if can't create log directory
        }
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
    
    # Check if running as administrator
    try {
        $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Write-Log "Running as administrator: $isAdmin" "INFO"
    }
    catch {
        $isAdmin = $false
        Write-Log "Could not determine admin status, assuming non-admin" "WARN"
    }
    
    # Check if PowerCLI is already installed
    Write-Log "Checking if PowerCLI is already installed..." "INFO"
    try {
        $existingModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($existingModule) {
            Write-Log "PowerCLI is already installed. Version: $($existingModule.Version)" "INFO"
            Write-Output "Success: PowerCLI is already installed. Version: $($existingModule.Version)"
            return
        }
        else {
            Write-Log "PowerCLI not found. Starting installation..." "INFO"
        }
    }
    catch {
        Write-Log "Error checking existing PowerCLI installation: $($_.Exception.Message)" "WARN"
        Write-Log "Proceeding with installation attempt..." "INFO"
    }
    
    # Use external PowerShell process for installation
    Write-Log "Using external PowerShell process for installation..." "INFO"
    Write-Log "This approach bypasses .NET SDK PowerShell limitations" "INFO"
    
    # Determine scope and create installation command
    $scope = if ($isAdmin) { "AllUsers" } else { "CurrentUser" }
    Write-Log "Installing for scope: $scope" "INFO"
    
    # Create the PowerShell command for external execution
    $installCommand = @"
try {
    Write-Host "Setting PowerShell Gallery as trusted..."
    Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction Stop
    
    Write-Host "Installing VMware.PowerCLI module..."
    Write-Host "This may take several minutes depending on your internet connection..."
    
    Install-Module -Name 'VMware.PowerCLI' -Scope '$scope' -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
    
    Write-Host "Verifying installation..."
    `$module = Get-Module -ListAvailable -Name 'VMware.PowerCLI' -ErrorAction Stop
    
    if (`$module) {
        Write-Host "SUCCESS: VMware.PowerCLI version `$(`$module.Version) installed successfully"
        
        # Test import
        Import-Module -Name 'VMware.PowerCLI' -Force -ErrorAction Stop
        Remove-Module -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
        Write-Host "SUCCESS: Module import test passed"
    } else {
        Write-Host "ERROR: Module not found after installation"
        exit 1
    }
}
catch {
    Write-Host "ERROR: Installation failed - `$(`$_.Exception.Message)"
    exit 1
}
finally {
    try {
        Set-PSRepository -Name 'PSGallery' -InstallationPolicy Untrusted -ErrorAction SilentlyContinue
    } catch {}
}
"@

    Write-Log "Executing installation command in external PowerShell process..." "INFO"
    
    # Execute the installation in an external PowerShell process
    $processArgs = @{
        FilePath = "powershell.exe"
        ArgumentList = @(
            "-NoProfile"
            "-ExecutionPolicy", "Unrestricted"
            "-Command", $installCommand
        )
        Wait = $true
        PassThru = $true
        NoNewWindow = $true
        RedirectStandardOutput = $true
        RedirectStandardError = $true
    }
    
    $process = Start-Process @processArgs
    
    # Capture output
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $exitCode = $process.ExitCode
    
    Write-Log "External PowerShell process completed with exit code: $exitCode" "INFO"
    
    if ($stdout) {
        Write-Log "Standard Output:" "INFO"
        $stdout.Split("`n") | ForEach-Object { 
            if ($_.Trim()) { Write-Log $_.Trim() "INFO" }
        }
    }
    
    if ($stderr) {
        Write-Log "Standard Error:" "ERROR"
        $stderr.Split("`n") | ForEach-Object { 
            if ($_.Trim()) { Write-Log $_.Trim() "ERROR" }
        }
    }
    
    if ($exitCode -eq 0) {
        Write-Log "PowerCLI installation completed successfully" "INFO"
        
        # Verify installation in current session
        Write-Log "Verifying installation in current session..." "INFO"
        $verifyModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($verifyModule) {
            Write-Log "SUCCESS: PowerCLI installation verified. Version: $($verifyModule.Version)" "INFO"
            Write-Output "Success: VMware.PowerCLI version $($verifyModule.Version) installed and verified successfully"
        }
        else {
            Write-Log "WARNING: Installation reported success but module not visible in current session" "WARN"
            Write-Output "Warning: Installation completed but module may require PowerShell restart to be visible"
        }
    }
    else {
        Write-Log "PowerCLI installation failed with exit code: $exitCode" "ERROR"
        Write-Output "Failure: PowerCLI installation failed. Check logs for details."
        
        if ($stderr -match "administrator") {
            Write-Output "Try running the application as Administrator or install PowerCLI manually:"
            Write-Output "Open PowerShell as Administrator and run: Install-Module -Name VMware.PowerCLI -Force"
        }
    }
}
catch {
    $errorMessage = "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    Write-Output $errorMessage
    Write-Output ""
    Write-Output "Manual installation instructions:"
    Write-Output "1. Open PowerShell as Administrator"
    Write-Output "2. Run: Install-Module -Name VMware.PowerCLI -Force"
    Write-Output "3. Restart this application"
}
finally {
    Write-Log "PowerCLI installation script completed" "INFO"
}