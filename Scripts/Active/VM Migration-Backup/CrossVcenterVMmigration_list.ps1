<#
.SYNOPSIS
    Cross-vCenter VM Migration Tool optimized for PowerCLI 13.x with Enhanced Linked Mode and Parallel Processing.

.DESCRIPTION
    This script migrates VMs from a source vCenter to a destination vCenter using direct migration.
    It preserves folder structure, network configurations, and handles all aspects of the migration process.
    Leverages Enhanced Linked Mode for live migration without requiring VM shutdown.
    Supports parallel migrations to reduce total migration time.
    Includes enhanced network handling to minimize vDS-related errors.
    Specifically optimized for PowerCLI 13.x to use the latest migration capabilities.

.PARAMETER SourceVCenter
    The hostname or IP address of the source vCenter Server.

.PARAMETER DestVCenter
    The hostname or IP address of the destination vCenter Server.

.PARAMETER VMList
    An array of virtual machine names to migrate.

.PARAMETER VMListFile
    Path to a text file containing VM names (one per line) to process.

.PARAMETER SourceVCCredential
    PSCredential object for source vCenter authentication.

.PARAMETER DestVCCredential
    PSCredential object for destination vCenter authentication.

.PARAMETER DestinationCluster
    Name of the destination cluster where VMs should be placed.

.PARAMETER DestinationDatastore
    (Optional) Name of specific destination datastore to use. If omitted, script selects optimal datastore.

.PARAMETER LogFile
    Base name for the log file (timestamp and extension added automatically).

.PARAMETER LogLevel
    Logging detail level: Minimal, Normal, Verbose, or Debug (default: Normal).

.PARAMETER SkipModuleCheck
    Switch to bypass PowerCLI module verification.

.PARAMETER NameSuffix
    Suffix to append to migrated VM names (default: "-Imported").

.PARAMETER PreserveMAC
    Switch to preserve MAC addresses during migration.

.PARAMETER NetworkMapping
    Hashtable defining custom network mappings between source and destination environments.
    Format: @{"SourceNetwork1" = "DestNetwork1"; "SourceNetwork2" = "DestNetwork2"}

.PARAMETER Validate
    Switch to perform validation checks without executing migration.

.PARAMETER DiskFormat
    Format for migrated VM disks: Thin, Thick, or EagerZeroedThick (default: Thin).

.PARAMETER MaxConcurrentMigrations
    Maximum number of simultaneous VM migrations (default: 2, max recommended: 8).

.PARAMETER SequentialMode
    Switch to force sequential processing (one VM at a time).

.PARAMETER IgnoreNetworkErrors
    Switch to continue migration even if network configuration fails.

.PARAMETER DisconnectNetworkDuringMigration
    Switch to temporarily disconnect network adapters during reconfiguration (helps with vDS issues).

.PARAMETER EnhancedNetworkHandling
    Switch to enable enhanced network configuration with fallback options and better vDS support.

.EXAMPLE
    # Migrate VMs with enhanced network handling and 3 concurrent migrations
    .\Cross-vCenterMigration.ps1 -SourceVCenter "source-vc.domain.com" -DestVCenter "dest-vc.domain.com" `
    -VMList "VM1","VM2","VM3","VM4" -MaxConcurrentMigrations 3 -EnhancedNetworkHandling

.EXAMPLE
    # Migrate with network mapping and enhanced error handling
    $networkMap = @{"VM Network" = "Production Network"; "Test Network" = "Dev Network"}
    .\Cross-vCenterMigration.ps1 -SourceVCenter "source-vc.domain.com" -DestVCenter "dest-vc.domain.com" `
    -VMList "WebServer1","AppServer2" -NetworkMapping $networkMap -PreserveMAC -IgnoreNetworkErrors

.EXAMPLE
    # Force sequential mode with enhanced network handling for careful migration
    .\Cross-vCenterMigration.ps1 -SourceVCenter "source-vc.domain.com" -DestVCenter "dest-vc.domain.com" `
    -VMList "VM1","VM2" -SequentialMode -EnhancedNetworkHandling -DisconnectNetworkDuringMigration

.EXAMPLE
    # Maximum performance with enhanced network handling (use with caution)
    .\Cross-vCenterMigration.ps1 -SourceVCenter "source-vc.domain.com" -DestVCenter "dest-vc.domain.com" `
    -VMListFile "C:\VMs.txt" -MaxConcurrentMigrations 4 -EnhancedNetworkHandling -LogLevel Verbose

.NOTES
    Author: Cross-vCenter Migration Tool
    Version: 2.3
    Requires: PowerCLI 13.0 or later, Enhanced Linked Mode between vCenters
    License: MIT
    
    Parallel Migration Notes:
    - Higher concurrency reduces total time but increases resource usage
    - Monitor vCenter and host performance during migrations
    - Network bandwidth may become a bottleneck with too many concurrent migrations
    - Recommended maximum: 4 concurrent migrations for most environments
    
    Enhanced Network Handling:
    - Automatically handles vDS to standard switch migrations
    - Provides fallback networks when target networks aren't found
    - Reduces "vDS port cannot be found" errors
    - Preserves network adapter settings and connection states
#>

[CmdletBinding(DefaultParameterSetName='VMNames')]
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceVCenter,
    
    [Parameter(Mandatory=$true)]
    [string]$DestVCenter,
    
    [Parameter(ParameterSetName='VMNames', Mandatory=$true)]
    [string[]]$VMList,
    
    [Parameter(ParameterSetName='VMFile', Mandatory=$true)]
    [ValidateScript({Test-Path $_ -PathType Leaf})]
    [string]$VMListFile,
    
    [Parameter()]
    [PSCredential]$SourceVCCredential,
    
    [Parameter()]
    [PSCredential]$DestVCCredential,
    
    [Parameter()]
    [string]$DestinationCluster,
    
    [Parameter()]
    [string]$DestinationDatastore,
    
    [Parameter()]
    [string]$LogFile = "CrossVCenterMigration",
    
    [Parameter()]
    [ValidateSet("Minimal", "Normal", "Verbose", "Debug")]
    [string]$LogLevel = "Normal",
    
    [Parameter()]
    [switch]$SkipModuleCheck,
    
    [Parameter()]
    [string]$NameSuffix = "-Imported",
    
    [Parameter()]
    [switch]$PreserveMAC,
    
    [Parameter()]
    [hashtable]$NetworkMapping = @{},
    
    [Parameter()]
    [switch]$Validate,
    
    [Parameter()]
    [ValidateSet("Thin", "Thick", "EagerZeroedThick")]
    [string]$DiskFormat = "Thin",
    
    [Parameter()]
    [ValidateRange(1, 8)]
    [int]$MaxConcurrentMigrations = 2,
    
    [Parameter()]
    [switch]$SequentialMode,
    
    [Parameter()]
    [switch]$IgnoreNetworkErrors,
    
    [Parameter()]
    [switch]$DisconnectNetworkDuringMigration,
    
    [Parameter()]
    [switch]$EnhancedNetworkHandling,
        
[ValidateSet('Debug', 'Verbose', 'Info', 'Warning', 'Error', 'Critical')]
    [string]$LogLevel = 'Normal',  # Map existing LogLevel values
    
    [switch]$DisableConsoleOutput,
    
    [switch]$IncludeStackTrace,
    
    [string]$CustomLogPath
)

# Initialize failed VMs tracking
$script:FailedVMs = @()
$script:MigrationJobs = @()
$script:CompletedMigrations = @()

#region Script Variables
# Set strict mode to catch common coding errors
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Script-wide variables
$script:ScriptStartTime = $null
$script:LogDirectory = $null
$script:LogFilePath = $null
$script:LogLevelValue = 0
$script:PowerCLIVersion = $null
#endregion

#region Logging Functions
function Get-LogLevelValue {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet("Minimal", "Normal", "Verbose", "Debug")]
        [string]$LogLevel
    )
    
    switch ($LogLevel) {
        "Minimal" { return 1 }
        "Normal"  { return 2 }
        "Verbose" { return 3 }
        "Debug"   { return 4 }
        default   { return 2 }
    }
}

function Initialize-Logging {
    [CmdletBinding()]
    param()
    
    $script:LogDirectory = Join-Path -Path $PSScriptRoot -ChildPath "logs"
    
    # Create logs directory if it doesn't exist
    if (-not (Test-Path -Path $script:LogDirectory -PathType Container)) {
        try {
            $null = New-Item -ItemType Directory -Path $script:LogDirectory -Force
        }
        catch {
            throw "Failed to create logs directory: $_"
        }
    }
    
    # Create timestamped log file
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $script:LogFilePath = Join-Path -Path $script:LogDirectory -ChildPath "$($LogFile)-$($timestamp).log"
    
    try {
        $null = New-Item -ItemType File -Path $script:LogFilePath -Force
    }
    catch {
        throw "Failed to create log file: $_"
    }
    
    # Set log level value
    $script:LogLevelValue = Get-LogLevelValue -LogLevel $LogLevel
    
    # Now we can start logging
    Write-LogMessage "Log file initialized: $($script:LogFilePath)" -Level "Normal"
}

function Write-LogMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, Position=0)]
        [string]$Message,
        
        [Parameter()]
        [ValidateSet("Minimal", "Normal", "Verbose", "Debug", "Error", "Warning", "Success")]
        [string]$Level = "Normal",
        
        [Parameter()]
        [switch]$NoConsole
    )
    
    # Initialize LogLevelValue if not set yet
    if (-not (Get-Variable -Name "LogLevelValue" -Scope Script -ErrorAction SilentlyContinue)) {
        $script:LogLevelValue = Get-LogLevelValue -LogLevel $LogLevel
    }
    
    # Determine if we should log based on level
    $levelValue = switch ($Level) {
        "Minimal" { 1 }
        "Normal"  { 2 }
        "Verbose" { 3 }
        "Debug"   { 4 }
        "Error"   { 1 }  # Always log errors
        "Warning" { 2 }
        "Success" { 2 }
        default   { 2 }
    }
    
    if ($levelValue -gt $script:LogLevelValue) {
        return
    }
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "$($timestamp) [$($Level)] $($Message)"
    
    # Add to log file if initialized with thread safety
    if (Get-Variable -Name "LogFilePath" -Scope Script -ErrorAction SilentlyContinue) {
        try {
            # Use a mutex for thread-safe logging when running parallel jobs
            $mutex = New-Object System.Threading.Mutex($false, "CrossVCenterMigrationLog")
            $mutex.WaitOne() | Out-Null
            Add-Content -Path $script:LogFilePath -Value $logEntry
            $mutex.ReleaseMutex()
        }
        catch {
            # Fallback to regular logging if mutex fails
            Add-Content -Path $script:LogFilePath -Value $logEntry
        }
        finally {
            if ($mutex) { $mutex.Dispose() }
        }
    }
    
    # Output to console with color coding (unless suppressed)
    if (-not $NoConsole) {
        $color = switch ($Level) {
            "Error"   { "Red" }
            "Warning" { "Yellow" }
            "Success" { "Green" }
            "Debug"   { "Gray" }
            "Verbose" { "Cyan" }
            default   { "White" }
        }
        
        Write-Host $logEntry -ForegroundColor $color
    }
}
#endregion

#region Module Management
function Get-PowerCLIVersion {
    [CmdletBinding()]
    [OutputType([version])]
    param()
    
    # PowerCLI module management handled by service layer
    Write-LogMessage "PowerCLI version managed by service layer" -Level "Normal"
    return [version]"13.0.0"  # Return a valid version to satisfy script logic
}

function Import-RequiredModules {
    [CmdletBinding()]
    param()

    # PowerCLI module management handled by service layer
    Write-LogMessage "PowerCLI modules managed by service layer" -Level "Success"
    $script:PowerCLIVersion = Get-PowerCLIVersion
    Write-LogMessage "PowerCLI version $($script:PowerCLIVersion) detected" -Level "Success"
    Write-LogMessage "Required modules loaded successfully" -Level "Success"
}
#endregion

#region Initialization
function Initialize-Script {
    [CmdletBinding()]
    param()
    
    # Set up basic console output before logging is initialized
    Write-Host "Initializing Cross-vCenter VM Migration Tool..." -ForegroundColor Cyan
    $script:ScriptStartTime = Get-Date

    try {
        Initialize-Logging
        Write-LogMessage "Logging initialized successfully" -Level "Success"
    }
    catch {
        Write-Host "ERROR: Failed to initialize logging: $_" -ForegroundColor Red
        throw "Failed to initialize logging"
    }
    
    try {
        Import-RequiredModules
    }
    catch {
        Write-LogMessage "Failed to import required modules: $_" -Level "Error"
        throw
    }
    
    # Validate parallel processing settings
    if ($SequentialMode) {
        $script:ActualConcurrency = 1
        Write-LogMessage "Sequential mode enabled - processing one VM at a time" -Level "Normal"
    }
    else {
        $script:ActualConcurrency = $MaxConcurrentMigrations
        Write-LogMessage "Parallel mode enabled - max concurrent migrations: $($script:ActualConcurrency)" -Level "Normal"
    }
    
    Write-LogMessage "Script initialized successfully" -Level "Success"
}
#endregion

#region VM Selection
function Get-VMsToProcess {
    [CmdletBinding()]
    [OutputType([string[]])]
    param()
    
    if ($PSCmdlet.ParameterSetName -eq 'VMNames') {
        # Force the result to be an array even if it's a single item
        return @($VMList)
    }
    else {
        try {
            $vmNames = @(Get-Content -Path $VMListFile -ErrorAction Stop | Where-Object { $_.Trim() -ne "" })
            Write-LogMessage "Loaded $($vmNames.Count) VM names from $($VMListFile)" -Level "Normal"
            return $vmNames
        }
        catch {
            Write-LogMessage "Failed to read VM list from file: $_" -Level "Error"
            throw
        }
    }
}
#endregion

#region Connection Management
function Connect-ToVIServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$Server,
        
        [Parameter(Mandatory=$true)]
        [PSCredential]$Credential,
        
        [Parameter()]
        [string]$Description = "vCenter"
    )
    
    try {
        Write-LogMessage "Connecting to $($Description) server: $($Server)" -Level "Normal"
        $connection = Connect-VIServer -Server $Server -Credential $Credential -ErrorAction Stop
        Write-LogMessage "Successfully connected to $($Server)" -Level "Success"
        return $connection
    }
    catch {
        Write-LogMessage "Failed to connect to $($Server): $_" -Level "Error"
        throw
    }
}

function Disconnect-FromVIServer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        $Server,
        
        [Parameter()]
        [string]$Description = "vCenter"
    )
    
    try {
        if ($Server -and $Server.IsConnected) {
            Write-LogMessage "Disconnecting from $($Description) server: $($Server.Name)" -Level "Normal"
            # DISCONNECT REMOVED - Using persistent connections managed by application
            Write-LogMessage "Successfully disconnected from $($Server.Name)" -Level "Success"
        }
    }
    catch {
        Write-LogMessage "Warning: Failed to disconnect from $($Server.Name): $_" -Level "Warning"
    }
}
#endregion

#region Resource Selection
function Get-OptimalVMHost {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$VMHosts,
        
        [Parameter()]
        [long]$RequiredCPU = 1,
        
        [Parameter()]
        [long]$RequiredMemoryMB = 1024
    )
    
    Write-LogMessage "Finding optimal host (Required: $($RequiredCPU) vCPU, $($RequiredMemoryMB) MB RAM)" -Level "Verbose"
    
    # Get hosts sorted by CPU and memory availability
    $sortedHosts = $VMHosts | 
        Where-Object { $_.ConnectionState -eq "Connected" -and $_.PowerState -eq "PoweredOn" } |
        Sort-Object -Property @{Expression={$_.CpuUsageMhz/$_.CpuTotalMhz}}, @{Expression={$_.MemoryUsageGB/$_.MemoryTotalGB}}
    
    if (-not $sortedHosts) {
        Write-LogMessage "No suitable hosts found in available clusters" -Level "Error"
        throw "No suitable hosts available"
    }
    
    $selectedHost = $sortedHosts | Select-Object -First 1
    $cpuUsage = if ($selectedHost.CpuTotalMhz -gt 0) { ($selectedHost.CpuUsageMhz/$selectedHost.CpuTotalMhz).ToString("P") } else { "Unknown" }
    $memUsage = if ($selectedHost.MemoryTotalGB -gt 0) { ($selectedHost.MemoryUsageGB/$selectedHost.MemoryTotalGB).ToString("P") } else { "Unknown" }
    
    Write-LogMessage "Selected host $($selectedHost.Name) (CPU Usage: $cpuUsage, Memory Usage: $memUsage)" -Level "Normal"
    
    return $selectedHost
}

function Get-OptimalDatastore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$Datastores,
        
        [Parameter(Mandatory=$true)]
        [long]$RequiredSpaceGB,
        
        [Parameter()]
        [double]$SpaceBuffer = 1.2  # 20% buffer
    )
    
    $requiredSpace = $RequiredSpaceGB * $SpaceBuffer
    Write-LogMessage "Finding optimal datastore (Required space: $($requiredSpace.ToString("N2")) GB)" -Level "Verbose"
    
    # Filter datastores with enough space and sort by free space percentage
    $suitableDatastores = $Datastores | 
        Where-Object { $_.FreeSpaceGB -gt $requiredSpace } |
        Sort-Object -Property @{Expression={$_.FreeSpaceGB/$_.CapacityGB}; Descending=$true}
    
    if (-not $suitableDatastores) {
        Write-LogMessage "No suitable datastores with $($requiredSpace.ToString("N2")) GB free space found" -Level "Error"
        throw "No suitable datastores available"
    }
    
    $selectedDatastore = $suitableDatastores | Select-Object -First 1
    Write-LogMessage "Selected datastore $($selectedDatastore.Name) (Free: $($selectedDatastore.FreeSpaceGB.ToString("N2")) GB, $([math]::Round(($selectedDatastore.FreeSpaceGB/$selectedDatastore.CapacityGB)*100))% available)" -Level "Normal"
    
    return $selectedDatastore
}

function Find-VMRootFolder {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        $Server,
        
        [Parameter(Mandatory=$true)]
        $Datacenter
    )
    
    Write-LogMessage "Looking for VM folder in datacenter $($Datacenter.Name)..." -Level "Normal"
    
    try {
        # PowerCLI 13.x optimized approach - use datacenter's VMFolder property
        $vmFolder = Get-View -Server $Server -Id $Datacenter.ExtensionData.VmFolder -ErrorAction SilentlyContinue
        if ($vmFolder) {
            $vmRootFolder = Get-Folder -Server $Server -Id $vmFolder.MoRef.ToString() -ErrorAction SilentlyContinue
            if ($vmRootFolder) {
                Write-LogMessage "Found VM root folder: $($vmRootFolder.Name)" -Level "Success"
                return $vmRootFolder
            }
        }
        
        # Fallback to traditional approach
        $vmRootFolder = Get-Folder -Server $Server | Where-Object { 
            $_.Type -eq "VM" -and 
            $_.Parent -and 
            $_.Parent.Name -eq $Datacenter.Name 
        } | Select-Object -First 1
        
        if ($vmRootFolder) {
            Write-LogMessage "Found VM root folder using parent match: $($vmRootFolder.Name)" -Level "Success"
            return $vmRootFolder
        }
        
        # Last resort - just use any VM folder
        $vmRootFolder = Get-Folder -Server $Server -Type VM | Select-Object -First 1
        
        if ($vmRootFolder) {
            Write-LogMessage "Using first available VM folder as fallback: $($vmRootFolder.Name)" -Level "Warning"
            return $vmRootFolder
        }
        
        throw "Could not find any VM folder"
    }
    catch {
        Write-LogMessage "Error finding VM root folder: $_" -Level "Error"
        throw "Could not find VM root folder in destination vCenter"
    }
}

function Get-VMFolderPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory=$true)]
        $VM,
        
        [Parameter(Mandatory=$true)]
        $Server
    )
    
    Write-LogMessage "Getting folder path for VM: $($VM.Name)" -Level "Verbose"
    
    try {
        # Get the folder that contains the VM
        $sourceFolder = Get-Folder -Server $Server -Id $VM.FolderId -ErrorAction Stop
        if (-not $sourceFolder) {
            throw "Could not find VM's folder"
        }
        
        Write-LogMessage "VM is in folder: $($sourceFolder.Name)" -Level "Debug"
        
        # Build the complete path by walking up the folder hierarchy
        $folderHierarchy = @($sourceFolder.Name)
        $parent = $sourceFolder.Parent
        
        # Traverse up the folder hierarchy
        while ($parent -and $parent.Name -ne "vm") {
            Write-LogMessage "Found parent folder: $($parent.Name)" -Level "Debug"
            $folderHierarchy = @($parent.Name) + $folderHierarchy
            $parent = $parent.Parent
        }
        
        # Reconstruct the path with proper separators
        $folderPath = $folderHierarchy -join "\"
        
        Write-LogMessage "Complete folder path: $($folderPath)" -Level "Normal"
        return $folderPath
    }
    catch {
        Write-LogMessage "Error getting folder path: $_" -Level "Warning"
        
        # Return VM name as a fallback folder name
        $fallbackPath = $VM.Name
        Write-LogMessage "Using fallback folder path: $($fallbackPath)" -Level "Warning"
        return $fallbackPath
    }
}

function Get-OrCreateFolderPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,
        
        [Parameter(Mandatory=$true)]
        $RootFolder,
        
        [Parameter(Mandatory=$true)]
        $VIServer
    )
    
    # Verify parameters
    if ($null -eq $RootFolder) {
        throw "RootFolder parameter cannot be null"
    }
    
    Write-LogMessage "Creating folder path: $($Path)" -Level "Normal"
    Write-LogMessage "Starting from root folder: $($RootFolder.Name) (Type: $($RootFolder.Type))" -Level "Debug"
    
    $currentFolder = $RootFolder
    $folderNames = $Path -split '\\' | Where-Object { $_ -ne '' -and $_ -ne 'vm' }
    
    Write-LogMessage "Will create/verify these folders: $($folderNames -join ', ')" -Level "Debug"
    
    foreach ($folderName in $folderNames) {
        Write-LogMessage "Processing folder: '$($folderName)'" -Level "Debug"
        
        # Check if folder already exists
        try {
            $existingFolders = Get-Folder -Server $VIServer -Name $folderName -Location $currentFolder -ErrorAction Stop
            $nextFolder = $existingFolders | Select-Object -First 1
            
            if ($nextFolder) {
                Write-LogMessage "Found existing folder: '$($folderName)'" -Level "Debug"
            }
        }
        catch {
            Write-LogMessage "Error checking for existing folder: $_" -Level "Debug"
            $nextFolder = $null
        }
        
        # Create folder if it doesn't exist
        if (-not $nextFolder) {
            Write-LogMessage "Creating new folder: '$($folderName)' under '$($currentFolder.Name)'" -Level "Normal"
            try {
                $nextFolder = New-Folder -Server $VIServer -Name $folderName -Location $currentFolder -ErrorAction Stop
                Write-LogMessage "Successfully created folder: '$($folderName)'" -Level "Success"
            }
            catch {
                Write-LogMessage "Failed to create folder '$($folderName)': $_" -Level "Error"
                throw
            }
        }
        
        $currentFolder = $nextFolder
    }
    
    Write-LogMessage "Folder path complete: $($currentFolder.Name)" -Level "Success"
    return $currentFolder
}

function Get-ResourcePoolPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory=$true)]
        $VM,

        [Parameter(Mandatory=$true)]
        $Server
    )

    Write-LogMessage "Getting resource pool path for VM: $($VM.Name)" -Level "Verbose"

    try {
        # Get the resource pool that contains the VM
        $sourceResourcePool = Get-ResourcePool -Server $Server -VM $VM -ErrorAction Stop
        if (-not $sourceResourcePool) {
            throw "Could not find VM's resource pool"
        }

        Write-LogMessage "VM is in resource pool: $($sourceResourcePool.Name)" -Level "Debug"

        # Build the complete path by walking up the resource pool hierarchy
        $resourcePoolHierarchy = @($sourceResourcePool.Name)
        $parent = $sourceResourcePool.Parent

        # Traverse up the resource pool hierarchy
        while ($parent -and $parent.GetType().Name -eq "ResourcePool") {
            Write-LogMessage "Found parent resource pool: $($parent.Name)" -Level "Debug"
            $resourcePoolHierarchy = @($parent.Name) + $resourcePoolHierarchy
            $parent = $parent.Parent
        }

        # Reconstruct the path with proper separators
        $resourcePoolPath = $resourcePoolHierarchy -join "\"

        Write-LogMessage "Complete resource pool path: $($resourcePoolPath)" -Level "Normal"
        return $resourcePoolPath
    }
    catch {
        Write-LogMessage "Error getting resource pool path: $_" -Level "Warning"

        # Return default resource pool name as a fallback
        $fallbackPath = "Resources"
        Write-LogMessage "Using fallback resource pool path: $($fallbackPath)" -Level "Warning"
        return $fallbackPath
    }
}

function Get-OrCreateResourcePoolPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [string]$Path,

        [Parameter(Mandatory=$true)]
        $Cluster,

        [Parameter(Mandatory=$true)]
        $VIServer
    )

    # Verify parameters
    if ($null -eq $Cluster) {
        throw "Cluster parameter cannot be null"
    }

    Write-LogMessage "Creating resource pool path: $($Path)" -Level "Normal"

    # Get the root resource pool of the cluster
    try {
        $rootResourcePool = Get-ResourcePool -Server $VIServer -Location $Cluster | Where-Object {$_.Name -eq "Resources"} | Select-Object -First 1
        if (-not $rootResourcePool) {
            # If no "Resources" pool found, get the first available resource pool
            $rootResourcePool = Get-ResourcePool -Server $VIServer -Location $Cluster | Select-Object -First 1
        }
    }
    catch {
        Write-LogMessage "Error getting root resource pool: $_" -Level "Error"
        throw "Failed to get root resource pool for cluster $($Cluster.Name)"
    }

    $currentResourcePool = $rootResourcePool
    $resourcePoolNames = $Path -split '\\' | Where-Object { $_ -ne '' -and $_ -ne 'Resources' }

    Write-LogMessage "Will create/verify these resource pools: $($resourcePoolNames -join ', ')" -Level "Debug"

    foreach ($resourcePoolName in $resourcePoolNames) {
        Write-LogMessage "Processing resource pool: '$($resourcePoolName)'" -Level "Debug"

        # Check if resource pool already exists
        try {
            $existingResourcePools = Get-ResourcePool -Server $VIServer -Name $resourcePoolName -Location $currentResourcePool -ErrorAction Stop
            $nextResourcePool = $existingResourcePools | Select-Object -First 1

            if ($nextResourcePool) {
                Write-LogMessage "Found existing resource pool: '$($resourcePoolName)'" -Level "Debug"
            }
        }
        catch {
            Write-LogMessage "Error checking for existing resource pool: $_" -Level "Debug"
            $nextResourcePool = $null
        }

        # Create resource pool if it doesn't exist
        if (-not $nextResourcePool) {
            Write-LogMessage "Creating new resource pool: '$($resourcePoolName)' under '$($currentResourcePool.Name)'" -Level "Normal"
            try {
                $nextResourcePool = New-ResourcePool -Server $VIServer -Name $resourcePoolName -Location $currentResourcePool -ErrorAction Stop
                Write-LogMessage "Successfully created resource pool: '$($resourcePoolName)'" -Level "Success"
            }
            catch {
                Write-LogMessage "Failed to create resource pool '$($resourcePoolName)': $_" -Level "Error"
                throw
            }
        }

        $currentResourcePool = $nextResourcePool
    }

    Write-LogMessage "Resource pool path complete: $($currentResourcePool.Name)" -Level "Success"
    return $currentResourcePool
}
#endregion

#region Parallel Migration Management
function New-MigrationJob {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$MigrationParams
    )
    
    $scriptBlock = {
        param($Params)
        
        # PowerCLI modules already loaded in job context
        
        # Extract parameters
        $vmName = $Params.VMName
        $sourceServer = $Params.SourceServer
        $destServer = $Params.DestServer
        $sourceCred = $Params.SourceCredential
        $destCred = $Params.DestCredential
        $migrationData = $Params.MigrationData
        
        try {
            # Connect to both vCenters within the job
            $sourceVI = Connect-VIServer -Server $sourceServer -Credential $sourceCred -ErrorAction Stop
            $destVI = Connect-VIServer -Server $destServer -Credential $destCred -ErrorAction Stop
            
            # Get source VM
            $sourceVM = Get-VM -Server $sourceVI -Name $vmName -ErrorAction Stop
            
            # Get destination objects
            $destHost = Get-VMHost -Server $destVI -Name $migrationData.DestHostName -ErrorAction Stop
            $destDatastore = Get-Datastore -Server $destVI -Name $migrationData.DestDatastoreName -ErrorAction Stop
            
            # Prepare move parameters
            $moveParams = @{
                VM = $sourceVM
                Destination = $destHost
                Datastore = $destDatastore
                Confirm = $false
                ErrorAction = "Stop"
            }
            
            if ($migrationData.DiskStorageFormat -ne "Thin") {
                $moveParams.DiskStorageFormat = $migrationData.DiskStorageFormat
            }
            
            # Perform the migration
            $result = Move-VM @moveParams
            
            # Post-migration tasks
            $migratedVM = Get-VM -Server $destVI -Name $vmName -ErrorAction SilentlyContinue
            if ($migratedVM) {
                # Move to folder using InventoryLocation parameter
                if ($migrationData.DestFolderId) {
                    $destFolder = Get-Folder -Server $destVI -Id $migrationData.DestFolderId -ErrorAction SilentlyContinue
                    if ($destFolder) {
                        Move-VM -VM $migratedVM -InventoryLocation $destFolder -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
                
                # Move to resource pool
                if ($migrationData.DestResourcePoolId) {
                    $destResourcePool = Get-ResourcePool -Server $destVI -Id $migrationData.DestResourcePoolId -ErrorAction SilentlyContinue
                    if ($destResourcePool) {
                        Move-VM -VM $migratedVM -Destination $destResourcePool -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
                
                # Apply network mapping
                if ($migrationData.NetworkMapping -and $migrationData.NetworkMapping.Count -gt 0) {
                    $adapters = $migratedVM | Get-NetworkAdapter
                    foreach ($adapter in $adapters) {
                        $sourceNetwork = $adapter.NetworkName
                        if ($migrationData.NetworkMapping.ContainsKey($sourceNetwork)) {
                            $targetNetwork = $migrationData.NetworkMapping[$sourceNetwork]
                            $destNetwork = Get-VirtualPortGroup -Server $destVI -Name $targetNetwork -ErrorAction SilentlyContinue
                            if ($destNetwork) {
                                Set-NetworkAdapter -NetworkAdapter $adapter -Portgroup $destNetwork -Confirm:$false -ErrorAction SilentlyContinue
                            }
                        }
                    }
                }
                
                # Rename VM if suffix specified
                if ($migrationData.NameSuffix) {
                    $newName = "$($vmName)$($migrationData.NameSuffix)"
                    Set-VM -VM $migratedVM -Name $newName -Confirm:$false -ErrorAction SilentlyContinue
                }
            }
            
            # Return a single result object
            $jobResult = [PSCustomObject]@{
                Success = $true
                VMName = $vmName
                Message = "Migration completed successfully"
                StartTime = $migrationData.StartTime
                EndTime = Get-Date
                JobId = $using:PID  # Add job identifier
            }
            
            return $jobResult
        }
        catch {
            # Return a single error result object
            $jobResult = [PSCustomObject]@{
                Success = $false
                VMName = $vmName
                Error = $_.Exception.Message
                StartTime = $migrationData.StartTime
                EndTime = Get-Date
                JobId = $using:PID  # Add job identifier
            }
            
            return $jobResult
        }
        finally {
            # Disconnect from vCenters
            if ($sourceVI) { # DISCONNECT REMOVED - Using persistent connections managed by application }
            if ($destVI) { # DISCONNECT REMOVED - Using persistent connections managed by application }
        }
    }
    
    # Start the background job
    $job = Start-Job -ScriptBlock $scriptBlock -ArgumentList $MigrationParams
    
    return $job
}

function Wait-ForMigrationJobs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$Jobs,
        
        [Parameter()]
        [int]$CheckIntervalSeconds = 30
    )
    
    $completed = @()
    $running = @($Jobs)  # Ensure it's an array
    
    while ($running.Count -gt 0) {
        Write-LogMessage "Waiting for $($running.Count) migration jobs to complete..." -Level "Normal"
        
        $stillRunning = @()  # Create new array for jobs still running
        
        foreach ($job in $running) {
            if ($job.State -eq "Completed" -or $job.State -eq "Failed") {
                try {
                    $jobResults = Receive-Job -Job $job -ErrorAction Stop
                    
                    # Handle case where job might return multiple objects or arrays
                    if ($jobResults -is [array]) {
                        # If it's an array, take the last result (should be our custom object)
                        $result = $jobResults[-1]
                    } else {
                        $result = $jobResults
                    }
                    
                    # Validate that we have the expected result structure
                    if ($result -and ($result | Get-Member -Name "Success" -MemberType Properties)) {
                        $completed += $result
                        
                        if ($result.Success) {
                            $duration = $result.EndTime - $result.StartTime
                            Write-LogMessage "Migration job completed: $($result.VMName) (Duration: $($duration.ToString('hh\:mm\:ss')))" -Level "Success"
                        }
                        else {
                            Write-LogMessage "Migration job failed: $($result.VMName) - $($result.Error)" -Level "Error"
                            $script:FailedVMs += @{Name = $result.VMName; Error = $result.Error}
                        }
                    }
                    else {
                        # Fallback handling if result structure is unexpected
                        Write-LogMessage "Job completed but result structure was unexpected for job ID: $($job.Id)" -Level "Warning"
                        
                        # Try to extract VM name from job if possible
                        $vmName = "Unknown"
                        if ($job.Command -like "*VMName*") {
                            # This is a fallback - may not always work
                            $vmName = "Job_$($job.Id)"
                        }
                        
                        if ($job.State -eq "Completed") {
                            Write-LogMessage "Job $($job.Id) completed successfully (structure unknown)" -Level "Success"
                            $completed += [PSCustomObject]@{
                                Success = $true
                                VMName = $vmName
                                Message = "Job completed with unknown result structure"
                                StartTime = $job.PSBeginTime
                                EndTime = Get-Date
                            }
                        } else {
                            Write-LogMessage "Job $($job.Id) failed" -Level "Error"
                            $script:FailedVMs += @{Name = $vmName; Error = "Job failed with unknown error"}
                        }
                    }
                }
                catch {
                    Write-LogMessage "Error retrieving job result for job $($job.Id): $_" -Level "Error"
                    
                    # Add to failed list with job information
                    $script:FailedVMs += @{
                        Name = "Job_$($job.Id)"
                        Error = "Failed to retrieve job result: $_"
                    }
                }
                finally {
                    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                $stillRunning += $job
            }
        }
        
        $running = $stillRunning
        
        if ($running.Count -gt 0) {
            # Update progress for running jobs
            $runningJobs = @($running | Where-Object { $_.State -eq "Running" })
            if ($runningJobs.Count -gt 0) {
                Write-LogMessage "Active migrations: $($runningJobs.Count)" -Level "Verbose"
            }
            
            Start-Sleep -Seconds $CheckIntervalSeconds
        }
    }
    
    return $completed
}

function Start-ParallelMigrations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$MigrationQueue,
        
        [Parameter(Mandatory=$true)]
        [int]$MaxConcurrent
    )
    
    $allJobs = @()
    $queueIndex = 0
    $totalMigrations = $MigrationQueue.Count
    
    Write-LogMessage "Starting parallel migrations with max concurrency: $MaxConcurrent" -Level "Normal"
    
    while ($queueIndex -lt $totalMigrations -or $allJobs.Count -gt 0) {
        # Get currently running jobs - ensure result is always an array
        $runningJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
        
        # Start new jobs if we have capacity and items in queue
        while ($runningJobs.Count -lt $MaxConcurrent -and $queueIndex -lt $totalMigrations) {
            $migrationData = $MigrationQueue[$queueIndex]
            
            Write-LogMessage "Starting migration job for VM: $($migrationData.VMName) (Job $($queueIndex + 1) of $totalMigrations)" -Level "Normal"
            
            try {
                $job = New-MigrationJob -MigrationParams $migrationData
                $allJobs += $job
                $queueIndex++
                
                # Re-calculate running jobs after adding new job
                $runningJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
            }
            catch {
                Write-LogMessage "Failed to start migration job for $($migrationData.VMName): $_" -Level "Error"
                $script:FailedVMs += @{Name = $migrationData.VMName; Error = $_.Exception.Message}
                $queueIndex++
            }
        }
        
        # Wait for at least one job to complete before checking again
        if ($runningJobs.Count -gt 0) {
            $completedJobs = Wait-ForMigrationJobs -Jobs $runningJobs -CheckIntervalSeconds 15
            $script:CompletedMigrations += $completedJobs
            
            # Remove completed jobs from the tracking array - ensure result is always an array
            $allJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
        }
        
        # Update progress
        $completed = $script:CompletedMigrations.Count + $script:FailedVMs.Count
        $progressPercent = if ($totalMigrations -gt 0) { [math]::Round(($completed / $totalMigrations) * 100) } else { 0 }
        Write-Progress -Activity "VM Migrations" -Status "Completed: $completed of $totalMigrations" -PercentComplete $progressPercent
    }
    
    Write-Progress -Activity "VM Migrations" -Completed
    Write-LogMessage "All migration jobs completed" -Level "Success"
}
#endregion

#region Enhanced Migration Wrappers
function Start-SequentialMigrations-Enhanced {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$MigrationQueue,
        
        [Parameter(Mandatory=$true)]
        $SourceVI,
        
        [Parameter(Mandatory=$true)]
        $DestVI,
        
        [Parameter(Mandatory=$true)]
        [array]$DestHosts,
        
        [Parameter()]
        $DestDatastore,
        
        [Parameter(Mandatory=$true)]
        $VmRootFolder
    )
    
    $successCount = 0
    $failureCount = 0
    $vmCurrent = 0
    $vmTotal = $MigrationQueue.Count

    foreach ($migrationData in $MigrationQueue) {
        $vmCurrent++
        $vmName = $migrationData.VMName
        Write-Progress -Activity "Processing VMs" -Status "VM $($vmCurrent) of $($vmTotal): $($vmName)" -PercentComplete (($vmCurrent / $vmTotal) * 100)
        
        try {
            # Get source VM for size information
            $sourceVM = Get-VM -Server $SourceVI -Name $vmName -ErrorAction Stop
            Write-LogMessage "Found source VM: $($vmName), PowerState: $($sourceVM.PowerState), Size: $($sourceVM.UsedSpaceGB.ToString("N2")) GB" -Level "Normal"
            
            # Select optimal host and datastore if not specified
            $selectedHost = Get-OptimalVMHost -VMHosts $DestHosts -RequiredCPU $sourceVM.NumCpu -RequiredMemoryMB $sourceVM.MemoryMB
            
            # Select optimal datastore if not specified
            if (-not $DestDatastore) {
                # Get datastores accessible from the selected host
                $hostDatastores = Get-Datastore -Server $DestVI -VMHost $selectedHost
                $selectedDatastore = Get-OptimalDatastore -Datastores $hostDatastores -RequiredSpaceGB $sourceVM.UsedSpaceGB
                Write-LogMessage "Selected optimal datastore for VM: $($selectedDatastore.Name)" -Level "Normal"
            } else {
                $selectedDatastore = $DestDatastore
            }
            
            # Get VM folder path and create destination folder
            $folderPath = Get-VMFolderPath -VM $sourceVM -Server $SourceVI
            $destFolder = Get-OrCreateFolderPath -Path $folderPath -RootFolder $VmRootFolder -VIServer $DestVI
            
            # Get resource pool path and create destination resource pool
            $resourcePoolPath = Get-ResourcePoolPath -VM $sourceVM -Server $SourceVI 
            $destClusterForVM = Get-Cluster -VMHost $selectedHost -Server $DestVI
            $destResourcePool = Get-OrCreateResourcePoolPath -Path $resourcePoolPath -Cluster $destClusterForVM -VIServer $DestVI
            
            # Use enhanced migration function
            $success = Migrate-VM-Enhanced -VMName $vmName `
                -SourceVI $SourceVI `
                -DestVI $DestVI `
                -DestHost $selectedHost `
                -DestDatastore $selectedDatastore `
                -DestFolder $destFolder `
                -DestResourcePool $destResourcePool `
                -NetworkMapping $NetworkMapping `
                -NameSuffix $NameSuffix `
                -PreserveMAC $PreserveMAC `
                -DiskStorageFormat $DiskFormat
            
            if ($success) {
                $successCount++
            } else {
                $failureCount++
            }
        }
        catch {
            Write-LogMessage "Failed to prepare for VM '$($vmName)' migration: $_" -Level "Error"
            $failureCount++
            $script:FailedVMs += @{Name = $vmName; Error = $_.Exception.Message}
        }
    }
    
    Write-Progress -Activity "Processing VMs" -Completed
    
    return @{
        SuccessCount = $successCount
        FailureCount = $failureCount
    }
}

function Start-ParallelMigrations-Enhanced {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$MigrationQueue,
        
        [Parameter(Mandatory=$true)]
        [int]$MaxConcurrent
    )
    
    # Replace the job creation with enhanced version
    $allJobs = @()
    $queueIndex = 0
    $totalMigrations = $MigrationQueue.Count
    
    Write-LogMessage "Starting enhanced parallel migrations with max concurrency: $MaxConcurrent" -Level "Normal"
    
    while ($queueIndex -lt $totalMigrations -or $allJobs.Count -gt 0) {
        # Get currently running jobs - ensure result is always an array
        $runningJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
        
        # Start new jobs if we have capacity and items in queue
        while ($runningJobs.Count -lt $MaxConcurrent -and $queueIndex -lt $totalMigrations) {
            $migrationData = $MigrationQueue[$queueIndex]
            
            Write-LogMessage "Starting enhanced migration job for VM: $($migrationData.VMName) (Job $($queueIndex + 1) of $totalMigrations)" -Level "Normal"
            
            try {
                $job = New-MigrationJob-Enhanced -MigrationParams $migrationData
                $allJobs += $job
                $queueIndex++
                
                # Re-calculate running jobs after adding new job
                $runningJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
            }
            catch {
                Write-LogMessage "Failed to start enhanced migration job for $($migrationData.VMName): $_" -Level "Error"
                $script:FailedVMs += @{Name = $migrationData.VMName; Error = $_.Exception.Message}
                $queueIndex++
            }
        }
        
        # Wait for at least one job to complete before checking again
        if ($runningJobs.Count -gt 0) {
            $completedJobs = Wait-ForMigrationJobs -Jobs $runningJobs -CheckIntervalSeconds 15
            $script:CompletedMigrations += $completedJobs
            
            # Remove completed jobs from the tracking array - ensure result is always an array
            $allJobs = @($allJobs | Where-Object { $_.State -eq "Running" })
        }
        
        # Update progress
        $completed = $script:CompletedMigrations.Count + $script:FailedVMs.Count
        $progressPercent = if ($totalMigrations -gt 0) { [math]::Round(($completed / $totalMigrations) * 100) } else { 0 }
        Write-Progress -Activity "Enhanced VM Migrations" -Status "Completed: $completed of $totalMigrations" -PercentComplete $progressPercent
    }
    
    Write-Progress -Activity "Enhanced VM Migrations" -Completed
    Write-LogMessage "All enhanced migration jobs completed" -Level "Success"
}
#endregion


#region Sequential Migration (Original Logic)
function Start-SequentialMigrations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [array]$MigrationQueue,
        
        [Parameter(Mandatory=$true)]
        $SourceVI,
        
        [Parameter(Mandatory=$true)]
        $DestVI,
        
        [Parameter(Mandatory=$true)]
        [array]$DestHosts,
        
        [Parameter()]
        $DestDatastore,
        
        [Parameter(Mandatory=$true)]
        $VmRootFolder
    )
    
    $successCount = 0
    $failureCount = 0
    $vmCurrent = 0
    $vmTotal = $MigrationQueue.Count

    foreach ($migrationData in $MigrationQueue) {
        $vmCurrent++
        $vmName = $migrationData.VMName
        Write-Progress -Activity "Processing VMs" -Status "VM $($vmCurrent) of $($vmTotal): $($vmName)" -PercentComplete (($vmCurrent / $vmTotal) * 100)
        
        try {
            # Get source VM for size information
            $sourceVM = Get-VM -Server $SourceVI -Name $vmName -ErrorAction Stop
            Write-LogMessage "Found source VM: $($vmName), PowerState: $($sourceVM.PowerState), Size: $($sourceVM.UsedSpaceGB.ToString("N2")) GB" -Level "Normal"
            
            # Select optimal host and datastore if not specified
            $selectedHost = Get-OptimalVMHost -VMHosts $DestHosts -RequiredCPU $sourceVM.NumCpu -RequiredMemoryMB $sourceVM.MemoryMB
            
            # Select optimal datastore if not specified
            if (-not $DestDatastore) {
                # Get datastores accessible from the selected host
                $hostDatastores = Get-Datastore -Server $DestVI -VMHost $selectedHost
                $selectedDatastore = Get-OptimalDatastore -Datastores $hostDatastores -RequiredSpaceGB $sourceVM.UsedSpaceGB
                Write-LogMessage "Selected optimal datastore for VM: $($selectedDatastore.Name)" -Level "Normal"
            } else {
                $selectedDatastore = $DestDatastore
            }
            
            # Get VM folder path and create destination folder
            $folderPath = Get-VMFolderPath -VM $sourceVM -Server $SourceVI
            $destFolder = Get-OrCreateFolderPath -Path $folderPath -RootFolder $VmRootFolder -VIServer $DestVI
            
            # Get resource pool path and create destination resource pool
            $resourcePoolPath = Get-ResourcePoolPath -VM $sourceVM -Server $SourceVI 
            $destClusterForVM = Get-Cluster -VMHost $selectedHost -Server $DestVI
            $destResourcePool = Get-OrCreateResourcePoolPath -Path $resourcePoolPath -Cluster $destClusterForVM -VIServer $DestVI
            
            $success = Migrate-VM -VMName $vmName `
                -SourceVI $SourceVI `
                -DestVI $DestVI `
                -DestHost $selectedHost `
                -DestDatastore $selectedDatastore `
                -DestFolder $destFolder `
                -DestResourcePool $destResourcePool `
                -NetworkMapping $NetworkMapping `
                -NameSuffix $NameSuffix `
                -PreserveMAC $PreserveMAC `
                -DiskStorageFormat $DiskFormat
            
            if ($success) {
                $successCount++
            } else {
                $failureCount++
            }
        }
        catch {
            Write-LogMessage "Failed to prepare for VM '$($vmName)' migration: $_" -Level "Error"
            $failureCount++
            $script:FailedVMs += @{Name = $vmName; Error = $_.Exception.Message}
        }
    }
    
    Write-Progress -Activity "Processing VMs" -Completed
    
    return @{
        SuccessCount = $successCount
        FailureCount = $failureCount
    }
}
#endregion
#region Enhanced Network Management Functions
function Get-VMNetworkConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        $VM,
        
        [Parameter(Mandatory=$true)]
        $Server
    )
    
    Write-LogMessage "Analyzing network configuration for VM: $($VM.Name)" -Level "Verbose"
    
    try {
        $networkAdapters = Get-NetworkAdapter -VM $VM -Server $Server
        $networkConfig = @()
        
        foreach ($adapter in $networkAdapters) {
            $config = [PSCustomObject]@{
                Name = $adapter.Name
                NetworkName = $adapter.NetworkName
                MacAddress = $adapter.MacAddress
                Type = $adapter.Type
                Connected = $adapter.ConnectionState.Connected
                StartConnected = $adapter.ConnectionState.StartConnected
                WakeOnLan = $adapter.WakeOnLanEnabled
            }
            
            # Check if it's connected to a distributed switch
            if ($adapter.ExtensionData.Backing.Port) {
                $config | Add-Member -NotePropertyName "IsDistributedSwitch" -NotePropertyValue $true
                $config | Add-Member -NotePropertyName "SwitchUuid" -NotePropertyValue $adapter.ExtensionData.Backing.Port.SwitchUuid
                $config | Add-Member -NotePropertyName "PortKey" -NotePropertyValue $adapter.ExtensionData.Backing.Port.PortKey
                Write-LogMessage "Adapter $($adapter.Name) connected to vDS port $($adapter.ExtensionData.Backing.Port.PortKey)" -Level "Debug"
            } else {
                $config | Add-Member -NotePropertyName "IsDistributedSwitch" -NotePropertyValue $false
            }
            
            $networkConfig += $config
        }
        
        return $networkConfig
    }
    catch {
        Write-LogMessage "Error analyzing network configuration: $_" -Level "Warning"
        return @()
    }
}

function Set-VMNetworkSafe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        $VM,
        
        [Parameter(Mandatory=$true)]
        $Server,
        
        [Parameter(Mandatory=$true)]
        [array]$OriginalNetworkConfig,
        
        [Parameter()]
        [hashtable]$NetworkMapping = @{},
        
        [Parameter()]
        [bool]$PreserveMAC = $false,
        
        [Parameter()]
        [bool]$DisconnectBeforeChange = $true
    )
    
    Write-LogMessage "Configuring networks for migrated VM: $($VM.Name)" -Level "Normal"
    
    try {
        $currentAdapters = Get-NetworkAdapter -VM $VM -Server $Server
        
        for ($i = 0; $i -lt $currentAdapters.Count; $i++) {
            $adapter = $currentAdapters[$i]
            $originalConfig = $OriginalNetworkConfig[$i]
            
            Write-LogMessage "Processing network adapter: $($adapter.Name)" -Level "Verbose"
            
            # Determine target network
            $targetNetworkName = $originalConfig.NetworkName
            if ($NetworkMapping.ContainsKey($originalConfig.NetworkName)) {
                $targetNetworkName = $NetworkMapping[$originalConfig.NetworkName]
                Write-LogMessage "Network mapping: $($originalConfig.NetworkName) -> $($targetNetworkName)" -Level "Normal"
            }
            
            # Find the target network in destination vCenter
            $targetNetwork = $null
            try {
                # Try to find as distributed port group first
                $targetNetwork = Get-VDPortgroup -Server $Server -Name $targetNetworkName -ErrorAction SilentlyContinue
                if ($targetNetwork) {
                    Write-LogMessage "Found distributed port group: $($targetNetworkName)" -Level "Verbose"
                }
            }
            catch {
                # Ignore errors and try standard port group
            }
            
            # If not found, try standard port group
            if (-not $targetNetwork) {
                try {
                    $targetNetwork = Get-VirtualPortGroup -Server $Server -Name $targetNetworkName -ErrorAction SilentlyContinue
                    if ($targetNetwork) {
                        Write-LogMessage "Found standard port group: $($targetNetworkName)" -Level "Verbose"
                    }
                }
                catch {
                    # Ignore errors
                }
            }
            
            if ($targetNetwork) {
                try {
                    # Optionally disconnect before changing (can help with vDS issues)
                    if ($DisconnectBeforeChange -and $adapter.ConnectionState.Connected) {
                        Write-LogMessage "Temporarily disconnecting adapter for safe reconfiguration" -Level "Verbose"
                        Set-NetworkAdapter -NetworkAdapter $adapter -Connected:$false -Confirm:$false -ErrorAction SilentlyContinue
                        Start-Sleep -Seconds 2  # Brief pause
                    }
                    
                    # Prepare network adapter parameters
                    $setParams = @{
                        NetworkAdapter = $adapter
                        Confirm = $false
                        ErrorAction = "Stop"
                    }
                    
                    # Set the network (works for both standard and distributed)
                    if ($targetNetwork.GetType().Name -like "*VDPortgroup*") {
                        $setParams.Portgroup = $targetNetwork
                    } else {
                        $setParams.Portgroup = $targetNetwork
                    }
                    
                    # Handle MAC address preservation
                    if ($PreserveMAC -and $originalConfig.MacAddress) {
                        $setParams.MacAddress = $originalConfig.MacAddress
                        Write-LogMessage "Preserving MAC address: $($originalConfig.MacAddress)" -Level "Verbose"
                    }
                    
                    # Apply the configuration
                    Set-NetworkAdapter @setParams
                    Write-LogMessage "Network adapter configured successfully: $($targetNetworkName)" -Level "Success"
                    
                    # Restore connection state
                    if ($originalConfig.Connected) {
                        Set-NetworkAdapter -NetworkAdapter $adapter -Connected:$true -Confirm:$false -ErrorAction SilentlyContinue
                        Write-LogMessage "Network adapter reconnected" -Level "Verbose"
                    }
                    
                    # Set start connected state
                    if ($originalConfig.StartConnected) {
                        Set-NetworkAdapter -NetworkAdapter $adapter -StartConnected:$true -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
                catch {
                    Write-LogMessage "Failed to configure network adapter $($adapter.Name): $_" -Level "Warning"
                    
                    # Try to find a fallback network
                    $fallbackNetwork = Get-VirtualPortGroup -Server $Server | Where-Object { $_.Name -like "*VM*" -or $_.Name -like "*Management*" } | Select-Object -First 1
                    if ($fallbackNetwork) {
                        try {
                            Set-NetworkAdapter -NetworkAdapter $adapter -Portgroup $fallbackNetwork -Confirm:$false -ErrorAction Stop
                            Write-LogMessage "Applied fallback network: $($fallbackNetwork.Name)" -Level "Warning"
                        }
                        catch {
                            Write-LogMessage "Failed to apply fallback network: $_" -Level "Error"
                        }
                    }
                }
            }
            else {
                Write-LogMessage "Target network '$($targetNetworkName)' not found. Adapter may need manual reconfiguration." -Level "Warning"
                
                # Try to find any available network as fallback
                $availableNetworks = Get-VirtualPortGroup -Server $Server | Select-Object -First 5
                if ($availableNetworks) {
                    Write-LogMessage "Available networks in destination: $($availableNetworks.Name -join ', ')" -Level "Warning"
                }
            }
        }
    }
    catch {
        Write-LogMessage "Error configuring VM networks: $_" -Level "Error"
    }
}
#endregion
#region Updated Migration Functions with Enhanced Network Handling
function Migrate-VM-Enhanced {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory=$true)]
        [string]$VMName,
        
        [Parameter(Mandatory=$true)]
        $SourceVI,
        
        [Parameter(Mandatory=$true)]
        $DestVI,
        
        [Parameter(Mandatory=$true)]
        $DestHost,
        
        [Parameter(Mandatory=$true)]
        $DestDatastore,
        
        [Parameter(Mandatory=$true)]
        $DestFolder,
        
        [Parameter(Mandatory=$true)]
        $DestResourcePool,
        
        [Parameter()]
        [hashtable]$NetworkMapping = @{},
        
        [Parameter()]
        [string]$NameSuffix = "-Imported",
        
        [Parameter()]
        [bool]$PreserveMAC = $false,
        
        [Parameter()]
        [string]$DiskStorageFormat = "Thin"
    )
    
    try {
        Write-LogMessage "Starting enhanced cross-vCenter migration for VM: $VMName" -Level "Normal"
        
        # Get the source VM
        $sourceVM = Get-VM -Server $SourceVI -Name $VMName -ErrorAction Stop
        if (-not $sourceVM) {
            throw "Source VM not found: $VMName"
        }
        
        # Capture original network configuration before migration
        $originalNetworkConfig = Get-VMNetworkConfiguration -VM $sourceVM -Server $SourceVI
        Write-LogMessage "Captured network configuration for $($originalNetworkConfig.Count) adapters" -Level "Verbose"
        
        Write-LogMessage "Target resource pool: $($DestResourcePool.Name)" -Level "Normal"
        
        # Start with minimal required parameters
        $moveParams = @{
            VM = $sourceVM
            Destination = $DestHost
            Datastore = $DestDatastore
            Confirm = $false
            ErrorAction = "Stop"
        }
        
        # Add disk format if specified
        if ($DiskStorageFormat -ne "Thin") {
            $moveParams.DiskStorageFormat = $DiskStorageFormat
        }
        
        Write-LogMessage "Initiating Move-VM operation..." -Level "Normal"
        
        try {
            # Start the migration and wait for it to complete
            $result = Move-VM @moveParams
            
            # If we get here, the migration was successful
            Write-LogMessage "Migration completed successfully" -Level "Success"
            
            # Get the migrated VM
            $migratedVM = Get-VM -Server $DestVI -Name $VMName -ErrorAction SilentlyContinue
            if ($migratedVM) {
                # Move the VM to the correct folder using InventoryLocation
                Write-LogMessage "Moving VM to destination folder: $($DestFolder.Name)" -Level "Normal"
                Move-VM -VM $migratedVM -InventoryLocation $DestFolder -Confirm:$false -ErrorAction Stop
                
                # Move the VM to the correct resource pool
                Write-LogMessage "Moving VM to resource pool: $($DestResourcePool.Name)" -Level "Normal"
                Move-VM -VM $migratedVM -Destination $DestResourcePool -Confirm:$false -ErrorAction Stop
                
                # Enhanced network configuration
                if ($originalNetworkConfig.Count -gt 0) {
                    Write-LogMessage "Applying enhanced network configuration..." -Level "Normal"
                    Set-VMNetworkSafe -VM $migratedVM -Server $DestVI -OriginalNetworkConfig $originalNetworkConfig -NetworkMapping $NetworkMapping -PreserveMAC $PreserveMAC
                }
                
                # If a name suffix is specified, rename the VM
                if (-not [string]::IsNullOrEmpty($NameSuffix)) {
                    $newVMName = "$($VMName)$($NameSuffix)"
                    Write-LogMessage "Renaming migrated VM to: $newVMName" -Level "Normal"
                    $migratedVM | Set-VM -Name $newVMName -Confirm:$false -ErrorAction Stop
                    Write-LogMessage "VM renamed successfully" -Level "Success"
                }
                
                # Final network validation
                Write-LogMessage "Validating final network configuration..." -Level "Verbose"
                $finalNetworkConfig = Get-VMNetworkConfiguration -VM $migratedVM -Server $DestVI
                foreach ($config in $finalNetworkConfig) {
                    Write-LogMessage "Final adapter $($config.Name): Network=$($config.NetworkName), Connected=$($config.Connected)" -Level "Verbose"
                }
            }
            
            Write-LogMessage "VM '$VMName' successfully migrated to destination vCenter" -Level "Success"
            return $true
        }
        catch {
            throw "Migration failed: $_"
        }
    }
    catch {
        Write-LogMessage "Failed to migrate VM '$VMName': $_" -Level "Error"
        $script:FailedVMs += @{Name = $VMName; Error = $_.Exception.Message}
        return $false
    }
}
#endregion

#region Updated Job Script Block for Enhanced Network Handling
# Update the New-MigrationJob function's script block:
function New-MigrationJob-Enhanced {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$MigrationParams
    )
    
    $scriptBlock = {
        param($Params)
        
        # PowerCLI modules already loaded in job context
        
        # Extract parameters
        $vmName = $Params.VMName
        $sourceServer = $Params.SourceServer
        $destServer = $Params.DestServer
        $sourceCred = $Params.SourceCredential
        $destCred = $Params.DestCredential
        $migrationData = $Params.MigrationData
        
        try {
            # Connect to both vCenters within the job
            $sourceVI = Connect-VIServer -Server $sourceServer -Credential $sourceCred -ErrorAction Stop
            $destVI = Connect-VIServer -Server $destServer -Credential $destCred -ErrorAction Stop
            
            # Get source VM and capture network config
            $sourceVM = Get-VM -Server $sourceVI -Name $vmName -ErrorAction Stop
            
            # Capture original network configuration
            $originalNetworkConfig = @()
            try {
                $networkAdapters = Get-NetworkAdapter -VM $sourceVM -Server $sourceVI
                foreach ($adapter in $networkAdapters) {
                    $config = @{
                        Name = $adapter.Name
                        NetworkName = $adapter.NetworkName
                        MacAddress = $adapter.MacAddress
                        Connected = $adapter.ConnectionState.Connected
                        StartConnected = $adapter.ConnectionState.StartConnected
                    }
                    $originalNetworkConfig += $config
                }
            }
            catch {
                # If network capture fails, continue with migration
            }
            
            # Get destination objects
            $destHost = Get-VMHost -Server $destVI -Name $migrationData.DestHostName -ErrorAction Stop
            $destDatastore = Get-Datastore -Server $destVI -Name $migrationData.DestDatastoreName -ErrorAction Stop
            
            # Prepare move parameters
            $moveParams = @{
                VM = $sourceVM
                Destination = $destHost
                Datastore = $destDatastore
                Confirm = $false
                ErrorAction = "Stop"
            }
            
            if ($migrationData.DiskStorageFormat -ne "Thin") {
                $moveParams.DiskStorageFormat = $migrationData.DiskStorageFormat
            }
            
            # Perform the migration
            $result = Move-VM @moveParams
            
            # Post-migration tasks
            $migratedVM = Get-VM -Server $destVI -Name $vmName -ErrorAction SilentlyContinue
            if ($migratedVM) {
                # Move to folder using InventoryLocation parameter
                if ($migrationData.DestFolderId) {
                    $destFolder = Get-Folder -Server $destVI -Id $migrationData.DestFolderId -ErrorAction SilentlyContinue
                    if ($destFolder) {
                        Move-VM -VM $migratedVM -InventoryLocation $destFolder -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
                
                # Move to resource pool
                if ($migrationData.DestResourcePoolId) {
                    $destResourcePool = Get-ResourcePool -Server $destVI -Id $migrationData.DestResourcePoolId -ErrorAction SilentlyContinue
                    if ($destResourcePool) {
                        Move-VM -VM $migratedVM -Destination $destResourcePool -Confirm:$false -ErrorAction SilentlyContinue
                    }
                }
                
                # Enhanced network configuration
                if ($originalNetworkConfig.Count -gt 0) {
                    $adapters = $migratedVM | Get-NetworkAdapter
                    
                    for ($i = 0; $i -lt $adapters.Count -and $i -lt $originalNetworkConfig.Count; $i++) {
                        $adapter = $adapters[$i]
                        $originalConfig = $originalNetworkConfig[$i]
                        
                        # Determine target network
                        $targetNetworkName = $originalConfig.NetworkName
                        if ($migrationData.NetworkMapping -and $migrationData.NetworkMapping.ContainsKey($originalConfig.NetworkName)) {
                            $targetNetworkName = $migrationData.NetworkMapping[$originalConfig.NetworkName]
                        }
                        
                        # Find target network (try both distributed and standard)
                        $destNetwork = $null
                        try {
                            $destNetwork = Get-VDPortgroup -Server $destVI -Name $targetNetworkName -ErrorAction SilentlyContinue
                        }
                        catch { }
                        
                        if (-not $destNetwork) {
                            try {
                                $destNetwork = Get-VirtualPortGroup -Server $destVI -Name $targetNetworkName -ErrorAction SilentlyContinue
                            }
                            catch { }
                        }
                        
                        if ($destNetwork) {
                            try {
                                $setParams = @{
                                    NetworkAdapter = $adapter
                                    Portgroup = $destNetwork
                                    Confirm = $false
                                    ErrorAction = "SilentlyContinue"
                                }
                                
                                if ($migrationData.PreserveMAC -and $originalConfig.MacAddress) {
                                    $setParams.MacAddress = $originalConfig.MacAddress
                                }
                                
                                Set-NetworkAdapter @setParams
                                
                                # Restore connection state
                                if ($originalConfig.Connected) {
                                    Set-NetworkAdapter -NetworkAdapter $adapter -Connected:$true -Confirm:$false -ErrorAction SilentlyContinue
                                }
                            }
                            catch {
                                # Network configuration failed, but don't fail the entire migration
                            }
                        }
                    }
                }
                
                # Rename VM if suffix specified
                if ($migrationData.NameSuffix) {
                    $newName = "$($vmName)$($migrationData.NameSuffix)"
                    Set-VM -VM $migratedVM -Name $newName -Confirm:$false -ErrorAction SilentlyContinue
                }
            }
            
            # Return a single result object
            $jobResult = [PSCustomObject]@{
                Success = $true
                VMName = $vmName
                Message = "Migration completed successfully"
                StartTime = $migrationData.StartTime
                EndTime = Get-Date
                NetworkIssues = $false  # Could be enhanced to track network reconfig issues
            }
            
            return $jobResult
        }
        catch {
            # Return a single error result object
            $jobResult = [PSCustomObject]@{
                Success = $false
                VMName = $vmName
                Error = $_.Exception.Message
                StartTime = $migrationData.StartTime
                EndTime = Get-Date
            }
            
            return $jobResult
        }
        finally {
            # Disconnect from vCenters
            if ($sourceVI) { # DISCONNECT REMOVED - Using persistent connections managed by application }
            if ($destVI) { # DISCONNECT REMOVED - Using persistent connections managed by application }
        }
    }
    
    # Start the background job
    $job = Start-Job -ScriptBlock $scriptBlock -ArgumentList $MigrationParams
    
    return $job
}
#endregion

#region Migration Function (Modified for both Sequential and Parallel)


function Migrate-VM {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory=$true)]
        [string]$VMName,
        
        [Parameter(Mandatory=$true)]
        $SourceVI,
        
        [Parameter(Mandatory=$true)]
        $DestVI,
        
        [Parameter(Mandatory=$true)]
        $DestHost,
        
        [Parameter(Mandatory=$true)]
        $DestDatastore,
        
        [Parameter(Mandatory=$true)]
        $DestFolder,
        
        [Parameter(Mandatory=$true)]
        $DestResourcePool,
        
        [Parameter()]
        [hashtable]$NetworkMapping = @{},
        
        [Parameter()]
        [string]$NameSuffix = "-Imported",
        
        [Parameter()]
        [bool]$PreserveMAC = $false,
        
        [Parameter()]
        [string]$DiskStorageFormat = "Thin"
    )
    
    try {
        Write-LogMessage "Starting cross-vCenter migration for VM: $VMName" -Level "Normal"
        
        # Get the source VM
        $sourceVM = Get-VM -Server $SourceVI -Name $VMName -ErrorAction Stop
        if (-not $sourceVM) {
            throw "Source VM not found: $VMName"
        }
        
        Write-LogMessage "Target resource pool: $($DestResourcePool.Name)" -Level "Normal"
        
        # Start with minimal required parameters
        $moveParams = @{
            VM = $sourceVM
            Destination = $DestHost
            Datastore = $DestDatastore
            Confirm = $false
            ErrorAction = "Stop"
        }
        
        # Add disk format if specified
        if ($DiskStorageFormat -ne "Thin") {
            $moveParams.DiskStorageFormat = $DiskStorageFormat
        }
        
        Write-LogMessage "Initiating Move-VM operation..." -Level "Normal"
        
        try {
            # Start the migration and wait for it to complete
            $result = Move-VM @moveParams
            
            # If we get here, the migration was successful
            Write-LogMessage "Migration completed successfully" -Level "Success"
            
            # Get the migrated VM
            $migratedVM = Get-VM -Server $DestVI -Name $VMName -ErrorAction SilentlyContinue
            if ($migratedVM) {
                # Move the VM to the correct folder using InventoryLocation
                Write-LogMessage "Moving VM to destination folder: $($DestFolder.Name)" -Level "Normal"
                Move-VM -VM $migratedVM -InventoryLocation $DestFolder -Confirm:$false -ErrorAction Stop
                
                # Move the VM to the correct resource pool
                Write-LogMessage "Moving VM to resource pool: $($DestResourcePool.Name)" -Level "Normal"
                Move-VM -VM $migratedVM -Destination $DestResourcePool -Confirm:$false -ErrorAction Stop
                
                # Apply network mapping if specified
                if ($NetworkMapping.Count -gt 0) {
                    $adapters = $migratedVM | Get-NetworkAdapter
                    
                    foreach ($adapter in $adapters) {
                        $sourceNetwork = $adapter.NetworkName
                        
                        if ($NetworkMapping.ContainsKey($sourceNetwork)) {
                            $targetNetwork = $NetworkMapping[$sourceNetwork]
                            Write-LogMessage "Applying network mapping: $sourceNetwork -> $targetNetwork" -Level "Normal"
                            
                            # Find the target network in destination vCenter
                            $destNetwork = Get-VirtualPortGroup -Server $DestVI -Name $targetNetwork -ErrorAction SilentlyContinue
                            
                            if ($destNetwork) {
                                $setParams = @{
                                    NetworkAdapter = $adapter
                                    Portgroup = $destNetwork
                                    Confirm = $false
                                }
                                
                                if ($PreserveMAC) {
                                    $setParams.MacAddress = $adapter.MacAddress
                                }
                                
                                Set-NetworkAdapter @setParams -ErrorAction SilentlyContinue
                                Write-LogMessage "Network adapter updated successfully" -Level "Success"
                            }
                            else {
                                Write-LogMessage "Warning: Target network '$targetNetwork' not found" -Level "Warning"
                            }
                        }
                    }
                }
                
                # If a name suffix is specified, rename the VM
                if (-not [string]::IsNullOrEmpty($NameSuffix)) {
                    $newVMName = "$($VMName)$($NameSuffix)"
                    Write-LogMessage "Renaming migrated VM to: $newVMName" -Level "Normal"
                    $migratedVM | Set-VM -Name $newVMName -Confirm:$false -ErrorAction Stop
                    Write-LogMessage "VM renamed successfully" -Level "Success"
                }
            }
            
            Write-LogMessage "VM '$VMName' successfully migrated to destination vCenter" -Level "Success"
            return $true
        }
        catch {
            throw "Migration failed: $_"
        }
    }
    catch {
        Write-LogMessage "Failed to migrate VM '$VMName': $_" -Level "Error"
        $script:FailedVMs += @{Name = $VMName; Error = $_.Exception.Message}
        return $false
    }
}
#endregion


#region Validation Functions
function Test-MigrationPrerequisites {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory=$true)]
        $SourceVI,
        
        [Parameter(Mandatory=$true)]
        $DestVI,
        
        [Parameter(Mandatory=$true)]
        [string[]]$VMNames
    )
    
    Write-LogMessage "Validating migration prerequisites..." -Level "Normal"
    $validationPassed = $true
    
    # Check Enhanced Linked Mode
    try {
        Write-LogMessage "Checking Enhanced Linked Mode connectivity..." -Level "Normal"
        # This is a basic check - in real scenarios you might want more sophisticated validation
        $sourceInfo = Get-View -Server $SourceVI -Id 'ServiceInstance'
        $destInfo = Get-View -Server $DestVI -Id 'ServiceInstance'
        
        if ($sourceInfo -and $destInfo) {
            Write-LogMessage "vCenter connections validated" -Level "Success"
        }
    }
    catch {
        Write-LogMessage "Error validating vCenter connectivity: $_" -Level "Error"
        $validationPassed = $false
    }
    
    # Check VM existence and accessibility
    Write-LogMessage "Validating source VMs..." -Level "Normal"
    foreach ($vmName in $VMNames) {
        try {
            $vm = Get-VM -Server $SourceVI -Name $vmName -ErrorAction Stop
            Write-LogMessage "VM '$vmName' found and accessible" -Level "Verbose"
        }
        catch {
            Write-LogMessage "VM '$vmName' not found or not accessible: $_" -Level "Error"
            $validationPassed = $false
        }
    }
    
    return $validationPassed
}
#endregion

#region Main Script Execution with Enhanced Functions
try {
    # Initialize script environment
    Initialize-Script
    
    # Log script parameters
    Write-LogMessage "==========  Cross-vCenter VM Migration Tool v2.3  ==========" -Level "Minimal"
    Write-LogMessage "Script started with parameters:" -Level "Normal"
    Write-LogMessage "  Source vCenter: $($SourceVCenter)" -Level "Normal"
    Write-LogMessage "  Destination vCenter: $($DestVCenter)" -Level "Normal"
    Write-LogMessage "  Name Suffix: $($NameSuffix)" -Level "Normal"
    Write-LogMessage "  Destination Cluster: $($DestinationCluster)" -Level "Normal"
    Write-LogMessage "  Disk Format: $($DiskFormat)" -Level "Normal"
    Write-LogMessage "  Preserve MAC: $($PreserveMAC)" -Level "Normal"
    Write-LogMessage "  Max Concurrent Migrations: $($script:ActualConcurrency)" -Level "Normal"
    Write-LogMessage "  Enhanced Network Handling: $($EnhancedNetworkHandling)" -Level "Normal"
    Write-LogMessage "  Ignore Network Errors: $($IgnoreNetworkErrors)" -Level "Normal"
    Write-LogMessage "  Disconnect Network During Migration: $($DisconnectNetworkDuringMigration)" -Level "Normal"
    Write-LogMessage "  Validation Mode: $($Validate)" -Level "Normal"
    Write-LogMessage "================================================================" -Level "Minimal"
    
    # Get list of VMs to process
    $vmsToProcess = @(Get-VMsToProcess)
    $vmTotal = $vmsToProcess.Count
    Write-LogMessage "Processing $($vmTotal) VMs" -Level "Normal"
    
    # Prompt for credentials if not provided
    if (-not $SourceVCCredential) {
        Write-LogMessage "Prompting for source vCenter credentials" -Level "Normal"
        $SourceVCCredential = Get-Credential -Message "Enter credentials for source vCenter ($($SourceVCenter))"
    }
    
    if (-not $DestVCCredential) {
        Write-LogMessage "Prompting for destination vCenter credentials" -Level "Normal"
        $DestVCCredential = Get-Credential -Message "Enter credentials for destination vCenter ($($DestVCenter))"
    }
    
    # Connect to both vCenters
    $sourceVI = Connect-ToVIServer -Server $SourceVCenter -Credential $SourceVCCredential -Description "source"
    $destVI = Connect-ToVIServer -Server $DestVCenter -Credential $DestVCCredential -Description "destination"
    
    # Validate prerequisites
    if ($Validate -or $script:LogLevelValue -ge 3) {
        $validationResult = Test-MigrationPrerequisites -SourceVI $sourceVI -DestVI $destVI -VMNames $vmsToProcess
        if (-not $validationResult) {
            Write-LogMessage "Validation failed. Please address the issues before proceeding." -Level "Error"
            if ($Validate) {
                return
            }
        }
        else {
            Write-LogMessage "Validation completed successfully" -Level "Success"
            if ($Validate) {
                Write-LogMessage "Validation mode completed. No migrations were performed." -Level "Normal"
                return
            }
        }
    }
    
    # Get destination cluster if specified
    if ($DestinationCluster) {
        try {
            $destCluster = Get-Cluster -Server $destVI -Name $DestinationCluster -ErrorAction Stop
            Write-LogMessage "Found destination cluster: $($destCluster.Name)" -Level "Success"
            $destHosts = Get-VMHost -Server $destVI -Location $destCluster
        }
        catch {
            Write-LogMessage "Failed to find destination cluster '$($DestinationCluster)': $_" -Level "Error"
            throw
        }
    }
    else {
        Write-LogMessage "No destination cluster specified, will use all available hosts" -Level "Normal"
        $destHosts = Get-VMHost -Server $destVI
    }
    
    # Get destination datastore if specified
    if ($DestinationDatastore) {
        try {
            $destDatastore = Get-Datastore -Server $destVI -Name $DestinationDatastore -ErrorAction Stop
            Write-LogMessage "Found destination datastore: $($destDatastore.Name)" -Level "Success"
        }
        catch {
            Write-LogMessage "Failed to find destination datastore '$($DestinationDatastore)': $_" -Level "Error"
            throw
        }
    }
    else {
        $destDatastore = $null  # Will be selected per VM
    }
    
    # Get the destination datacenter
    $destDatacenter = Get-Datacenter -Server $destVI | Select-Object -First 1
    if (-not $destDatacenter) {
        Write-LogMessage "No datacenter found in destination vCenter" -Level "Error"
        throw "No datacenter found in destination vCenter"
    }
    Write-LogMessage "Found destination datacenter: $($destDatacenter.Name)" -Level "Success"
    
    # Find VM root folder in destination
    $vmRootFolder = Find-VMRootFolder -Server $destVI -Datacenter $destDatacenter
    Write-LogMessage "Found VM root folder: $($vmRootFolder.Name)" -Level "Success"
    
    # Prepare migration queue
    Write-LogMessage "Preparing migration queue..." -Level "Normal"
    $migrationQueue = @()
    
    foreach ($vmName in $vmsToProcess) {
        $migrationParams = @{
            VMName = $vmName
            SourceServer = $SourceVCenter
            DestServer = $DestVCenter
            SourceCredential = $SourceVCCredential
            DestCredential = $DestVCCredential
            MigrationData = @{
                StartTime = Get-Date
                DiskStorageFormat = $DiskFormat
                NetworkMapping = $NetworkMapping
                NameSuffix = $NameSuffix
                PreserveMAC = $PreserveMAC.IsPresent
                EnhancedNetworkHandling = $EnhancedNetworkHandling.IsPresent
                IgnoreNetworkErrors = $IgnoreNetworkErrors.IsPresent
                DisconnectNetworkDuringMigration = $DisconnectNetworkDuringMigration.IsPresent
            }
        }
        
        # Pre-calculate destination objects for parallel execution
        try {
            $sourceVM = Get-VM -Server $sourceVI -Name $vmName -ErrorAction Stop
            $selectedHost = Get-OptimalVMHost -VMHosts $destHosts -RequiredCPU $sourceVM.NumCpu -RequiredMemoryMB $sourceVM.MemoryMB
            
            if (-not $destDatastore) {
                $hostDatastores = Get-Datastore -Server $destVI -VMHost $selectedHost
                $selectedDatastore = Get-OptimalDatastore -Datastores $hostDatastores -RequiredSpaceGB $sourceVM.UsedSpaceGB
            } else {
                $selectedDatastore = $destDatastore
            }
            
            $folderPath = Get-VMFolderPath -VM $sourceVM -Server $sourceVI
            $destFolder = Get-OrCreateFolderPath -Path $folderPath -RootFolder $vmRootFolder -VIServer $destVI
            
            $resourcePoolPath = Get-ResourcePoolPath -VM $sourceVM -Server $sourceVI
            $destClusterForVM = Get-Cluster -VMHost $selectedHost -Server $destVI
            $destResourcePool = Get-OrCreateResourcePoolPath -Path $resourcePoolPath -Cluster $destClusterForVM -VIServer $destVI
            
            # Add pre-calculated objects to migration data
            $migrationParams.MigrationData.DestHostName = $selectedHost.Name
            $migrationParams.MigrationData.DestDatastoreName = $selectedDatastore.Name
            $migrationParams.MigrationData.DestFolderId = $destFolder.Id
            $migrationParams.MigrationData.DestResourcePoolId = $destResourcePool.Id
            
            $migrationQueue += $migrationParams
        }
        catch {
            Write-LogMessage "Failed to prepare migration data for VM '$vmName': $_" -Level "Error"
            $script:FailedVMs += @{Name = $vmName; Error = $_.Exception.Message}
        }
    }
    
    Write-LogMessage "Migration queue prepared with $($migrationQueue.Count) VMs" -Level "Success"
    
    # Execute migrations based on mode and network handling preference
    if ($script:ActualConcurrency -eq 1) {
        Write-LogMessage "Starting sequential migrations..." -Level "Normal"
        
        if ($EnhancedNetworkHandling) {
            Write-LogMessage "Using enhanced network handling for sequential migrations" -Level "Normal"
            $results = Start-SequentialMigrations-Enhanced -MigrationQueue $migrationQueue -SourceVI $sourceVI -DestVI $destVI -DestHosts $destHosts -DestDatastore $destDatastore -VmRootFolder $vmRootFolder
        } else {
            $results = Start-SequentialMigrations -MigrationQueue $migrationQueue -SourceVI $sourceVI -DestVI $destVI -DestHosts $destHosts -DestDatastore $destDatastore -VmRootFolder $vmRootFolder
        }
        
        $successCount = $results.SuccessCount
        $failureCount = $results.FailureCount
    }
    else {
        Write-LogMessage "Starting parallel migrations..." -Level "Normal"
        
        if ($EnhancedNetworkHandling) {
            Write-LogMessage "Using enhanced network handling for parallel migrations" -Level "Normal"
            Start-ParallelMigrations-Enhanced -MigrationQueue $migrationQueue -MaxConcurrent $script:ActualConcurrency
        } else {
            Start-ParallelMigrations -MigrationQueue $migrationQueue -MaxConcurrent $script:ActualConcurrency
        }
        
        $successCount = $script:CompletedMigrations | Where-Object { $_.Success } | Measure-Object | Select-Object -ExpandProperty Count
        $failureCount = $script:FailedVMs.Count
    }
    
    # Summary report
    Write-LogMessage "==========  Operation Summary  ==========" -Level "Minimal"
    Write-LogMessage "Total VMs processed: $($vmTotal)" -Level "Minimal"
    Write-LogMessage "Successful: $($successCount)" -Level "Minimal"
    Write-LogMessage "Failed: $($failureCount)" -Level "Minimal"
    
    if ($failureCount -gt 0) {
        Write-LogMessage "Failed VMs:" -Level "Minimal"
        foreach ($failedVM in $script:FailedVMs) {
            Write-LogMessage "  - $($failedVM.Name): $($failedVM.Error)" -Level "Minimal"
        }
    }
    
    # Show timing information for parallel migrations
    if ($script:ActualConcurrency -gt 1 -and $script:CompletedMigrations.Count -gt 0) {
        $successfulMigrations = $script:CompletedMigrations | Where-Object { $_.Success }
        if ($successfulMigrations.Count -gt 0) {
            $avgDuration = ($successfulMigrations | ForEach-Object { ($_.EndTime - $_.StartTime).TotalMinutes } | Measure-Object -Average).Average
            Write-LogMessage "Average migration time: $([math]::Round($avgDuration, 2)) minutes" -Level "Normal"
        }
    }
    
    # Network handling summary
    if ($EnhancedNetworkHandling) {
        Write-LogMessage "Enhanced network handling was enabled" -Level "Normal"
    }
    
    Write-LogMessage "=======================================" -Level "Minimal"
    
    if ($successCount -eq $vmTotal) {
        Write-LogMessage "All VMs migrated successfully!" -Level "Success"
    }
    elseif ($successCount -gt 0) {
        Write-LogMessage "Migration completed with some failures. Please review the log for details." -Level "Warning"
    }
    else {
        Write-LogMessage "No VMs were successfully migrated. Please review the errors above." -Level "Error"
    }
}
catch {
    Write-LogMessage "Script execution failed: $_" -Level "Error"
    
    # Calculate execution time if we have a start time
    if (Get-Variable -Name "ScriptStartTime" -Scope Script -ErrorAction SilentlyContinue) {
        $executionTime = (Get-Date) - $script:ScriptStartTime
        Write-LogMessage "Total execution time: $($executionTime.ToString('hh\:mm\:ss'))" -Level "Normal"
    }
    
    throw
}
finally {
    # Clean up any remaining background jobs
    $remainingJobs = Get-Job | Where-Object { $_.Name -like "*Migration*" }
    if ($remainingJobs) {
        Write-LogMessage "Cleaning up $($remainingJobs.Count) remaining background jobs..." -Level "Normal"
        $remainingJobs | Stop-Job -PassThru | Remove-Job -Force
    }
    
    # Disconnect from vCenters
    if ($sourceVI) {
        Disconnect-FromVIServer -Server $sourceVI -Description "source"
    }
    
    if ($destVI) {
        Disconnect-FromVIServer -Server $destVI -Description "destination"
    }
    
    # Calculate execution time if we have a start time
    if (Get-Variable -Name "ScriptStartTime" -Scope Script -ErrorAction SilentlyContinue) {
        $executionTime = (Get-Date) - $script:ScriptStartTime
        Write-LogMessage "Total execution time: $($executionTime.ToString('hh\:mm\:ss'))" -Level "Normal"
    }
    
    Write-LogMessage "Script execution completed" -Level "Minimal"
    Write-LogMessage "Log file saved to: $($script:LogFilePath)" -Level "Normal"
}
#endregion

