# In Scripts/Get-Prerequisites.ps1

try {
    # Check if the PowerCLI module is available
    $powerCliModule = Get-Module -ListAvailable -Name VMware.PowerCLI

    # Create a custom object with the results
    $result = [PSCustomObject]@{
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        IsPowerCliInstalled = [bool]$powerCliModule
    }

    # Convert the results object to a JSON string for C# to read
    $result | ConvertTo-Json
}
catch {
    # In case of a script error, return a failure object
    $errorResult = [PSCustomObject]@{
        PowerShellVersion   = "Error"
        IsPowerCliInstalled = $false
    }
    $errorResult | ConvertTo-Json
}