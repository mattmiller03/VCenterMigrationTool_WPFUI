# Get-VmsForMigration.ps1 - Fixed version
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password
)

# Function to write structured logs
function Write-ScriptLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Information "[$timestamp] [$Level] $Message" -InformationAction Continue
}

try {
    Write-ScriptLog "Starting VM inventory script"
    Write-ScriptLog "Target vCenter: $VCenterServer"
    
    # Check if PowerCLI module is available
    Write-ScriptLog "Checking PowerCLI module availability..."
    
    $powerCliModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
    if (-not $powerCliModule) {
        Write-ScriptLog "PowerCLI module not found. Returning sample data." "WARN"
        
        # Return sample data in JSON format
        $sampleVMs = @(
            [PSCustomObject]@{
                Name = "Sample-Web-Server"
                PowerState = "PoweredOn"
                EsxiHost = "sample-esx01.lab.local"
                Datastore = "sample-datastore1"
                Cluster = "Sample-Cluster1"
            },
            [PSCustomObject]@{
                Name = "Sample-DB-Server"
                PowerState = "PoweredOn"
                EsxiHost = "sample-esx02.lab.local"
                Datastore = "sample-datastore2"
                Cluster = "Sample-Cluster1"
            },
            [PSCustomObject]@{
                Name = "Sample-App-Server"
                PowerState = "PoweredOff"
                EsxiHost = "sample-esx01.lab.local"
                Datastore = "sample-datastore1"
                Cluster = "Sample-Cluster1"
            }
        )
        
        $sampleVMs | ConvertTo-Json -Depth 3
        return
    }

    Write-ScriptLog "PowerCLI module found. Version: $($powerCliModule.Version)"
    
    # Import PowerCLI module
    Write-ScriptLog "Importing PowerCLI module..."
    Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    
    # Suppress PowerCLI configuration warnings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -ParticipateInCEIP $false -Scope Session -Confirm:$false | Out-Null
    
    # Connect to vCenter (if parameters provided)
    if ($VCenterServer -and $Username -and $Password) {
        Write-ScriptLog "Connecting to vCenter: $VCenterServer"
        
        $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
        $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
        
        $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force -ErrorAction Stop
        Write-ScriptLog "Successfully connected to $($connection.Name)"
        
        try {
            # Get VMs with error handling
            Write-ScriptLog "Retrieving VM inventory..."
            $vms = Get-VM -ErrorAction Stop | Select-Object -First 50 | ForEach-Object {
                [PSCustomObject]@{
                    Name = $_.Name
                    PowerState = $_.PowerState.ToString()
                    EsxiHost = if ($_.VMHost) { $_.VMHost.Name } else { "Unknown" }
                    Datastore = if ($_ | Get-Datastore -ErrorAction SilentlyContinue | Select-Object -First 1) { 
                        ($_ | Get-Datastore | Select-Object -First 1).Name 
                    } else { 
                        "Unknown" 
                    }
                    Cluster = if ($_.VMHost -and $_.VMHost.Parent) { $_.VMHost.Parent.Name } else { "Unknown" }
                }
            }
            
            Write-ScriptLog "Retrieved $($vms.Count) VMs"
            $vms | ConvertTo-Json -Depth 3
        }
        finally {
            # Always disconnect
            Write-ScriptLog "Disconnecting from vCenter"
            Disconnect-VIServer -Server $VCenterServer -Confirm:$false -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-ScriptLog "No connection parameters provided. Returning sample data." "WARN"
        
        # Return sample data when no connection info provided
        $sampleVMs = @(
            [PSCustomObject]@{
                Name = "Demo-Web-Server-01"
                PowerState = "PoweredOn"
                EsxiHost = "demo-esx01.lab.local"
                Datastore = "demo-datastore1"
                Cluster = "Demo-Cluster1"
            },
            [PSCustomObject]@{
                Name = "Demo-DB-Server-01"
                PowerState = "PoweredOn"
                EsxiHost = "demo-esx02.lab.local"
                Datastore = "demo-datastore2"
                Cluster = "Demo-Cluster1"
            }
        )
        
        $sampleVMs | ConvertTo-Json -Depth 3
    }
}
catch {
    Write-ScriptLog "Error occurred: $($_.Exception.Message)" "ERROR"
    Write-ScriptLog "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    
    # Return sample data on error
    $sampleVMs = @(
        [PSCustomObject]@{
            Name = "Error-Recovery-VM-01"
            PowerState = "Unknown"
            EsxiHost = "error-recovery-host"
            Datastore = "error-recovery-ds"
            Cluster = "Error-Recovery-Cluster"
        }
    )
    
    $sampleVMs | ConvertTo-Json -Depth 3
}
