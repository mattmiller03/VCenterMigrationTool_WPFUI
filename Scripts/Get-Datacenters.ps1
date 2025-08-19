# Get-Datacenters.ps1 - Retrieves datacenter information from vCenter
<#
.SYNOPSIS
    Retrieves datacenter names from vCenter for folder structure migration.

.DESCRIPTION
    This script connects to a vCenter server and retrieves the names of all datacenters
    for use in folder structure migration operations.

.PARAMETER VCenterServer
    The hostname or IP address of the vCenter Server.

.PARAMETER Username
    Username for vCenter authentication.

.PARAMETER Password
    Password for vCenter authentication.

.PARAMETER BypassModuleCheck
    Switch to bypass PowerCLI module verification for faster execution.

.PARAMETER LogPath
    Path for log file output.

.PARAMETER SuppressConsoleOutput
    Suppress console output for clean JSON returns.

.EXAMPLE
    .\Get-Datacenters.ps1 -VCenterServer "vcenter.lab.local" -Username "admin" -Password "password"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory=$true)]
    [pscredential]$viCredential,

    [Parameter()]
    [switch]$BypassModuleCheck,
    
    [string]$LogPath = "",
    
    [bool]$SuppressConsoleOutput = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Override Write-Host if console output is suppressed
if ($SuppressConsoleOutput) {
    function global:Write-Host {
        # Suppress all Write-Host output
    }
}

# Start logging
Start-ScriptLogging -ScriptName "Get-Datacenters" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

# Initialize result
$result = @()
$scriptSuccess = $true
$viConnection = $null

try {
    Write-LogInfo "Starting datacenter discovery from vCenter: $($VCenterServer)" -Category "Initialization"
    
    # Import PowerCLI modules if not bypassed
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..." -Category "Module"
        try {
            Import-Module VMware.VimAutomation.Core -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-LogSuccess "PowerCLI modules imported successfully" -Category "Module"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)" -Category "Module"
            throw $_
        }
    }
    else {
        Write-LogInfo "Bypassing PowerCLI module check" -Category "Module"
    }
    
    # Create credential object
    if (-not $viCredential) {
        Write-LogError "No credential provided. Please provide a valid PSCredential object." -Category "Authentication"
        throw "Credential is required to connect to vCenter"
    }
    
    # Connect to vCenter
    Write-LogInfo "Connecting to vCenter server: $($VCenterServer)" -Category "Connection"
    
    try {
        $viConnection = Connect-VIServer -Server $VCenterServer -Credential $viCredential -Force -ErrorAction Stop
        
        if ($viConnection.IsConnected) {
            Write-LogSuccess "Successfully connected to vCenter: $($viConnection.Name)" -Category "Connection"
            Write-LogInfo "  Version: $($viConnection.Version)" -Category "Connection"
            Write-LogInfo "  Build: $($viConnection.Build)" -Category "Connection"
        }
        else {
            throw "Connection object returned but IsConnected is false"
        }
    }
    catch {
        Write-LogError "Failed to connect to vCenter: $($_.Exception.Message)" -Category "Connection"
        throw $_
    }
    
    # Get all datacenters
    Write-LogInfo "Retrieving datacenter information..." -Category "Discovery"
    
    try {
        $datacenters = Get-Datacenter -ErrorAction Stop
        
        if ($datacenters) {
            Write-LogSuccess "Found $($datacenters.Count) datacenters" -Category "Discovery"
            
            foreach ($dc in $datacenters) {
                Write-LogDebug "Processing datacenter: $($dc.Name)" -Category "Discovery"
                
                $dcInfo = @{
                    Name = $dc.Name
                    Id = $dc.Id
                    NumHosts = ($dc | Get-VMHost).Count
                    NumClusters = ($dc | Get-Cluster).Count
                    NumVMs = ($dc | Get-VM).Count
                    NumDatastores = ($dc | Get-Datastore).Count
                    NumNetworks = ($dc | Get-VirtualPortGroup).Count
                }
                
                $result += $dcInfo
                
                Write-LogInfo "  Datacenter: $($dc.Name) - Hosts: $($dcInfo.NumHosts), Clusters: $($dcInfo.NumClusters), VMs: $($dcInfo.NumVMs)" -Category "Discovery"
            }
            
            # For backward compatibility, also create comma-separated list
            $datacenterNames = $datacenters | Select-Object -ExpandProperty Name
            $datacenterList = $datacenterNames -join ", "
            Write-LogInfo "Datacenter names: $datacenterList" -Category "Summary"
        }
        else {
            Write-LogWarning "No datacenters found in vCenter" -Category "Discovery"
        }
    }
    catch {
        Write-LogError "Failed to retrieve datacenters: $($_.Exception.Message)" -Category "Discovery"
        throw $_
    }
    
    Write-LogSuccess "Datacenter discovery completed successfully" -Category "Summary"
}
catch {
    $scriptSuccess = $false
    $errorMessage = "Datacenter discovery failed: $($_.Exception.Message)"
    Write-LogCritical $errorMessage -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Return error in JSON format if called from C#
    if ($SuppressConsoleOutput) {
        $errorResult = @{
            Success = $false
            Error = $_.Exception.Message
        }
        Write-Output ($errorResult | ConvertTo-Json -Compress)
    }
    else {
        Write-Output "Error: Could not retrieve datacenters"
    }
    
    Stop-ScriptLogging -Success $false -Summary $errorMessage
    exit 1
}
finally {
    # Disconnect from vCenter
    if ($viConnection -and $viConnection.IsConnected) {
        try {
            Write-LogInfo "Disconnecting from vCenter..." -Category "Connection"
            Disconnect-VIServer -Server $viConnection -Confirm:$false -Force -ErrorAction Stop
            Write-LogSuccess "Disconnected from vCenter" -Category "Connection"
        }
        catch {
            Write-LogWarning "Failed to disconnect cleanly: $($_.Exception.Message)" -Category "Connection"
        }
    }
    
    # Stop logging and output result
    if ($scriptSuccess) {
        $stats = @{
            "TotalDatacenters" = $result.Count
            "TotalHosts" = ($result | Measure-Object -Property NumHosts -Sum).Sum
            "TotalVMs" = ($result | Measure-Object -Property NumVMs -Sum).Sum
        }
        
        Stop-ScriptLogging -Success $true -Summary "Retrieved $($result.Count) datacenters" -Statistics $stats
        
        # Output result based on context
        if ($SuppressConsoleOutput -or $result.Count -gt 0) {
            # Output as JSON for C# consumption
            Write-Output ($result | ConvertTo-Json -Depth 3 -Compress)
        }
        else {
            # For backward compatibility, output comma-separated list
            $datacenterNames = $result | Select-Object -ExpandProperty Name
            $datacenterList = $datacenterNames -join ", "
            Write-Output $datacenterList
        }
    }
}