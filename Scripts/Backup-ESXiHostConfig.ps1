# Backup-ESXiHostConfig.ps1
# Enhanced ESXi host configuration backup with integrated logging system

param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,
    
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    
    [bool]$IncludeAdvancedSettings = $true,
    [bool]$IncludeNetworkConfig = $true,
    [bool]$IncludeStorageConfig = $true,
    [bool]$IncludeServices = $true,
    [bool]$BypassModuleCheck = $false,
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

# Start logging (suppress console output if requested)
Start-ScriptLogging -ScriptName "Backup-ESXiHostConfig" -SuppressConsoleOutput $SuppressConsoleOutput

# Initialize variables for the finally block
$scriptSuccess = $false
$finalSummary = ""
$finalStats = @{}
$result = @{}

try {
    Write-LogInfo "Starting backup of ESXi host: $($HostName)"
    Write-LogInfo "Backup destination: $($BackupPath)"
    Write-LogInfo "Options: AdvancedSettings=$($IncludeAdvancedSettings), Network=$($IncludeNetworkConfig), Storage=$($IncludeStorageConfig), Services=$($IncludeServices)"
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-LogInfo "Importing PowerCLI modules..."
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-LogSuccess "PowerCLI modules imported successfully"
        }
        catch {
            Write-LogCritical "Failed to import PowerCLI modules: $($_.Exception.Message)"
            throw $_
        }
    }
    else {
        Write-LogInfo "Bypassing PowerCLI module check (already confirmed installed)"
    }
    
    # Check vCenter connection
    if (-not $global:DefaultVIServer -or $global:DefaultVIServer.IsConnected -eq $false) {
        Write-LogCritical "No active vCenter connection found. Please connect to vCenter first."
        throw "No vCenter connection"
    }
    
    Write-LogInfo "Connected to vCenter: $($global:DefaultVIServer.Name)" -Category "Connection"
    
    # Get the VMHost object
    Write-LogInfo "Retrieving VMHost object for: $($HostName)" -Category "Discovery"
    $vmhost = Get-VMHost -Name $HostName -ErrorAction Stop
    
    if (-not $vmhost) {
        Write-LogError "Host $($HostName) not found in vCenter"
        throw "Host $($HostName) not found"
    }
    
    Write-LogSuccess "Found host: $($vmhost.Name)" -Category "Discovery"
    Write-LogInfo "  Version: $($vmhost.Version) Build: $($vmhost.Build)" -Category "Discovery"
    Write-LogInfo "  Model: $($vmhost.Model) Manufacturer: $($vmhost.Manufacturer)" -Category "Discovery"
    Write-LogInfo "  Connection State: $($vmhost.ConnectionState)" -Category "Discovery"
    
    # Initialize backup object
    Write-LogInfo "Initializing backup data structure" -Category "Backup"
    $backup = @{
        HostName = $vmhost.Name
        Version = $vmhost.Version
        Build = $vmhost.Build
        Model = $vmhost.Model
        Manufacturer = $vmhost.Manufacturer
        ProcessorType = $vmhost.ProcessorType
        BackupDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        BackupUser = $env:USERNAME
    }
    
    # Track backup components
    $componentStats = @{
        NetworkItems = 0
        StorageItems = 0
        ServiceItems = 0
        AdvancedSettings = 0
    }
    
    # Network Configuration
    if ($IncludeNetworkConfig) {
        Write-LogInfo "Backing up network configuration..." -Category "Network"
        $networkStartTime = Get-Date
        
        $backup.NetworkConfig = @{
            VirtualSwitches = @()
            PortGroups = @()
            VMKernelAdapters = @()
            PhysicalAdapters = @()
            DNSConfig = @{}
        }
        
        try {
            # Virtual Switches
            Write-LogDebug "  Retrieving virtual switches..." -Category "Network"
            $vSwitches = Get-VirtualSwitch -VMHost $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($vSwitches.Count) virtual switches" -Category "Network"
            
            foreach ($vSwitch in $vSwitches) {
                Write-LogVerbose "    Processing vSwitch: $($vSwitch.Name)" -Category "Network"
                $backup.NetworkConfig.VirtualSwitches += @{
                    Name = $vSwitch.Name
                    NumPorts = $vSwitch.NumPorts
                    NumPortsAvailable = $vSwitch.NumPortsAvailable
                    Mtu = $vSwitch.Mtu
                    Nic = $vSwitch.Nic -join ","
                }
                $componentStats.NetworkItems++
            }
            
            # Port Groups
            Write-LogDebug "  Retrieving port groups..." -Category "Network"
            $portGroups = Get-VirtualPortGroup -VMHost $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($portGroups.Count) port groups" -Category "Network"
            
            foreach ($pg in $portGroups) {
                Write-LogVerbose "    Processing port group: $($pg.Name)" -Category "Network"
                $backup.NetworkConfig.PortGroups += @{
                    Name = $pg.Name
                    VirtualSwitch = $pg.VirtualSwitchName
                    VLanId = $pg.VLanId
                }
                $componentStats.NetworkItems++
            }
            
            # VMKernel Adapters
            Write-LogDebug "  Retrieving VMKernel adapters..." -Category "Network"
            $vmkAdapters = Get-VMHostNetworkAdapter -VMHost $vmhost -VMKernel -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($vmkAdapters.Count) VMKernel adapters" -Category "Network"
            
            foreach ($vmk in $vmkAdapters) {
                Write-LogVerbose "    Processing VMK: $($vmk.Name) - IP: $($vmk.IP)" -Category "Network"
                $backup.NetworkConfig.VMKernelAdapters += @{
                    Name = $vmk.Name
                    IP = $vmk.IP
                    SubnetMask = $vmk.SubnetMask
                    Mac = $vmk.Mac
                    PortGroupName = $vmk.PortGroupName
                    DhcpEnabled = $vmk.DhcpEnabled
                    ManagementTrafficEnabled = $vmk.ManagementTrafficEnabled
                    VMotionEnabled = $vmk.VMotionEnabled
                    FaultToleranceLoggingEnabled = $vmk.FaultToleranceLoggingEnabled
                    VsanTrafficEnabled = $vmk.VsanTrafficEnabled
                }
                $componentStats.NetworkItems++
            }
            
            $networkTime = (Get-Date) - $networkStartTime
            Write-LogSuccess "Network configuration backed up in $($networkTime.TotalSeconds.ToString('F2')) seconds ($($componentStats.NetworkItems) items)" -Category "Network"
        }
        catch {
            Write-LogError "Failed to backup network configuration: $($_.Exception.Message)" -Category "Network"
            throw $_
        }
    }
    
    # Storage Configuration
    if ($IncludeStorageConfig) {
        Write-LogInfo "Backing up storage configuration..." -Category "Storage"
        $storageStartTime = Get-Date
        
        $backup.StorageConfig = @{
            Datastores = @()
            StorageAdapters = @()
        }
        
        try {
            # Datastores
            Write-LogDebug "  Retrieving datastores..." -Category "Storage"
            $datastores = Get-Datastore -VMHost $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($datastores.Count) datastores" -Category "Storage"
            
            foreach ($ds in $datastores) {
                Write-LogVerbose "    Processing datastore: $($ds.Name) - Capacity: $([math]::Round($ds.CapacityGB, 2))GB" -Category "Storage"
                $backup.StorageConfig.Datastores += @{
                    Name = $ds.Name
                    CapacityGB = [math]::Round($ds.CapacityGB, 2)
                    FreeSpaceGB = [math]::Round($ds.FreeSpaceGB, 2)
                    Type = $ds.Type
                    FileSystemVersion = $ds.FileSystemVersion
                    Accessible = $ds.Accessible
                }
                $componentStats.StorageItems++
            }
            
            # Storage Adapters
            Write-LogDebug "  Retrieving storage adapters..." -Category "Storage"
            $hbas = Get-VMHostHba -VMHost $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($hbas.Count) storage adapters" -Category "Storage"
            
            foreach ($hba in $hbas) {
                Write-LogVerbose "    Processing HBA: $($hba.Device) - Type: $($hba.Type)" -Category "Storage"
                $backup.StorageConfig.StorageAdapters += @{
                    Device = $hba.Device
                    Type = $hba.Type
                    Model = $hba.Model
                    Driver = $hba.Driver
                    Status = $hba.Status
                }
                $componentStats.StorageItems++
            }
            
            $storageTime = (Get-Date) - $storageStartTime
            Write-LogSuccess "Storage configuration backed up in $($storageTime.TotalSeconds.ToString('F2')) seconds ($($componentStats.StorageItems) items)" -Category "Storage"
        }
        catch {
            Write-LogError "Failed to backup storage configuration: $($_.Exception.Message)" -Category "Storage"
            throw $_
        }
    }
    
    # Services
    if ($IncludeServices) {
        Write-LogInfo "Backing up services configuration..." -Category "Services"
        $servicesStartTime = Get-Date
        
        try {
            $backup.Services = @()
            $services = Get-VMHostService -VMHost $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($services.Count) services" -Category "Services"
            
            foreach ($service in $services) {
                Write-LogVerbose "    Service: $($service.Label) - Running: $($service.Running)" -Category "Services"
                $backup.Services += @{
                    Key = $service.Key
                    Label = $service.Label
                    Running = $service.Running
                    Required = $service.Required
                    Policy = $service.Policy
                }
                $componentStats.ServiceItems++
            }
            
            $servicesTime = (Get-Date) - $servicesStartTime
            Write-LogSuccess "Services configuration backed up in $($servicesTime.TotalSeconds.ToString('F2')) seconds ($($componentStats.ServiceItems) items)" -Category "Services"
        }
        catch {
            Write-LogError "Failed to backup services configuration: $($_.Exception.Message)" -Category "Services"
            throw $_
        }
    }
    
    # Advanced Settings
    if ($IncludeAdvancedSettings) {
        Write-LogInfo "Backing up advanced settings..." -Category "Advanced"
        $advStartTime = Get-Date
        
        try {
            $backup.AdvancedSettings = @{}
            $advSettings = Get-AdvancedSetting -Entity $vmhost -ErrorAction SilentlyContinue
            Write-LogInfo "  Found $($advSettings.Count) advanced settings" -Category "Advanced"
            
            $settingCount = 0
            foreach ($setting in $advSettings) {
                $backup.AdvancedSettings[$setting.Name] = $setting.Value
                $settingCount++
                $componentStats.AdvancedSettings++
                
                if ($settingCount % 250 -eq 0) {
                    Write-LogDebug "    Processed $($settingCount)/$($advSettings.Count) advanced settings..." -Category "Advanced"
                }
            }
            
            $advTime = (Get-Date) - $advStartTime
            Write-LogSuccess "Advanced settings backed up in $($advTime.TotalSeconds.ToString('F2')) seconds ($($componentStats.AdvancedSettings) items)" -Category "Advanced"
        }
        catch {
            Write-LogError "Failed to backup advanced settings: $($_.Exception.Message)" -Category "Advanced"
            throw $_
        }
    }
    
    # Additional configurations
    Write-LogInfo "Backing up additional configurations..." -Category "Additional"
    
    try {
        # NTP Servers
        Write-LogDebug "  Retrieving NTP servers..." -Category "Additional"
        $ntpServers = Get-VMHostNtpServer -VMHost $vmhost -ErrorAction SilentlyContinue
        $backup.NtpServers = $ntpServers
        if ($ntpServers.Count -gt 0) {
            Write-LogInfo "  NTP Servers: $($ntpServers -join ', ')" -Category "Additional"
        } else {
            Write-LogInfo "  No NTP servers configured" -Category "Additional"
        }
        
        # Syslog Configuration
        Write-LogDebug "  Retrieving syslog configuration..." -Category "Additional"
        $syslog = Get-AdvancedSetting -Entity $vmhost -Name "Syslog.global.logHost" -ErrorAction SilentlyContinue
        if ($syslog -and $syslog.Value) {
            $backup.SyslogServer = $syslog.Value
            Write-LogInfo "  Syslog Server: $($syslog.Value)" -Category "Additional"
        } else {
            Write-LogInfo "  No syslog server configured" -Category "Additional"
        }
        
        # DNS Configuration
        Write-LogDebug "  Retrieving DNS configuration..." -Category "Additional"
        $dnsConfig = Get-VMHostNetwork -VMHost $vmhost -ErrorAction SilentlyContinue
        if ($dnsConfig) {
            $backup.DNSConfig = @{
                HostName = $dnsConfig.HostName
                DomainName = $dnsConfig.DomainName
                DnsAddress = $dnsConfig.DnsAddress
                SearchDomain = $dnsConfig.SearchDomain
            }
            Write-LogInfo "  DNS: $($dnsConfig.DnsAddress -join ', ') Domain: $($dnsConfig.DomainName)" -Category "Additional"
        }
    }
    catch {
        Write-LogWarning "Some additional configurations could not be retrieved: $($_.Exception.Message)" -Category "Additional"
    }
    
    # Create backup directory if it doesn't exist
    if (-not (Test-Path $BackupPath)) {
        Write-LogInfo "Creating backup directory: $($BackupPath)" -Category "FileSystem"
        try {
            New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
            Write-LogSuccess "Backup directory created successfully" -Category "FileSystem"
        }
        catch {
            Write-LogError "Failed to create backup directory: $($_.Exception.Message)" -Category "FileSystem"
            throw $_
        }
    }
    
    # Generate filename with timestamp
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $fileName = "$($vmhost.Name)_backup_$($timestamp).json"
    $fullPath = Join-Path $BackupPath $fileName
    
    # Convert to JSON and save
    Write-LogInfo "Saving backup to: $($fullPath)" -Category "Export"
    $jsonStartTime = Get-Date
    
    try {
        $jsonContent = $backup | ConvertTo-Json -Depth 10
        $jsonSize = [math]::Round($jsonContent.Length / 1MB, 2)
        Write-LogInfo "  JSON size: $($jsonSize)MB" -Category "Export"
        
        $jsonContent | Out-File -FilePath $fullPath -Encoding UTF8
        
        $jsonTime = (Get-Date) - $jsonStartTime
        Write-LogSuccess "Backup saved successfully in $($jsonTime.TotalSeconds.ToString('F2')) seconds" -Category "Export"
        
        # Verify file was created
        if (Test-Path $fullPath) {
            $fileInfo = Get-Item $fullPath
            Write-LogInfo "  File size: $([math]::Round($fileInfo.Length / 1MB, 2))MB" -Category "Export"
            Write-LogInfo "  File created: $($fileInfo.CreationTime)" -Category "Export"
        } else {
            throw "Backup file was not created successfully"
        }
    }
    catch {
        Write-LogError "Failed to save backup file: $($_.Exception.Message)" -Category "Export"
        throw $_
    }
    
    # Calculate total items backed up
    $totalItems = $componentStats.NetworkItems + $componentStats.StorageItems + $componentStats.ServiceItems + $componentStats.AdvancedSettings
    
    # Prepare success result
    $result = @{
        Success = $true
        Message = "Backup completed successfully"
        FilePath = $fullPath
        HostName = $vmhost.Name
        BackupDate = $backup.BackupDate
        FileSize = $jsonSize
        TotalItems = $totalItems
        ComponentStats = $componentStats
    }
    
    Write-LogSuccess "Backup operation completed successfully for host: $($HostName)"
    
    # Prepare statistics for final logging
    $finalStats = @{
        "Host" = $HostName
        "Version" = $vmhost.Version
        "Build" = $vmhost.Build
        "FileSize" = "$($jsonSize)MB"
        "TotalItems" = $totalItems
        "NetworkItems" = $componentStats.NetworkItems
        "StorageItems" = $componentStats.StorageItems
        "ServiceItems" = $componentStats.ServiceItems
        "AdvancedSettings" = $componentStats.AdvancedSettings
        "NTPServers" = if ($backup.NtpServers) { $backup.NtpServers.Count } else { 0 }
        "SyslogConfigured" = if ($backup.SyslogServer) { "Yes" } else { "No" }
    }
    
    $finalSummary = "Host $($HostName) backed up successfully to $($fileName) ($($totalItems) items, $($jsonSize)MB)"
    $scriptSuccess = $true
    
} catch {
    Write-LogCritical "Backup operation failed: $($_.Exception.Message)"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    $result = @{
        Success = $false
        Message = "Backup failed: $($_.Exception.Message)"
        HostName = $HostName
        Error = $_.Exception.Message
        ErrorType = $_.Exception.GetType().Name
    }
    
    $finalSummary = "Failed to backup host $($HostName): $($_.Exception.Message)"
    $scriptSuccess = $false
    
    # Re-throw the exception to ensure the calling process knows about the failure
    throw $_
}
finally {
    # Calculate total execution time
    $totalTime = if ($Global:ScriptStartTime) {
        ((Get-Date) - $Global:ScriptStartTime).TotalSeconds.ToString('F2')
    } else {
        "Unknown"
    }
    
    $finalStats["ExecutionTime"] = "$($totalTime)s"
    
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
    
    # Output the final result as JSON for consumption by calling scripts
    $result | ConvertTo-Json -Compress
}   