# In Scripts/Export-vCenterConfig.ps1

param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,

    [Parameter(Mandatory=$true)]
    [string]$User,

    [Parameter(Mandatory=$true)]
    [string]$Password,
    
    [Parameter(Mandatory=$true)]
    [string]$ExportPath
)

# Use Write-Information for progress updates that will show in the UI
Write-Information "Attempting to connect to $($VCenterServer)..."

# Example: Exporting vDS configuration
# NOTE: This assumes you have the PowerCLI module installed where this app runs.
# You will replace this with your actual, more detailed export logic.

try {
    # Connect to vCenter (add your connection logic here)
    # Connect-VIServer -Server $VCenterServer -User $User -Password $Password
    Write-Information "Successfully connected to $($VCenterServer)."

    if (-not (Test-Path -Path $ExportPath)) {
        New-Item -Path $ExportPath -ItemType Directory | Out-Null
    }
    
    Write-Information "Exporting vDS configurations to $($ExportPath)..."
    # Get-VDSwitch | Export-Vds -FolderPath $ExportPath -Confirm:$false
    
    Write-Information "Export complete!"
}
catch {
    # Use Write-Error to send exceptions back to the C# app
    Write-Error "An error occurred: $_"
}
finally {
    # Disconnect
    # Disconnect-VIServer -Server $VCenterServer -Confirm:$false
    Write-Information "Disconnected from server."
}