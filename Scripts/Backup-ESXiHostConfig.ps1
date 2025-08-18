# Backup-ESXiHostConfig.ps1
# Enhanced with detailed logging and integrated logging system

param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,
    
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,
    
    [bool]$IncludeAdvancedSettings = $true,
    [bool]$IncludeNetworkConfig = $true,
    [bool]$IncludeStorageConfig = $true,
    [bool]$IncludeServices = $true,
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Backup-ESXiHostConfig"

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
    
    Write-LogInfo "Connected to vCenter: $($global:DefaultVIServer.Name)"
    
    # Get the VMHost object
    Write-LogInfo "Retrieving VMHost object for: $($HostName)"
    $vmhost = Get-VMHost -Name $HostName -ErrorAction Stop
    
    if (-not $vmhost) {
        Write-LogError "Host $($HostName) not found in vCenter"
        throw "Host $($HostName) not found"
    }
    
    Write-LogSuccess "Found host: $($vmhost.Name)"
    Write-LogInfo "  Version: $($vmhost.Version)"
    Write-LogInfo "  Build: $($vmhost.Build)"
    Write-LogInfo "  Model: $($vmhost.Model)"
    Write-LogInfo "  Connection State: $($vmhost.ConnectionState)"
    
    # Initialize backup object
    Write-LogInfo "Initializing backup data structure"
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
        
        # Virtual Switches
        Write-LogDebug "  Retrieving virtual switches..."
        $vSwitches = Get-VirtualSwitch -VMHost $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($vSwitches.Count) virtual switches"
        
        foreach ($vSwitch in $vSwitches) {
            Write-LogVerbose "    Processing vSwitch: $($vSwitch.Name)"
            $backup.NetworkConfig.VirtualSwitches += @{
                Name = $vSwitch.Name
                NumPorts = $vSwitch.NumPorts
                NumPortsAvailable = $vSwitch.NumPortsAvailable
                Mtu = $vSwitch.Mtu
                Nic = $vSwitch.Nic -join ","
            }
        }
        
        # Port Groups
        Write-LogDebug "  Retrieving port groups..."
        $portGroups = Get-VirtualPortGroup -VMHost $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($portGroups.Count) port groups"
        
        foreach ($pg in $portGroups) {
            Write-LogVerbose "    Processing port group: $($pg.Name)"
            $backup.NetworkConfig.PortGroups += @{
                Name = $pg.Name
                VirtualSwitch = $pg.VirtualSwitchName
                VLanId = $pg.VLanId
            }
        }
        
        # VMKernel Adapters
        Write-LogDebug "  Retrieving VMKernel adapters..."
        $vmkAdapters = Get-VMHostNetworkAdapter -VMHost $vmhost -VMKernel -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($vmkAdapters.Count) VMKernel adapters"
        
        foreach ($vmk in $vmkAdapters) {
            Write-LogVerbose "    Processing VMK: $($vmk.Name) - IP: $($vmk.IP)"
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
        }
        
        $networkTime = (Get-Date) - $networkStartTime
        Write-LogSuccess "Network configuration backed up in $($networkTime.TotalSeconds) seconds" -Category "Network"
    }
    
    # Storage Configuration
    if ($IncludeStorageConfig) {
        Write-LogInfo "Backing up storage configuration..." -Category "Storage"
        $storageStartTime = Get-Date
        
        $backup.StorageConfig = @{
            Datastores = @()
            StorageAdapters = @()
        }
        
        # Datastores
        Write-LogDebug "  Retrieving datastores..."
        $datastores = Get-Datastore -VMHost $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($datastores.Count) datastores"
        
        foreach ($ds in $datastores) {
            Write-LogVerbose "    Processing datastore: $($ds.Name) - Capacity: $([math]::Round($ds.CapacityGB, 2))GB"
            $backup.StorageConfig.Datastores += @{
                Name = $ds.Name
                CapacityGB = [math]::Round($ds.CapacityGB, 2)
                FreeSpaceGB = [math]::Round($ds.FreeSpaceGB, 2)
                Type = $ds.Type
                FileSystemVersion = $ds.FileSystemVersion
                Accessible = $ds.Accessible
            }
        }
        
        # Storage Adapters
        Write-LogDebug "  Retrieving storage adapters..."
        $hbas = Get-VMHostHba -VMHost $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($hbas.Count) storage adapters"
        
        foreach ($hba in $hbas) {
            Write-LogVerbose "    Processing HBA: $($hba.Device) - Type: $($hba.Type)"
            $backup.StorageConfig.StorageAdapters += @{
                Device = $hba.Device
                Type = $hba.Type
                Model = $hba.Model
                Driver = $hba.Driver
                Status = $hba.Status
            }
        }
        
        $storageTime = (Get-Date) - $storageStartTime
        Write-LogSuccess "Storage configuration backed up in $($storageTime.TotalSeconds) seconds" -Category "Storage"
    }
    
    # Services
    if ($IncludeServices) {
        Write-LogInfo "Backing up services configuration..." -Category "Services"
        $servicesStartTime = Get-Date
        
        $backup.Services = @()
        $services = Get-VMHostService -VMHost $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($services.Count) services"
        
        foreach ($service in $services) {
            Write-LogVerbose "    Service: $($service.Label) - Running: $($service.Running)"
            $backup.Services += @{
                Key = $service.Key
                Label = $service.Label
                Running = $service.Running
                Required = $service.Required
                Policy = $service.Policy
            }
        }
        
        $servicesTime = (Get-Date) - $servicesStartTime
        Write-LogSuccess "Services configuration backed up in $($servicesTime.TotalSeconds) seconds" -Category "Services"
    }
    
    # Advanced Settings
    if ($IncludeAdvancedSettings) {
        Write-LogInfo "Backing up advanced settings..." -Category "Advanced"
        $advStartTime = Get-Date
        
        $backup.AdvancedSettings = @{}
        $advSettings = Get-AdvancedSetting -Entity $vmhost -ErrorAction SilentlyContinue
        Write-LogInfo "  Found $($advSettings.Count) advanced settings"
        
        $settingCount = 0
        foreach ($setting in $advSettings) {
            $backup.AdvancedSettings[$setting.Name] = $setting.Value
            $settingCount++
            
            if ($settingCount % 100 -eq 0) {
                Write-LogDebug "    Processed $($settingCount) advanced settings..."
            }
        }
        
        $advTime = (Get-Date) - $advStartTime
        Write-LogSuccess "Advanced settings backed up in $($advTime.TotalSeconds) seconds" -Category "Advanced"
    }
    
    # Additional configurations
    Write-LogInfo "Backing up additional configurations..." -Category "Additional"
    
    # NTP Servers
    Write-LogDebug "  Retrieving NTP servers..."
    $ntpServers = Get-VMHostNtpServer -VMHost $vmhost -ErrorAction SilentlyContinue
    $backup.NtpServers = $ntpServers
    Write-LogInfo "  NTP Servers: $($ntpServers -join ', ')"
    
    # Syslog Configuration
    Write-LogDebug "  Retrieving syslog configuration..."
    $syslog = Get-AdvancedSetting -Entity $vmhost -Name "Syslog.global.logHost" -ErrorAction SilentlyContinue
    if ($syslog) {
        $backup.SyslogServer = $syslog.Value
        Write-LogInfo "  Syslog Server: $($syslog.Value)"
    }
    
    # Create backup directory if it doesn't exist
    if (-not (Test-Path $BackupPath)) {
        Write-LogInfo "Creating backup directory: $($BackupPath)"
        New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    }
    
    # Generate filename with timestamp
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $fileName = "$($vmhost.Name)_backup_$($timestamp).json"
    $fullPath = Join-Path $BackupPath $fileName
    
    # Convert to JSON and save
    Write-LogInfo "Saving backup to: $($fullPath)"
    $jsonStartTime = Get-Date
    
    $jsonContent = $backup | ConvertTo-Json -Depth 10
    $jsonSize = [math]::Round($jsonContent.Length / 1MB, 2)
    Write-LogInfo "  JSON size: $($jsonSize)MB"
    
    $jsonContent | Out-File -FilePath $fullPath -Encoding UTF8
    
    $jsonTime = (Get-Date) - $jsonStartTime
    Write-LogSuccess "Backup saved successfully in $($jsonTime.TotalSeconds) seconds"
    
    # Verify file was created
    if (Test-Path $fullPath) {
        $fileInfo = Get-Item $fullPath
        Write-LogInfo "  File size: $([math]::Round($fileInfo.Length / 1MB, 2))MB"
        Write-LogInfo "  File created: $($fileInfo.CreationTime)"
    }
    
    # Prepare success result
    $result = @{
        Success = $true
        Message = "Backup completed successfully"
        FilePath = $fullPath
        HostName = $vmhost.Name
        BackupDate = $backup.BackupDate
        FileSize = $jsonSize
    }
    
    Write-LogSuccess "Backup operation completed successfully for host: $($HostName)"
    
    # Prepare statistics for logging
    $finalStats = @{
        "Host" = $HostName
        "FileSize" = "$($jsonSize)MB"
        "NetworkComponents" = if ($IncludeNetworkConfig) { $backup.NetworkConfig.VirtualSwitches.Count + $backup.NetworkConfig.PortGroups.Count } else { 0 }
        "StorageComponents" = if ($IncludeStorageConfig) { $backup.StorageConfig.Datastores.Count + $backup.StorageConfig.StorageAdapters.Count } else { 0 }
        "Services" = if ($IncludeServices) { $backup.Services.Count } else { 0 }
        "AdvancedSettings" = if ($IncludeAdvancedSettings) { $backup.AdvancedSettings.Count } else { 0 }
    }
    
    $finalSummary = "Host $($HostName) backed up to $($fileName)"
    $scriptSuccess = $true
    
} catch {
    Write-LogCritical "Backup operation failed: $($_.Exception.Message)"
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    $result = @{
        Success = $false
        Message = "Backup failed: $($_.Exception.Message)"
        HostName = $HostName
        Error = $_.Exception.Message
    }
    
    $finalSummary = "Failed to backup host $($HostName): $($_.Exception.Message)"
    $scriptSuccess = $false
    
    # Re-throw the exception to ensure the calling process knows about the failure
    throw $_
}
finally {
    Stop-ScriptLogging -Success $scriptSuccess -Summary $finalSummary -Statistics $finalStats
    
    # Output the final result as JSON
    $result | ConvertTo-Json -Compress
}