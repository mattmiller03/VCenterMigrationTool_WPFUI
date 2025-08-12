param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,

    [Parameter(Mandatory = $true)]
    [string]$Username,

    [Parameter(Mandatory = $true)]
    [string]$Password
)

# Suppress errors for the connection attempt so we can handle them gracefully.
$ErrorActionPreference = "SilentlyContinue"

try {
    # Create a secure PSCredential object. This is the key change.
    # It converts the plain-text password into a SecureString.
    $credential = New-Object System.Management.Automation.PSCredential($Username, (ConvertTo-SecureString -String $Password -AsPlainText -Force))

    # Attempt to connect using the explicit credential object.
    $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force
    
    if ($connection) {
        # If the connection object is not null, it was successful.
        Write-Output "Success"
    }
    else {
        # If the connection object is null, it failed. Grab the last error message.
        # This will now give a specific "invalid credentials" error instead of using Windows auth.
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