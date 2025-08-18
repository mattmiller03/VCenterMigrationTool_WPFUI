# Install-PowerCli.ps1 - Fixed version with integrated logging
param(
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Install-PowerCli"

try {
    Write-LogInfo "Starting PowerCLI installation process"
    Write-LogInfo "PowerShell version: $($PSVersionTable.PSVersion.ToString())" -Category "System"
    
    # Check if running as administrator
    try {
        $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        Write-LogInfo "Running as administrator: $isAdmin" -Category "System"
    }
    catch {
        $isAdmin = $false
        Write-LogWarning "Could not determine admin status, assuming non-admin"
    }
    
    # Check if PowerCLI is already installed
    Write-LogInfo "Checking if PowerCLI is already installed..." -Category "Check"
    try {
        $existingModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        
        if ($existingModule) {
            $version = $existingModule[0].Version.ToString()
            Write-LogSuccess "PowerCLI is already installed. Version: $version" -Category "Check"
            
            # Test import to make sure it works
            try {
                Import-Module -Name "VMware.PowerCLI" -Force -ErrorAction Stop
                Write-LogSuccess "PowerCLI import test successful"
                Remove-Module -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
                
                Stop-ScriptLogging -Success $true -Summary "PowerCLI already installed and working - Version: $version" -Statistics @{"Version" = $version; "AlreadyInstalled" = $true}
                Write-Output "Success: PowerCLI is already installed and working. Version: $version"
                return
            }
            catch {
                Write-LogWarning "PowerCLI is installed but import test failed: $($_.Exception.Message)"
                Write-LogInfo "Attempting reinstallation..."
            }
        }
        else {
            Write-LogInfo "PowerCLI not found. Starting installation..." -Category "Check"
        }
    }
    catch {
        Write-LogWarning "Error checking existing PowerCLI installation: $($_.Exception.Message)"
        Write-LogInfo "Proceeding with installation attempt..."
    }
    
    # Determine scope
    $scope = if ($isAdmin) { "AllUsers" } else { "CurrentUser" }
    Write-LogInfo "Installing for scope: $scope" -Category "Install"
    
    # DIRECT INSTALLATION - No external process
    Write-LogInfo "Starting direct PowerCLI installation in current PowerShell session..." -Category "Install"
    
    try {
        # Check if PowerShellGet is available
        if (Get-Command "Get-PSRepository" -ErrorAction SilentlyContinue) {
            Write-LogSuccess "PowerShellGet commands are available"
            
            # Check repository
            try {
                $repo = Get-PSRepository -Name "PSGallery" -ErrorAction Stop
                Write-LogInfo "PSGallery repository found. Trust: $($repo.InstallationPolicy)" -Category "Repository"
            }
            catch {
                Write-LogCritical "Cannot access PSGallery repository: $($_.Exception.Message)" -Category "Repository"
                Write-Output "Failure: Cannot access PowerShell Gallery. Check internet connection."
                return
            }
            
            # Set repository to trusted
            try {
                Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction Stop
                Write-LogSuccess "Repository set to trusted" -Category "Repository"
            }
            catch {
                Write-LogCritical "Cannot set repository trust: $($_.Exception.Message)" -Category "Repository"
                Write-Output "Failure: Cannot configure PowerShell Gallery trust settings."
                return
            }
            
            # Test internet connectivity
            try {
                $testConnection = Test-NetConnection -ComputerName "www.powershellgallery.com" -Port 443 -ErrorAction Stop
                if ($testConnection.TcpTestSucceeded) {
                    Write-LogSuccess "Internet connectivity to PowerShell Gallery: OK" -Category "Connectivity"
                } else {
                    Write-LogCritical "Cannot connect to PowerShell Gallery" -Category "Connectivity"
                    Write-Output "Failure: Cannot connect to PowerShell Gallery"
                    return
                }
            }
            catch {
                Write-LogWarning "Could not test connectivity: $($_.Exception.Message)" -Category "Connectivity"
            }
            
            # Attempt installation
            try {
                Write-LogInfo "Starting VMware.PowerCLI installation..." -Category "Install"
                $installStartTime = Get-Date
                
                Install-Module -Name 'VMware.PowerCLI' -Scope $scope -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
                
                $installTime = (Get-Date) - $installStartTime
                Write-LogSuccess "Installation command completed in $($installTime.TotalSeconds) seconds" -Category "Install"
            }
            catch {
                Write-LogCritical "Install-Module failed: $($_.Exception.Message)" -Category "Install"
                Write-LogError "Exception Type: $($_.Exception.GetType().Name)"
                
                Stop-ScriptLogging -Success $false -Summary "PowerCLI installation failed: $($_.Exception.Message)"
                Write-Output "Failure: PowerCLI installation failed. Details: $($_.Exception.Message)"
                return
            }
            
            # Verify installation
            try {
                Write-LogInfo "Verifying installation..." -Category "Verify"
                $module = Get-Module -ListAvailable -Name 'VMware.PowerCLI' -ErrorAction Stop
                
                if ($module) {
                    $installedVersion = $module[0].Version.ToString()
                    Write-LogSuccess "Module found after installation. Version: $installedVersion" -Category "Verify"
                    
                    # Test import
                    try {
                        Write-LogInfo "Testing module import..." -Category "Verify"
                        Import-Module -Name 'VMware.PowerCLI' -Force -ErrorAction Stop
                        Write-LogSuccess "Module import test passed" -Category "Verify"
                        
                        # Test a basic command
                        try {
                            $null = Get-Command "Connect-VIServer" -ErrorAction Stop
                            Write-LogSuccess "PowerCLI commands are available" -Category "Verify"
                        }
                        catch {
                            Write-LogWarning "PowerCLI commands not found: $($_.Exception.Message)" -Category "Verify"
                        }
                        
                        Remove-Module -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
                        Write-LogDebug "Test module removed"
                        
                        # Create success statistics
                        $stats = @{
                            "Version" = $installedVersion
                            "Scope" = $scope
                            "InstallTimeSeconds" = [math]::Round($installTime.TotalSeconds, 2)
                            "IsAdmin" = $isAdmin
                            "PowerShellVersion" = $PSVersionTable.PSVersion.ToString()
                        }
                        
                        Stop-ScriptLogging -Success $true -Summary "VMware.PowerCLI installed successfully - Version: $installedVersion" -Statistics $stats
                        Write-Output "Success: VMware.PowerCLI installed successfully. Version: $installedVersion"
                    }
                    catch {
                        Write-LogWarning "Module installed but import failed: $($_.Exception.Message)" -Category "Verify"
                        Stop-ScriptLogging -Success $false -Summary "PowerCLI installed but import test failed"
                        Write-Output "Partial Success: PowerCLI installed but import test failed."
                    }
                } else {
                    Write-LogCritical "Module not found after installation" -Category "Verify"
                    Stop-ScriptLogging -Success $false -Summary "Module not found after installation"
                    Write-Output "Failure: Module not found after installation"
                }
            }
            catch {
                Write-LogCritical "Module verification failed: $($_.Exception.Message)" -Category "Verify"
                Stop-ScriptLogging -Success $false -Summary "Module verification failed"
                Write-Output "Failure: Module verification failed"
            }
        }
        else {
            Write-LogCritical "PowerShellGet commands not available" -Category "System"
            Stop-ScriptLogging -Success $false -Summary "PowerShellGet module not available"
            Write-Output "Failure: PowerShellGet module not available"
        }
    }
    catch {
        Write-LogCritical "FATAL ERROR: $($_.Exception.Message)"
        Write-LogError "Exception Type: $($_.Exception.GetType().Name)"
        Write-LogError "Stack trace: $($_.ScriptStackTrace)"
        
        Stop-ScriptLogging -Success $false -Summary "Fatal error during installation: $($_.Exception.Message)"
        Write-Output "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    }
    finally {
        # Reset repository settings
        try {
            Set-PSRepository -Name 'PSGallery' -InstallationPolicy Untrusted -ErrorAction SilentlyContinue
            Write-LogInfo "Repository trust settings reset" -Category "Cleanup"
        } catch {
            Write-LogWarning "Could not reset repository settings"
        }
    }
}
catch {
    $errorMessage = "Failure: An unexpected error occurred during installation. Details: $($_.Exception.Message)"
    Write-LogCritical $errorMessage
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    Stop-ScriptLogging -Success $false -Summary $errorMessage
    Write-Output $errorMessage
}
finally {
    Write-LogInfo "PowerCLI installation script completed"
    
    # Always suggest manual installation as backup
    Write-Output ""
    Write-Output "If automatic installation fails, install manually:"
    Write-Output "1. Open PowerShell 7 as Administrator"
    Write-Output "2. Run: Install-Module -Name VMware.PowerCLI -Force"
    Write-Output "3. Restart this application"
}