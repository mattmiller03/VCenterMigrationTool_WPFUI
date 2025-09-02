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
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "BackupVMConfigurations"

try {
    Write-LogInfo "Starting VM configuration backup..."
    Write-LogInfo "Backup file path: $BackupFilePath"
    Write-LogInfo "Options: Settings=$IncludeSettings, Snapshots=$IncludeSnapshots, Annotations=$IncludeAnnotations, CustomAttributes=$IncludeCustomAttributes, Permissions=$IncludePermissions, Compress=$CompressOutput"
    
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
    
    # Determine which VMs to backup
    Write-LogInfo "Determining VMs to backup..." -Category "Scope"
    if ($VMNames.Count -gt 0) {
        $vms = @()
        foreach ($vmName in $VMNames) {
            try {
                $vm = Get-VM -Name $vmName -ErrorAction Stop
                $vms += $vm
                Write-LogDebug "Added VM: $vmName"
            }
            catch {
                Write-LogWarning "VM '$vmName' not found"
            }
        }
        Write-LogInfo "Backing up $($vms.Count) selected VMs" -Category "Scope"
    } 
    elseif (-not [string]::IsNullOrEmpty($ClusterName)) {
        $vms = Get-Cluster -Name $ClusterName | Get-VM
        Write-LogInfo "Backing up all VMs from cluster: $ClusterName ($($vms.Count) VMs)" -Category "Scope"
    } 
    elseif ($BackupAllVMs) {
        $vms = Get-VM
        Write-LogInfo "Backing up all VMs from vCenter ($($vms.Count) VMs)" -Category "Scope"
    }
    else {
        Write-LogCritical "No backup scope specified"
        throw "No backup scope specified"
    }
    
    if ($vms.Count -eq 0) {
        Write-LogCritical "No VMs found to backup"
        throw "No VMs found to backup"
    }
    
    # Create backup data structure
    Write-LogInfo "Creating backup data structure..."
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
    $successCount = 0
    $errorCount = 0
    
    foreach ($vm in $vms) {
        $vmCount++
        $percent = [math]::Round(($vmCount / $vms.Count) * 100, 1)
        Write-LogInfo "Processing VM: $($vm.Name) ($vmCount/$($vms.Count) - $percent%)" -Category "Progress"
        
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
                Write-LogDebug "  Collecting hardware settings for $($vm.Name)"
                
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
                            Write-LogWarning "Could not get datastore info for disk $($disk.Name)"
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
                
                Write-LogDebug "  Hardware settings collected: $($vmConfig.NetworkAdapters.Count) NICs, $($vmConfig.HardDisks.Count) disks"
            }
            
            # VM annotations and notes
            if ($IncludeAnnotations) {
                Write-LogDebug "  Collecting annotations for $($vm.Name)"
                $vmConfig.Notes = $vm.Notes
                $vmConfig.Description = $vm.Description
            }
            
            # Custom attributes
            if ($IncludeCustomAttributes) {
                Write-LogDebug "  Collecting custom attributes for $($vm.Name)"
                $vmConfig.CustomFields = @{}
                try {
                    foreach ($field in ($vm | Get-Annotation -ErrorAction SilentlyContinue)) {
                        $vmConfig.CustomFields[$field.Name] = $field.Value
                    }
                }
                catch {
                    Write-LogWarning "Could not retrieve custom attributes for $($vm.Name)"
                }
            }
            
            # Snapshot information
            if ($IncludeSnapshots) {
                Write-LogDebug "  Collecting snapshots for $($vm.Name)"
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
                    
                    if ($vmConfig.Snapshots.Count -gt 0) {
                        Write-LogDebug "  Found $($vmConfig.Snapshots.Count) snapshots"
                    }
                }
                catch {
                    Write-LogWarning "Could not retrieve snapshots for $($vm.Name)"
                }
            }
            
            # VM permissions (requires elevated privileges)
            if ($IncludePermissions) {
                Write-LogDebug "  Collecting permissions for $($vm.Name)"
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
                    
                    if ($vmConfig.Permissions.Count -gt 0) {
                        Write-LogDebug "  Found $($vmConfig.Permissions.Count) permissions"
                    }
                }
                catch {
                    Write-LogWarning "Could not retrieve permissions for $($vm.Name)"
                }
            }
            
            $backupData.VMs += $vmConfig
            $successCount++
            Write-LogSuccess "Successfully processed VM: $($vm.Name)"
        }
        catch {
            $errorCount++
            Write-LogError "Failed to backup VM $($vm.Name): $($_.Exception.Message)"
        }
    }
    
    Write-LogInfo "VM processing completed: $successCount successful, $errorCount failed" -Category "Summary"
    
    # Create backup directory if needed
    $backupDir = Split-Path -Parent $BackupFilePath
    if (-not (Test-Path $backupDir)) {
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
        Write-LogInfo "Created backup directory: $backupDir"
    }
    
    # Save backup data to JSON
    Write-LogInfo "Saving backup data to: $BackupFilePath"
    $jsonStartTime = Get-Date
    $jsonOutput = $backupData | ConvertTo-Json -Depth 15
    $jsonOutput | Out-File -FilePath $BackupFilePath -Encoding UTF8
    $jsonTime = (Get-Date) - $jsonStartTime
    Write-LogInfo "JSON serialization completed in $($jsonTime.TotalSeconds) seconds"
    
    # Compress if requested
    if ($CompressOutput) {
        Write-LogInfo "Compressing backup file..."
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
            
            Write-LogSuccess "Backup compressed to: $zipPath"
        }
        catch {
            Write-LogWarning "Compression failed: $($_.Exception.Message)"
        }
    }
    
    $fileSize = [math]::Round((Get-Item $BackupFilePath).Length / 1KB, 0)
    Write-LogSuccess "VM backup completed successfully"
    Write-LogInfo "Backup file: $BackupFilePath"
    Write-LogInfo "VMs backed up: $($backupData.VMs.Count)"
    Write-LogInfo "File size: $fileSize KB"
    
    # Create statistics for logging
    $stats = @{
        "VMsProcessed" = $vmCount
        "VMsSuccessful" = $successCount
        "VMsWithErrors" = $errorCount
        "FileSizeKB" = $fileSize
        "BackupScope" = $backupData.BackupInfo.BackupScope
        "Compressed" = $CompressOutput
    }
    
    Stop-ScriptLogging -Success $true -Summary "VM backup completed - $successCount VMs, $fileSize KB" -Statistics $stats
    
    # Return success result
    $result = @{
        Success = $true
        Message = "VM backup completed successfully"
        FilePath = $BackupFilePath
        VMCount = $backupData.VMs.Count
        FileSize = $fileSize
    }
    
    $result | ConvertTo-Json -Compress
}
catch {
    $errorMsg = "VM backup failed: $($_.Exception.Message)"
    Write-LogCritical $errorMsg
    Write-LogError "Stack trace: $($_.ScriptStackTrace)"
    
    Stop-ScriptLogging -Success $false -Summary $errorMsg
    
    # Return error result
    $result = @{
        Success = $false
        Message = $errorMsg
        Error = $_.Exception.Message
    }
    
    $result | ConvertTo-Json -Compress
    throw $_
}