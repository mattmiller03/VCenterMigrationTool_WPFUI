param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFilePath,
    
    [string[]]$VMNames = @(),
    [string]$ClusterName = '',
    [bool]$BackupAllVMs = $false,
    [bool]$IncludeSettings = $true,
    [bool]$IncludeSnapshots = $false,
    [bool]$IncludeAnnotations = $true,
    [bool]$IncludeCustomAttributes = $true,
    [bool]$IncludePermissions = $false,
    [bool]$CompressOutput = $true,
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
    Write-Log "Starting VM configuration backup..." "Info"
    
    # Import PowerCLI modules if not bypassing module check
    if (-not $BypassModuleCheck) {
        Write-Log "Importing PowerCLI modules..." "Info"
        try {
            Import-Module VMware.PowerCLI -Force -ErrorAction Stop
            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
            Write-Log "PowerCLI modules imported successfully" "Info"
        }
        catch {
            Write-Error "Failed to import PowerCLI modules: $($_.Exception.Message)"
            exit 1
        }
    }
    else {
        Write-Log "Bypassing PowerCLI module check (already confirmed installed)" "Info"
    }
    
    # Check vCenter connection
    if (-not $global:DefaultVIServer -or $global:DefaultVIServer.IsConnected -eq $false) {
        Write-Error "No active vCenter connection found. Please connect to vCenter first."
        exit 1
    }
    
    Write-Log "Connected to vCenter: $($global:DefaultVIServer.Name)" "Info"
    
    # Determine which VMs to backup
    if ($VMNames.Count -gt 0) {
        $vms = @()
        foreach ($vmName in $VMNames) {
            try {
                $vm = Get-VM -Name $vmName -ErrorAction Stop
                $vms += $vm
            }
            catch {
                Write-Log "VM '$vmName' not found" "Warning"
            }
        }
        Write-Log "Backing up $($vms.Count) selected VMs" "Info"
    } 
    elseif (-not [string]::IsNullOrEmpty($ClusterName)) {
        $vms = Get-Cluster -Name $ClusterName | Get-VM
        Write-Log "Backing up all VMs from cluster: $ClusterName ($($vms.Count) VMs)" "Info"
    } 
    elseif ($BackupAllVMs) {
        $vms = Get-VM
        Write-Log "Backing up all VMs from vCenter ($($vms.Count) VMs)" "Info"
    }
    else {
        throw "No backup scope specified"
    }
    
    if ($vms.Count -eq 0) {
        throw "No VMs found to backup"
    }
    
    # Create backup data structure
    $backupData = @{
        BackupInfo = @{
            Timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            Source = $global:DefaultVIServer.Name
            VMCount = $vms.Count
            BackupScope = if ($VMNames.Count -gt 0) { "SelectedVMs" } elseif (-not [string]::IsNullOrEmpty($ClusterName)) { "Cluster_$ClusterName" } else { "AllVMs" }
            BackupOptions = @{
                IncludeSettings = $IncludeSettings
                IncludeSnapshots = $IncludeSnapshots
                IncludeAnnotations = $IncludeAnnotations
                IncludeCustomAttributes = $IncludeCustomAttributes
                IncludePermissions = $IncludePermissions
            }
            PowerCLIVersion = (Get-Module VMware.PowerCLI).Version.ToString()
            BackupVersion = "1.0"
        }
        VMs = @()
    }
    
    # Process each VM
    $vmCount = 0
    foreach ($vm in $vms) {
        $vmCount++
        Write-Progress -Activity 'Backing up VM configurations' -Status "Processing $($vm.Name)" -PercentComplete (($vmCount / $vms.Count) * 100)
        Write-Log "Processing VM: $($vm.Name)" "Info"
        
        try {
            $vmConfig = @{
                Name = $vm.Name
                PowerState = $vm.PowerState.ToString()
                NumCpu = $vm.NumCpu
                CoresPerSocket = $vm.CoresPerSocket
                MemoryMB = $vm.MemoryMB
                ProvisionedSpaceGB = [math]::Round($vm.ProvisionedSpaceGB, 2)
                UsedSpaceGB = [math]::Round($vm.UsedSpaceGB, 2)
                VMHost = $vm.VMHost.Name
                Folder = $vm.Folder.Name
                ResourcePool = $vm.ResourcePool.Name
                GuestId = $vm.ExtensionData.Config.GuestId
                GuestFullName = $vm.ExtensionData.Config.GuestFullName
                Version = $vm.Version
                CreateDate = $vm.CreateDate
                UUID = $vm.ExtensionData.Config.Uuid
                InstanceUuid = $vm.ExtensionData.Config.InstanceUuid
                ChangeVersion = $vm.ExtensionData.Config.ChangeVersion
            }
            
            # Include detailed hardware configuration
            if ($IncludeSettings) {
                # Network adapters
                $vmConfig.NetworkAdapters = @()
                foreach ($nic in ($vm | Get-NetworkAdapter)) {
                    $nicConfig = @{
                        Name = $nic.Name
                        Type = $nic.Type.ToString()
                        NetworkName = $nic.NetworkName
                        MacAddress = $nic.MacAddress
                        WakeOnLan = $nic.WakeOnLan
                        StartConnected = $nic.StartConnected
                        Connected = $nic.Connected
                    }
                    
                    # Get distributed switch info if available
                    if ($nic.ExtensionData.Backing.Port) {
                        $nicConfig.DistributedSwitch = @{
                            PortGroup = $nic.ExtensionData.Backing.Port.PortgroupKey
                            PortKey = $nic.ExtensionData.Backing.Port.PortKey
                            SwitchUuid = $nic.ExtensionData.Backing.Port.SwitchUuid
                        }
                    }
                    
                    $vmConfig.NetworkAdapters += $nicConfig
                }
                
                # Hard disks
                $vmConfig.HardDisks = @()
                foreach ($disk in ($vm | Get-HardDisk)) {
                    $diskConfig = @{
                        Name = $disk.Name
                        CapacityGB = [math]::Round($disk.CapacityGB, 2)
                        StorageFormat = $disk.StorageFormat.ToString()
                        DiskType = $disk.DiskType.ToString()
                        Filename = $disk.Filename
                        Persistence = $disk.Persistence.ToString()
                        SCSIController = $disk.ExtensionData.ControllerKey
                        UnitNumber = $disk.ExtensionData.UnitNumber
                    }
                    
                    # Get datastore information
                    if ($disk.ExtensionData.Backing.Datastore) {
                        try {
                            $datastore = Get-View -Id $disk.ExtensionData.Backing.Datastore -ErrorAction SilentlyContinue
                            if ($datastore) {
                                $diskConfig.Datastore = $datastore.Name
                                $diskConfig.DatastoreType = $datastore.Summary.Type
                            }
                        }
                        catch {
                            Write-Log "Could not get datastore info for disk $($disk.Name)" "Warning"
                        }
                    }
                    
                    $vmConfig.HardDisks += $diskConfig
                }
                
                # CD/DVD drives
                $vmConfig.CDDrives = @()
                foreach ($cd in ($vm | Get-CDDrive)) {
                    $vmConfig.CDDrives += @{
                        Name = $cd.Name
                        IsoPath = $cd.IsoPath
                        HostDevice = $cd.HostDevice
                        StartConnected = $cd.StartConnected
                        Connected = $cd.Connected
                    }
                }
                
                # Advanced configuration
                $vmConfig.AdvancedConfig = @{
                    MemoryHotAddEnabled = $vm.ExtensionData.Config.MemoryHotAddEnabled
                    CpuHotAddEnabled = $vm.ExtensionData.Config.CpuHotAddEnabled
                    CpuHotRemoveEnabled = $vm.ExtensionData.Config.CpuHotRemoveEnabled
                    BootDelay = $vm.ExtensionData.Config.BootOptions.BootDelay
                    EnterBIOSSetup = $vm.ExtensionData.Config.BootOptions.EnterBIOSSetup
                }
            }
            
            # VM annotations and notes
            if ($IncludeAnnotations) {
                $vmConfig.Notes = $vm.Notes
                $vmConfig.Description = $vm.Description
            }
            
            # Custom attributes
            if ($IncludeCustomAttributes) {
                $vmConfig.CustomFields = @{}
                try {
                    foreach ($field in ($vm | Get-Annotation -ErrorAction SilentlyContinue)) {
                        $vmConfig.CustomFields[$field.Name] = $field.Value
                    }
                }
                catch {
                    Write-Log "Could not retrieve custom attributes for $($vm.Name)" "Warning"
                }
            }
            
            # Snapshot information
            if ($IncludeSnapshots) {
                $vmConfig.Snapshots = @()
                try {
                    foreach ($snapshot in ($vm | Get-Snapshot -ErrorAction SilentlyContinue)) {
                        $snapConfig = @{
                            Name = $snapshot.Name
                            Description = $snapshot.Description
                            Created = $snapshot.Created
                            SizeGB = [math]::Round($snapshot.SizeGB, 2)
                            IsCurrent = $snapshot.IsCurrent
                            PowerState = $snapshot.PowerState.ToString()
                            Quiesced = $snapshot.Quiesced
                        }
                        
                        if ($snapshot.ParentSnapshot) {
                            $snapConfig.ParentSnapshot = $snapshot.ParentSnapshot.Name
                        }
                        
                        $vmConfig.Snapshots += $snapConfig
                    }
                }
                catch {
                    Write-Log "Could not retrieve snapshots for $($vm.Name)" "Warning"
                }
            }
            
            # VM permissions (requires elevated privileges)
            if ($IncludePermissions) {
                $vmConfig.Permissions = @()
                try {
                    $permissions = Get-VIPermission -Entity $vm -ErrorAction SilentlyContinue
                    foreach ($perm in $permissions) {
                        $vmConfig.Permissions += @{
                            Principal = $perm.Principal
                            Role = $perm.Role
                            IsGroup = $perm.IsGroup
                            Propagate = $perm.Propagate
                        }
                    }
                }
                catch {
                    Write-Log "Could not retrieve permissions for $($vm.Name)" "Warning"
                }
            }
            
            $backupData.VMs += $vmConfig
            Write-Log "Successfully processed VM: $($vm.Name)" "Info"
        }
        catch {
            Write-Log "Failed to backup VM $($vm.Name): $($_.Exception.Message)" "Error"
        }
    }
    
    # Create backup directory if needed
    $backupDir = Split-Path -Parent $BackupFilePath
    if (-not (Test-Path $backupDir)) {
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
        Write-Log "Created backup directory: $backupDir" "Info"
    }
    
    # Save backup data to JSON
    Write-Log "Saving backup data to: $BackupFilePath" "Info"
    $jsonOutput = $backupData | ConvertTo-Json -Depth 15
    $jsonOutput | Out-File -FilePath $BackupFilePath -Encoding UTF8
    
    # Compress if requested
    if ($CompressOutput) {
        Write-Log "Compressing backup file..." "Info"
        $zipPath = [System.IO.Path]::ChangeExtension($BackupFilePath, '.zip')
        
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $compression = [System.IO.Compression.CompressionLevel]::Optimal
            $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
            
            $entryName = [System.IO.Path]::GetFileName($BackupFilePath)
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $BackupFilePath, $entryName, $compression) | Out-Null
            $zip.Dispose()
            
            # Remove original JSON file
            Remove-Item $BackupFilePath -Force
            $BackupFilePath = $zipPath
            
            Write-Log "Backup compressed to: $zipPath" "Info"
        }
        catch {
            Write-Log "Compression failed: $($_.Exception.Message)" "Warning"
        }
    }
    
    $fileSize = [math]::Round((Get-Item $BackupFilePath).Length / 1KB, 0)
    Write-Host "SUCCESS: VM backup completed"
    Write-Host "Backup file: $BackupFilePath"
    Write-Host "VMs backed up: $($backupData.VMs.Count)"
    Write-Host "File size: $fileSize KB"
    Write-Log "VM backup completed successfully - $($backupData.VMs.Count) VMs, $fileSize KB" "Info"
}
catch {
    $errorMsg = "VM backup failed: $($_.Exception.Message)"
    Write-Log $errorMsg "Error"
    Write-Error $errorMsg
    exit 1
}
finally {
    Write-Progress -Activity 'Backing up VM configurations' -Completed
}