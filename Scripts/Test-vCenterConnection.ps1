# In Scripts/Test-vCenterConnection.ps1

param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password
)

# Suppress errors for the connection attempt so we can handle them gracefully
$ErrorActionPreference = "SilentlyContinue"

try {
    # Attempt to connect. The -Force flag will overwrite any existing connections.
    $connection = Connect-VIServer -Server $VCenterServer -User $Username -Password $Password -Force
    
    if ($connection) {
        # If the connection object is not null, it was successful.
        Write-Output "Success"
    }
    else {
        # If the connection object is null, it failed. Grab the last error message.
        Write-Output "Failure: $($error[0].ToString())"
    }
}
catch {
    # Catch any unexpected script-terminating errors.
    Write-Output "Failure: $($_.Exception.Message)"
}
finally {
    # Always attempt to disconnect to clean up the session.
    if (Get-VIServer -Server $VCenterServer -ErrorAction SilentlyContinue) {
        Disconnect-VIServer -Server $VCenterServer -Confirm:$false
    }
}