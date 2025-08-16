param(
    [Parameter(Mandatory = $true)]
    [string]$VCenterServer,
    
    [Parameter(Mandatory = $true)]
    [System.Management.Automation.PSCredential]$Credential,
    
    [bool]$BypassModuleCheck = $false,
    [string]$LogPath = ""
)

# Function to write log messages
function Write-Log {
    param([string]$Message, [string]$Level = "Info")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Host $logMessage
    
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        try {
            $logMessage | Out-File -FilePath $LogPath -Append -Encoding UTF8
        }
        catch {
            # Ignore log file errors
        }
    }
}

try {
    Write-Log "Starting VM discovery from vCenter: $VCenterServer" "Info"
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Importing PowerCLI modules..." "Info"
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
        Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    }
    
    # Connect to vCenter using PSCredential
    Write-Log "Connecting to vCenter..." "Info"
    $connection = Connect-VIServer -Server $VCenterServer -Credential $Credential -Force
    
    if (-not $connection.IsConnected) {
        throw "Failed to connect to vCenter server"
    }
    
    Write-Log "Successfully connected, retrieving VMs..." "Info"
    
    # Get all VMs
    $vms = Get-VM | Select-Object Name, PowerState, 
        @{N="EsxiHost";E={$_.VMHost.Name}},
        @{N="Datastore";E={($_.DatastoreIdList | Get-Datastore | Select-Object -First 1).Name}},
        @{N="Cluster";E={$_.VMHost.Parent.Name}},
        @{N="IsSelected";E={$false}}
    
    # Convert to JSON for output
    $jsonOutput = $vms | ConvertTo-Json -Depth 3
    Write-Output $jsonOutput
    
    Write-Log "VM discovery completed successfully. Found $($vms.Count) VMs" "Info"
    
    # Disconnect
    Disconnect-VIServer -Server $VCenterServer -Force -Confirm:$false
}
catch {
    $errorMsg = "VM discovery failed: $($_.Exception.Message)"
    Write-Log $errorMsg "Error"
    Write-Error $errorMsg
    exit 1
}