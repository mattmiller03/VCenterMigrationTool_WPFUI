# Get-TargetResources.ps1 - Optimized version
param(
    [string]$VCenterServer,
    [string]$Username,
    [string]$Password,
    [switch]$BypassModuleCheck = $false  # NEW: Allow bypassing PowerCLI checks
)

# Function to write structured logs
function Write-ScriptLog {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Information "[$timestamp] [$Level] $Message" -InformationAction Continue
}

try {
    Write-ScriptLog "Starting target resources script"
    Write-ScriptLog "Target vCenter: $VCenterServer"
    
    # OPTIMIZED: Only check PowerCLI if not bypassed
    if (-not $BypassModuleCheck) {
        Write-ScriptLog "Checking PowerCLI module availability..."
        
        $powerCliModule = Get-Module -ListAvailable -Name "VMware.PowerCLI" -ErrorAction SilentlyContinue
        if (-not $powerCliModule) {
            Write-ScriptLog "PowerCLI module not found. Returning sample data." "WARN"
            
            # Return sample data in JSON format
            $sampleData = @{
                Hosts = @(
                    @{ Name = "sample-target-esx01.lab.local" },
                    @{ Name = "sample-target-esx02.lab.local" },
                    @{ Name = "sample-target-esx03.lab.local" }
                )
                Datastores = @(
                    @{ Name = "sample-target-datastore1" },
                    @{ Name = "sample-target-datastore2" },
                    @{ Name = "sample-target-datastore3" }
                )
            }
            
            $sampleData | ConvertTo-Json -Depth 3
            return
        }

        Write-ScriptLog "PowerCLI module found. Importing..."
        Import-Module VMware.PowerCLI -Force -ErrorAction Stop
    } else {
        Write-ScriptLog "Bypassing PowerCLI module check (assumed available)"
        # Still try to import silently
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction SilentlyContinue
        } catch {
            # Ignore import errors when bypassing
        }
    }
    
    # Suppress PowerCLI configuration warnings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -ParticipateInCEIP $false -Scope Session -Confirm:$false | Out-Null
    
    # Connect to vCenter (if parameters provided)
    if ($VCenterServer -and $Username -and $Password) {
        Write-ScriptLog "Connecting to target vCenter: $VCenterServer"
        
        $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
        $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
        
        $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force -ErrorAction Stop
        Write-ScriptLog "Successfully connected to $($connection.Name)"
        
        try {
            # Get hosts and datastores
            Write-ScriptLog "Retrieving hosts and datastores..."
            
            $hosts = Get-VMHost -ErrorAction Stop | Select-Object -First 20 | ForEach-Object {
                @{ Name = $_.Name }
            }
            
            $datastores = Get-Datastore -ErrorAction Stop | Select-Object -First 20 | ForEach-Object {
                @{ Name = $_.Name }
            }
            
            $result = @{
                Hosts = $hosts
                Datastores = $datastores
            }
            
            Write-ScriptLog "Retrieved $($hosts.Count) hosts and $($datastores.Count) datastores"
            $result | ConvertTo-Json -Depth 3
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
        $sampleData = @{
            Hosts = @(
                @{ Name = "demo-target-esx01.lab.local" },
                @{ Name = "demo-target-esx02.lab.local" },
                @{ Name = "demo-target-esx03.lab.local" }
            )
            Datastores = @(
                @{ Name = "demo-target-datastore1" },
                @{ Name = "demo-target-datastore2" },
                @{ Name = "demo-target-datastore3" }
            )
        }
        
        $sampleData | ConvertTo-Json -Depth 3
    }
}
catch {
    Write-ScriptLog "Error occurred: $($_.Exception.Message)" "ERROR"
    Write-ScriptLog "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    
    # Return sample data on error
    $sampleData = @{
        Hosts = @(
            @{ Name = "error-recovery-host-01" },
            @{ Name = "error-recovery-host-02" }
        )
        Datastores = @(
            @{ Name = "error-recovery-ds-01" },
            @{ Name = "error-recovery-ds-02" }
        )
    }
    
    $sampleData | ConvertTo-Json -Depth 3
}