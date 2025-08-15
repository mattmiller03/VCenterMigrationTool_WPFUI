# Install-PowerCli.ps1 - Minimal approach with basic commands only
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
    
    # Determine scope
    $scope = if ($isAdmin) { "AllUsers" } else { "CurrentUser" }
    Write-Log "Installing for scope: $scope" "INFO"
    
    # Create a minimal installation script with only essential commands
    $tempScriptPath = Join-Path $env:TEMP "InstallPowerCLI.ps1"
    Write-Log "Creating temporary installation script: $tempScriptPath" "INFO"
    
    # Use only basic PowerShell commands that should always be available
    $installScriptContent = @"
# Minimal PowerCLI Installation Script - Basic commands only
try {
    # Set repository to trusted (required for installation)
    Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction Stop
    
    # Install PowerCLI
    Install-Module -Name 'VMware.PowerCLI' -Scope '$scope' -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
    
    # Verify installation
    `$module = Get-Module -ListAvailable -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
    
    if (`$module) {
        # Test import
        Import-Module -Name 'VMware.PowerCLI' -Force -ErrorAction Stop
        Remove-Module -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
        exit 0  # Success
    } else {
        exit 1  # Module not found after installation
    }
}
catch {
    exit 2  # Installation failed
}
finally {
    # Reset repository settings
    try {
        Set-PSRepository -Name 'PSGallery' -InstallationPolicy Untrusted -ErrorAction SilentlyContinue
    } catch {
        # Ignore reset errors
    }
}
"@

    # Write the installation script to temp file
    try {
        Set-Content -Path $tempScriptPath -Value $installScriptContent -Encoding UTF8
        Write-Log "Temporary script created successfully" "INFO"
    }
    catch {
        Write-Log "Failed to create temporary script: $($_.Exception.Message)" "ERROR"
        throw "Could not create temporary installation script"
    }
    
    Write-Log "Executing installation script in external PowerShell process..." "INFO"
    
    # Execute the installation script
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell.exe"
        $psi.Arguments = "-NoProfile -ExecutionPolicy Unrestricted -File `"$tempScriptPath`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $psi
        
        Write-Log "Starting external PowerShell process..." "INFO"
        $started = $process.Start()
        
        if (-not $started) {
            throw "Failed to start PowerShell process"
        }
        
        # Read output
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        
        # Wait for completion with timeout
        $process.WaitForExit(600000)  # 10 minute timeout for PowerCLI download
        $exitCode = $process.ExitCode
        
        Write-Log "External PowerShell process completed with exit code: $exitCode" "INFO"
        
        # Log any output (but don't treat stderr as fatal since we know Write-Host fails)
        if ($stdout.Trim()) {
            Write-Log "Process output: $($stdout.Trim())" "INFO"
        }
        
        if ($stderr.Trim() -and -not ($stderr -like "*Write-Host*")) {
            Write-Log "Process errors (excluding Write-Host): $($stderr.Trim())" "ERROR"
        }
        
        # Interpret exit codes
        switch ($exitCode) {
            0 {
                Write-Log "PowerCLI installation completed successfully" "INFO"
                
                # Verify installation in current session
                Write-Log "Verifying installation in current session..." "INFO"
                Start-Sleep -Seconds 2  # Give filesystem time to update
                
                $verifyModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
                
                if ($verifyModule) {
                    Write-Log "SUCCESS: PowerCLI installation verified. Version: $($verifyModule.Version)" "INFO"
                    Write-Output "Success: VMware.PowerCLI version $($verifyModule.Version) installed and verified successfully"
                }
                else {
                    Write-Log "Installation succeeded but module not yet visible. May need application restart." "WARN"
                    Write-Output "Success: PowerCLI installation completed. Restart the application to see the module."
                }
            }
            1 {
                Write-Log "Installation completed but module verification failed" "ERROR"
                Write-Output "Warning: Installation may have completed but module verification failed. Try restarting the application."
            }
            2 {
                Write-Log "PowerCLI installation failed during module installation" "ERROR"
                Write-Output "Failure: PowerCLI installation failed. Check internet connection or try manual installation."
            }
            default {
                Write-Log "Unknown exit code: $exitCode" "ERROR"
                Write-Output "Warning: Installation completed with unexpected result. Try restarting the application."
            }
        }
        
        # Clean up process
        $process.Dispose()
    }
    catch {
        Write-Log "Error executing external PowerShell process: $($_.Exception.Message)" "ERROR"
        throw
    }
}
catch {
    $errorMessage = "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Output $errorMessage
    Write-Output ""
    Write-Output "Manual installation instructions:"
    Write-Output "1. Open PowerShell as Administrator"
    Write-Output "2. Run: Install-Module -Name VMware.PowerCLI -Force"
    Write-Output "3. Restart this application"
}
finally {
    # Clean up temporary script
    try {
        if ($tempScriptPath -and (Test-Path $tempScriptPath)) {
            Remove-Item $tempScriptPath -Force -ErrorAction SilentlyContinue
            Write-Log "Temporary script cleaned up" "INFO"
        }
    }
    catch {
        # Ignore cleanup errors
    }
    
    Write-Log "PowerCLI installation script completed" "INFO"
}

# Also suggest checking prerequisites after installation
Write-Output ""
Write-Output "After installation, click 'Check Prerequisites' to verify PowerCLI is detected."