# Install-PowerCli.ps1 - Diagnostic version with detailed error reporting
param(
    [string]$LogPath = "Logs"
)
# Force PowerShell 7 if we're running in Windows PowerShell
if ($PSVersionTable.PSVersion.Major -lt 6) {
    Write-Information "Detected Windows PowerShell $($PSVersionTable.PSVersion). Switching to PowerShell 7..." -InformationAction Continue
    
    # Try to find PowerShell 7
    $pwshPaths = @(
        "pwsh.exe",
        "C:\Program Files\PowerShell\7\pwsh.exe",
        "C:\Program Files (x86)\PowerShell\7\pwsh.exe"
    )
    
    foreach ($pwshPath in $pwshPaths) {
        try {
            if ($pwshPath -like "*\*") {
                # Full path - check if file exists
                if (Test-Path $pwshPath) {
                    $pwshExe = $pwshPath
                    break
                }
            } else {
                # Just executable name - test if it's in PATH
                $null = Get-Command $pwshPath -ErrorAction Stop
                $pwshExe = $pwshPath
                break
            }
        }
        catch {
            continue
        }
    }
    
    if ($pwshExe) {
        Write-Information "Found PowerShell 7 at: $pwshExe" -InformationAction Continue
        Write-Information "Restarting installation with PowerShell 7..." -InformationAction Continue
        
        # Rebuild the argument list
        $scriptPath = $MyInvocation.MyCommand.Path
        $arguments = @()
        if ($LogPath) { $arguments += "-LogPath `"$LogPath`"" }
        
        # Execute this script again with PowerShell 7
        $argumentString = $arguments -join " "
        $process = Start-Process -FilePath $pwshExe -ArgumentList "-NoProfile", "-ExecutionPolicy", "Unrestricted", "-File", "`"$scriptPath`"", $argumentString -Wait -PassThru -NoNewWindow
        
        # Exit with the same exit code
        exit $process.ExitCode
    }
    else {
        Write-Error "PowerShell 7 not found. Please install PowerShell 7 first."
        exit 1
    }
}
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
    
    # Create a diagnostic installation script
    $tempScriptPath = Join-Path $env:TEMP "InstallPowerCLI.ps1"
    Write-Log "Creating temporary installation script: $tempScriptPath" "INFO"
    
    # Enhanced diagnostic script
    $installScriptContent = @"
# Diagnostic PowerCLI Installation Script
`$ErrorActionPreference = 'Stop'
`$ProgressPreference = 'SilentlyContinue'

try {
    # Output basic info
    "PowerShell Version: `$(`$PSVersionTable.PSVersion)"
    "Execution Policy: `$(Get-ExecutionPolicy)"
    "User: `$(`$env:USERNAME)"
    
    # Check if PowerShellGet is available
    if (Get-Command "Get-PSRepository" -ErrorAction SilentlyContinue) {
        "PowerShellGet commands are available"
        
        # Check repository
        try {
            `$repo = Get-PSRepository -Name "PSGallery" -ErrorAction Stop
            "PSGallery repository found. Trust: `$(`$repo.InstallationPolicy)"
        }
        catch {
            "ERROR: Cannot access PSGallery repository: `$(`$_.Exception.Message)"
            exit 10
        }
        
        # Set repository to trusted
        try {
            Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction Stop
            "Repository set to trusted"
        }
        catch {
            "ERROR: Cannot set repository trust: `$(`$_.Exception.Message)"
            exit 11
        }
        
        # Test internet connectivity
        try {
            `$testConnection = Test-NetConnection -ComputerName "www.powershellgallery.com" -Port 443 -ErrorAction Stop
            if (`$testConnection.TcpTestSucceeded) {
                "Internet connectivity to PowerShell Gallery: OK"
            } else {
                "ERROR: Cannot connect to PowerShell Gallery"
                exit 12
            }
        }
        catch {
            "WARNING: Could not test connectivity: `$(`$_.Exception.Message)"
        }
        
        # Attempt installation
        try {
            "Starting VMware.PowerCLI installation..."
            Install-Module -Name 'VMware.PowerCLI' -Scope '$scope' -Force -AllowClobber -SkipPublisherCheck -ErrorAction Stop
            "Installation command completed"
        }
        catch {
            "ERROR: Install-Module failed: `$(`$_.Exception.Message)"
            "Exception Type: `$(`$_.Exception.GetType().Name)"
            exit 13
        }
        
        # Verify installation
        try {
            `$module = Get-Module -ListAvailable -Name 'VMware.PowerCLI' -ErrorAction Stop
            if (`$module) {
                "SUCCESS: Module found after installation. Version: `$(`$module.Version)"
                
                # Test import
                try {
                    Import-Module -Name 'VMware.PowerCLI' -Force -ErrorAction Stop
                    "SUCCESS: Module import test passed"
                    Remove-Module -Name 'VMware.PowerCLI' -ErrorAction SilentlyContinue
                    exit 0
                }
                catch {
                    "WARNING: Module installed but import failed: `$(`$_.Exception.Message)"
                    exit 14
                }
            } else {
                "ERROR: Module not found after installation"
                exit 15
            }
        }
        catch {
            "ERROR: Module verification failed: `$(`$_.Exception.Message)"
            exit 16
        }
    }
    else {
        "ERROR: PowerShellGet commands not available"
        exit 20
    }
}
catch {
    "FATAL ERROR: `$(`$_.Exception.Message)"
    "Exception Type: `$(`$_.Exception.GetType().Name)"
    exit 99
}
finally {
    # Reset repository settings
    try {
        Set-PSRepository -Name 'PSGallery' -InstallationPolicy Untrusted -ErrorAction SilentlyContinue
    } catch {}
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
    
    Write-Log "Executing diagnostic installation script..." "INFO"
    
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
        $process.WaitForExit(600000)  # 10 minute timeout
        $exitCode = $process.ExitCode
        
        Write-Log "External PowerShell process completed with exit code: $exitCode" "INFO"
        
        # Log detailed output
        if ($stdout.Trim()) {
            Write-Log "=== DIAGNOSTIC OUTPUT ===" "INFO"
            $stdout.Split("`n") | ForEach-Object { 
                if ($_.Trim()) { Write-Log "  $($_.Trim())" "INFO" }
            }
        }
        
        if ($stderr.Trim()) {
            Write-Log "=== DIAGNOSTIC ERRORS ===" "ERROR"
            $stderr.Split("`n") | ForEach-Object { 
                if ($_.Trim()) { Write-Log "  $($_.Trim())" "ERROR" }
            }
        }
        
        # Interpret specific exit codes
        switch ($exitCode) {
            0 {
                Write-Log "SUCCESS: PowerCLI installation completed successfully" "INFO"
                Write-Output "Success: VMware.PowerCLI installed successfully"
            }
            10 {
                Write-Log "ERROR: Cannot access PSGallery repository" "ERROR"
                Write-Output "Failure: Cannot access PowerShell Gallery. Check internet connection."
            }
            11 {
                Write-Log "ERROR: Cannot set repository trust" "ERROR"
                Write-Output "Failure: Cannot configure PowerShell Gallery trust settings."
            }
            12 {
                Write-Log "ERROR: No internet connectivity to PowerShell Gallery" "ERROR"
                Write-Output "Failure: Cannot connect to PowerShell Gallery. Check firewall/proxy settings."
            }
            13 {
                Write-Log "ERROR: Install-Module command failed" "ERROR"
                Write-Output "Failure: PowerCLI installation failed. See diagnostic output above."
            }
            20 {
                Write-Log "ERROR: PowerShellGet not available" "ERROR"
                Write-Output "Failure: PowerShellGet module not available in external PowerShell."
            }
            default {
                Write-Log "ERROR: Installation failed with exit code $exitCode" "ERROR"
                Write-Output "Failure: PowerCLI installation failed. Check diagnostic output above."
            }
        }
        
        # Always suggest manual installation as backup
        Write-Output ""
        Write-Output "If automatic installation fails, install manually:"
        Write-Output "1. Open PowerShell as Administrator"
        Write-Output "2. Run: Install-Module -Name VMware.PowerCLI -Force"
        Write-Output "3. Restart this application"
        
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