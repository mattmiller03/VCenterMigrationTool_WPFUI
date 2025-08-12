# In Scripts/Install-PowerCli.ps1
param()

try {
    # Set the repository to trust it for this session to avoid prompts
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted

    # Install the module for the current user, which doesn't require admin rights
    Install-Module -Name VMware.PowerCLI -Scope CurrentUser -Force -AllowClobber
    
    # Check if it was successful
    if (Get-Module -ListAvailable -Name VMware.PowerCLI) {
        Write-Output "Success: VMware.PowerCLI module installed successfully."
    }
    else {
        Write-Output "Failure: Module installation failed. Please check your internet connection and try running as an administrator if issues persist."
    }
}
catch {
    Write-Output "Failure: An error occurred during installation. Details: $($_.Exception.Message)"
}