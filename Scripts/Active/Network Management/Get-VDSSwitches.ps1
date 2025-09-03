<#
.SYNOPSIS
    Discovers Virtual Distributed Switch (vDS) information using PowerCLI 13.x
.DESCRIPTION
    Connects to vCenter and discovers all vDS switches with their port groups
    for display purposes without performing any backup operations.
.NOTES
    Version: 1.0 - PowerCLI 13.x optimized for discovery only
    Requires: VMware.PowerCLI 13.x or later
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,
    
    [Parameter()]
    [bool]$BypassModuleCheck = $false,
    
    [Parameter()]
    [string]$LogPath,
    
    [Parameter()]
    [bool]$SuppressConsoleOutput = $false
)

# Embedded logging functions for SDK execution compatibility
$Global:ScriptLogFile = $null
$Global:SuppressConsoleOutput = $false

function Write-LogInfo { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Info] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor White }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogSuccess { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Success] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Green }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogWarning { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Warning] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Yellow }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Write-LogError { 
    param([string]$Message, [string]$Category = '')
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "$timestamp [Error] [$Category] $Message"
    if (-not $Global:SuppressConsoleOutput) { Write-Host $logEntry -ForegroundColor Red }
    if ($Global:ScriptLogFile) { $logEntry | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8 }
}

function Start-ScriptLogging {
    param(
        [string]$ScriptName = '',
        [string]$LogPath = $null,
        [bool]$SuppressConsoleOutput = $false
    )
    
    $Global:SuppressConsoleOutput = $SuppressConsoleOutput
    
    if ($LogPath) {
        if ([System.IO.Path]::HasExtension($LogPath)) {
            $logDir = [System.IO.Path]::GetDirectoryName($LogPath)
        } else {
            $logDir = $LogPath
        }
        
        $psLogDir = Join-Path $logDir "PowerShell"
        if (-not (Test-Path $psLogDir)) {
            New-Item -ItemType Directory -Path $psLogDir -Force | Out-Null
        }
        
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $sessionId = [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
        $Global:ScriptLogFile = Join-Path $psLogDir "${ScriptName}_${timestamp}_${sessionId}.log"
        
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Encoding UTF8
        "SCRIPT START: $ScriptName" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "Start Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

function Stop-ScriptLogging {
    param(
        [bool]$Success = $true,
        [string]$Summary = "",
        [hashtable]$Statistics = @{}
    )
    
    if ($Global:ScriptLogFile) {
        $separator = "=" * 80
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        if ($Success) {
            "SCRIPT COMPLETED SUCCESSFULLY" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        } else {
            "SCRIPT FAILED" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Summary) {
            "Summary: $Summary" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        }
        
        if ($Statistics.Count -gt 0) {
            "Statistics:" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            foreach ($key in $Statistics.Keys) {
                "    $key = $($Statistics[$key])" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
            }
        }
        
        "End Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
        "$separator" | Out-File -FilePath $Global:ScriptLogFile -Append -Encoding UTF8
    }
}

# Start logging
Start-ScriptLogging -ScriptName "Get-VDSSwitches" -LogPath $LogPath -SuppressConsoleOutput $SuppressConsoleOutput

$scriptSuccess = $false
$finalSummary = ""
$discoveryData = @{
    DiscoveryDate = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    SourceVCenter = $VCenterServer
    VDSSwitches = @()
    TotalSwitches = 0
    TotalPortGroups = 0
}

try {
    Write-LogInfo "Starting vDS discovery process" -Category "Initialization"
    
    # PowerCLI module management handled by service layer
    
    # Configure PowerCLI settings
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
    
    # Use existing vCenter connection established by PersistentVcenterConnectionService
    Write-LogInfo "Using existing vCenter connection: $VCenterServer" -Category "Connection"
    $viConnection = $global:DefaultVIServers | Where-Object { $_.Name -eq $VCenterServer }
    if (-not $viConnection -or -not $viConnection.IsConnected) {
        throw "vCenter connection to '$VCenterServer' not found or not active. Please establish connection through main UI first."
    }
    Write-LogSuccess "Using vCenter connection: $($viConnection.Name) (v$($viConnection.Version))" -Category "Connection"
    
    # Get all distributed virtual switches
    Write-LogInfo "Discovering Virtual Distributed Switches..." -Category "Discovery"
    $vdSwitches = Get-VDSwitch -ErrorAction SilentlyContinue
    
    if (-not $vdSwitches) {
        Write-LogWarning "No Virtual Distributed Switches found in vCenter" -Category "Discovery"
    }
    else {
        Write-LogInfo "Found $($vdSwitches.Count) Virtual Distributed Switches" -Category "Discovery"
        
        foreach ($vds in $vdSwitches) {
            Write-LogInfo "Discovering vDS: $($vds.Name)" -Category "Discovery"
            
            # Create vDS discovery object with basic information
            $vdsDiscovery = @{
                Name = $vds.Name
                Uuid = $vds.ExtensionData.Uuid
                Version = $vds.Version
                Type = "VmwareDistributedVirtualSwitch"
                MaxPorts = $vds.MaxPorts
                NumPorts = $vds.NumPorts
                PortGroups = @()
            }
            
            # Get all port groups for this vDS (lightweight - just name and basic info)
            Write-LogInfo "Discovering port groups for vDS: $($vds.Name)" -Category "Discovery"
            $portGroups = Get-VDPortgroup -VDSwitch $vds
            
            foreach ($pg in $portGroups) {
                # Skip uplink port groups
                if ($pg.IsUplink) {
                    Write-LogInfo "Skipping uplink port group: $($pg.Name)" -Category "Discovery"
                    continue
                }
                
                # Create lightweight port group info
                $pgDiscovery = @{
                    Name = $pg.Name
                    Type = "DistributedVirtualPortgroup"
                    NumPorts = $pg.NumPorts
                    VlanId = if ($pg.VlanConfiguration -and $pg.VlanConfiguration.VlanId) { $pg.VlanConfiguration.VlanId } else { 0 }
                    IsSelected = $false
                }
                
                $vdsDiscovery.PortGroups += $pgDiscovery
                $discoveryData.TotalPortGroups++
            }
            
            $discoveryData.VDSSwitches += $vdsDiscovery
            $discoveryData.TotalSwitches++
            
            Write-LogSuccess "Discovered vDS '$($vds.Name)' with $($vdsDiscovery.PortGroups.Count) port groups" -Category "Discovery"
        }
    }
    
    $scriptSuccess = $true
    $finalSummary = "Successfully discovered $($discoveryData.TotalSwitches) vDS switches with $($discoveryData.TotalPortGroups) port groups"
    
    # Output discovery data as JSON for the application
    $jsonOutput = $discoveryData | ConvertTo-Json -Depth 10
    Write-Output $jsonOutput
    
} catch {
    $scriptSuccess = $false
    $finalSummary = "Discovery failed: $($_.Exception.Message)"
    Write-LogError "Discovery failed: $($_.Exception.Message)" -Category "Error"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)" -Category "Error"
    
    # Output error for the application
    Write-Output "ERROR: $($_.Exception.Message)"
    
} finally {
    # Disconnect from vCenter
    if ($viConnection) {
        Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
        # DISCONNECT REMOVED - Using persistent connections managed by application
    }
    
    $discoveryStats = @{
        VCenterServer = $VCenterServer
        SwitchesFound = $discoveryData.TotalSwitches
        PortGroupsFound = $discoveryData.TotalPortGroups
    }
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $discoveryStats
}