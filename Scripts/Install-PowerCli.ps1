# Install-PowerCli.ps1 - Fixed version that doesn't create subprocess
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
    
    # Determine scope
    $scope = if ($isAdmin) { "AllUsers" } else { "CurrentUser" }
    Write-Log "Installing for scope: $scope" "INFO"
    
    # DIRECT INSTALLATION - No external process
    Write-Log "Starting direct PowerCLI installation in current PowerShell session..." "INFO"
    
    try {
        # Check if PowerShellGet is available
        if (Get-Command "Get-PSRepository" -ErrorAction SilentlyContinue) {
            Write-Log "PowerShellGet commands are available" "INFO"
            
            # Check repository
            try {
                $repo = Get-PSRepository -Name "PSGallery" -ErrorAction Stop
                Write-Log "PSGallery repository found. Trust: $($repo.InstallationPolicy)" "INFO"
            }
            catch {
                Write-Log "ERROR: Cannot access PSGallery repository: $($_.Exception.Message)" "ERROR"
                Write-Output "Failure: Cannot access PowerShell Gallery. Check internet connection."
                return
            }
            
            # Set repository to trusted
            try {
                Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction Stop
                Write-Log "Repository set to trusted" "INFO"
            }
            catch {
                Write-Log "ERROR: Cannot set repository trust: $($_.Exception.Message)" "ERROR"
                Write-Output "Failure: Cannot configure PowerShell Gallery trust settings."
                return
            }
            
            # Test internet connectivity
            try {
                $testConnection = Test-NetConnection -ComputerName "www.powershellgallery.com" -Port 443 -ErrorAction Stop
                if ($testConnection.TcpTestSucceeded) {
                    Write-Log "Internet connectivity to PowerShell Gallery: OK" "INFO"
                } else {
                    Write-Log "ERROR: Cannot connect to PowerShell Gallery" "ERROR"
                    Write-Output "Failure: Cannot connect to PowerShell Gallery"
                    return
                }
            }
            catch {
                Write-Log "WARNING: Could not test connectivity: $($_.Exception.Message)" "WARN"
            }
            
            # Attempt installation
            try {
                Write-Log "Starting VMware.PowerCLI installation..." "INFO"
                Install-Module -Name 'VMware.PowerCLI' -Scope $scope -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
                Write-Log "Installation command completed" "INFO"
            }
            catch {
                Write-Log "ERROR: Install-Module failed: $($_.Exception.Message)" "ERROR"
                Write-Log "Exception Type: $($_.Exception.GetType().Name)" "ERROR"
                Write-Output "Failure: PowerCLI installation failed. Details: $($_.Exception.Message)"
                return
            }
            
            # Verify installation
            try {
                $module = Get-Module -ListAvailable -Name 'VMware.PowerCLI' -ErrorAction Stop
                if ($module) {
                    Write-Log "SUCCESS: Module found after installation. Version: $($module.Version)" "INFO"
                    
                    # Test import
                    try {
                        Import-Module -Name 'VMware.PowerCLI' -Force -ErrorAction Stop
                        Write-Log "SUCCESS: Module import test passed" "INFO"
                        Remove-Module -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
                        Write-Output "Success: VMware.PowerCLI installed successfully. Version: $($module.Version)"
                    }
                    catch {
                        Write-Log "WARNING: Module installed but import failed: $($_.Exception.Message)" "WARN"
                        Write-Output "Partial Success: PowerCLI installed but import test failed."
                    }
                } else {
                    Write-Log "ERROR: Module not found after installation" "ERROR"
                    Write-Output "Failure: Module not found after installation"
                }
            }
            catch {
                Write-Log "ERROR: Module verification failed: $($_.Exception.Message)" "ERROR"
                Write-Output "Failure: Module verification failed"
            }
        }
        else {
            Write-Log "ERROR: PowerShellGet commands not available" "ERROR"
            Write-Output "Failure: PowerShellGet module not available"
        }
    }
    catch {
        Write-Log "FATAL ERROR: $($_.Exception.Message)" "ERROR"
        Write-Log "Exception Type: $($_.Exception.GetType().Name)" "ERROR"
        Write-Output "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    }
    finally {
        # Reset repository settings
        try {
            Set-PSRepository -Name 'PSGallery' -InstallationPolicy Untrusted -ErrorAction SilentlyContinue
            Write-Log "Repository trust settings reset" "INFO"
        } catch {
            Write-Log "Could not reset repository settings" "WARN"
        }
    }
}
catch {
    $errorMessage = "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    Write-Log $errorMessage "ERROR"
    Write-Output $errorMessage
}
finally {
    Write-Log "PowerCLI installation script completed" "INFO"
    
    # Always suggest manual installation as backup
    Write-Output ""
    Write-Output "If automatic installation fails, install manually:"
    Write-Output "1. Open PowerShell 7 as Administrator"
    Write-Output "2. Run: Install-Module -Name VMware.PowerCLI -Force"
    Write-Output "3. Restart this application"
}