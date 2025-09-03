# Export-vCenterConfig.ps1 - Updated with integrated logging

param(
    [Parameter(Mandatory=$true)]
    [string]$VCenterServer,

    [Parameter(Mandatory=$true)]
    [string]$Username,

    [Parameter(Mandatory=$true)]
    [string]$Password,
    
    [Parameter(Mandatory=$true)]
    [string]$ExportPath,
    
    [bool]$BypassModuleCheck = $false
)

# Import logging functions
. "$PSScriptRoot\Write-ScriptLog.ps1"

# Start logging
Start-ScriptLogging -ScriptName "Export-vCenterConfig"

try {
    Write-LogInfo "Starting vCenter configuration export"
    Write-LogInfo "Target vCenter: $VCenterServer" -Category "Target"
    Write-LogInfo "Export path: $ExportPath" -Category "Export"
    Write-LogInfo "User: $Username" -Category "Connection"
    
    # PowerCLI configuration (module management handled by service layer)
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
    
    # Create secure credential
    Write-LogDebug "Creating secure credentials..."
    $securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
    $credential = New-Object System.Management.Automation.PSCredential($Username, $securePassword)
    $Password = $null # Clear from memory
    
    # Attempt to connect to vCenter
    Write-LogInfo "Attempting to connect to $VCenterServer..." -Category "Connection"
    $connectionStartTime = Get-Date
    
    try {
        $connection = Connect-VIServer -Server $VCenterServer -Credential $credential -Force -ErrorAction Stop
        $connectionTime = (Get-Date) - $connectionStartTime
        Write-LogSuccess "Successfully connected to $VCenterServer in $($connectionTime.TotalSeconds) seconds" -Category "Connection"
        
        Write-LogInfo "Connection Details:" -Category "Connection"
        Write-LogInfo "  Server: $($connection.Name)"
        Write-LogInfo "  Version: $($connection.Version)"
        Write-LogInfo "  Build: $($connection.Build)"
        Write-LogInfo "  User: $($connection.User)"
    }
    catch {
        Write-LogCritical "Failed to connect to vCenter: $($_.Exception.Message)" -Category "Connection"
        throw $_
    }

    # Create export directory if it doesn't exist
    if (-not (Test-Path -Path $ExportPath)) {
        Write-LogInfo "Creating export directory: $ExportPath"
        try {
            New-Item -Path $ExportPath -ItemType Directory -Force | Out-Null
            Write-LogSuccess "Export directory created successfully"
        }
        catch {
            Write-LogCritical "Failed to create export directory: $($_.Exception.Message)"
            throw $_
        }
    }
    else {
        Write-LogInfo "Export directory already exists: $ExportPath"
    }
    
    # Initialize export data structure
    $exportData = @{
        ExportInfo = @{
            Timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            Source = $VCenterServer
            vCenterVersion = $connection.Version
            vCenterBuild = $connection.Build
            ExportedBy = $Username
            ExportPath = $ExportPath
        }
        VirtualSwitches = @()
        DistributedSwitches = @()
        PortGroups = @()
        Clusters = @()
        Datacenters = @()
        Datastores = @()
        Networks = @()
    }
    
    Write-LogInfo "Exporting vCenter configuration components..." -Category "Export"
    
    # Export Datacenters
    Write-LogInfo "Exporting Datacenter configurations..." -Category "Datacenters"
    try {
        $datacenters = Get-Datacenter -ErrorAction Stop
        Write-LogInfo "Found $($datacenters.Count) datacenters"
        
        foreach ($dc in $datacenters) {
            Write-LogDebug "Processing datacenter: $($dc.Name)"
            $exportData.Datacenters += @{
                Name = $dc.Name
                Id = $dc.Id
                Uid = $dc.Uid
            }
        }
        Write-LogSuccess "Datacenter configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export datacenters: $($_.Exception.Message)"
    }
    
    # Export Clusters
    Write-LogInfo "Exporting Cluster configurations..." -Category "Clusters"
    try {
        $clusters = Get-Cluster -ErrorAction Stop
        Write-LogInfo "Found $($clusters.Count) clusters"
        
        foreach ($cluster in $clusters) {
            Write-LogDebug "Processing cluster: $($cluster.Name)"
            $exportData.Clusters += @{
                Name = $cluster.Name
                Id = $cluster.Id
                HAEnabled = $cluster.HAEnabled
                HAFailoverLevel = $cluster.HAFailoverLevel
                DrsEnabled = $cluster.DrsEnabled
                DrsAutomationLevel = $cluster.DrsAutomationLevel
                VsanEnabled = $cluster.VsanEnabled
                EVCMode = $cluster.EVCMode
            }
        }
        Write-LogSuccess "Cluster configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export clusters: $($_.Exception.Message)"
    }
    
    # Export Virtual Switches (Standard)
    Write-LogInfo "Exporting Virtual Switch configurations..." -Category "vSwitches"
    try {
        $vSwitches = Get-VirtualSwitch -Standard -ErrorAction Stop
        Write-LogInfo "Found $($vSwitches.Count) standard virtual switches"
        
        foreach ($vSwitch in $vSwitches) {
            Write-LogDebug "Processing vSwitch: $($vSwitch.Name)"
            $exportData.VirtualSwitches += @{
                Name = $vSwitch.Name
                VMHost = $vSwitch.VMHost.Name
                NumPorts = $vSwitch.NumPorts
                Mtu = $vSwitch.Mtu
                Nic = $vSwitch.Nic -join ","
                Key = $vSwitch.Key
            }
        }
        Write-LogSuccess "Virtual Switch configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export virtual switches: $($_.Exception.Message)"
    }
    
    # Export Distributed Switches
    Write-LogInfo "Exporting Distributed Switch configurations..." -Category "dvSwitches"
    try {
        $dvSwitches = Get-VDSwitch -ErrorAction Stop
        Write-LogInfo "Found $($dvSwitches.Count) distributed virtual switches"
        
        foreach ($dvSwitch in $dvSwitches) {
            Write-LogDebug "Processing dvSwitch: $($dvSwitch.Name)"
            $exportData.DistributedSwitches += @{
                Name = $dvSwitch.Name
                Id = $dvSwitch.Id
                Datacenter = $dvSwitch.Datacenter.Name
                Version = $dvSwitch.Version
                NumUplinkPorts = $dvSwitch.NumUplinkPorts
                Mtu = $dvSwitch.Mtu
                LinkDiscoveryProtocol = $dvSwitch.LinkDiscoveryProtocol
                LinkDiscoveryProtocolOperation = $dvSwitch.LinkDiscoveryProtocolOperation
            }
        }
        Write-LogSuccess "Distributed Switch configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export distributed switches: $($_.Exception.Message)"
    }
    
    # Export Port Groups
    Write-LogInfo "Exporting Port Group configurations..." -Category "PortGroups"
    try {
        $portGroups = Get-VirtualPortGroup -ErrorAction Stop
        Write-LogInfo "Found $($portGroups.Count) port groups"
        
        foreach ($pg in $portGroups) {
            Write-LogDebug "Processing port group: $($pg.Name)"
            $exportData.PortGroups += @{
                Name = $pg.Name
                VirtualSwitchName = $pg.VirtualSwitchName
                VLanId = $pg.VLanId
                Key = $pg.Key
            }
        }
        Write-LogSuccess "Port Group configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export port groups: $($_.Exception.Message)"
    }
    
    # Export Datastores
    Write-LogInfo "Exporting Datastore configurations..." -Category "Datastores"
    try {
        $datastores = Get-Datastore -ErrorAction Stop
        Write-LogInfo "Found $($datastores.Count) datastores"
        
        foreach ($ds in $datastores) {
            Write-LogDebug "Processing datastore: $($ds.Name)"
            $exportData.Datastores += @{
                Name = $ds.Name
                Id = $ds.Id
                CapacityGB = [math]::Round($ds.CapacityGB, 2)
                FreeSpaceGB = [math]::Round($ds.FreeSpaceGB, 2)
                Type = $ds.Type
                FileSystemVersion = $ds.FileSystemVersion
                Accessible = $ds.Accessible
                State = $ds.State
                Datacenter = $ds.Datacenter.Name
            }
        }
        Write-LogSuccess "Datastore configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export datastores: $($_.Exception.Message)"
    }
    
    # Export Networks
    Write-LogInfo "Exporting Network configurations..." -Category "Networks"
    try {
        $networks = Get-VirtualNetwork -ErrorAction Stop
        Write-LogInfo "Found $($networks.Count) networks"
        
        foreach ($network in $networks) {
            Write-LogDebug "Processing network: $($network.Name)"
            $exportData.Networks += @{
                Name = $network.Name
                Id = $network.Id
                VLanId = $network.VLanId
            }
        }
        Write-LogSuccess "Network configurations exported successfully"
    }
    catch {
        Write-LogError "Failed to export networks: $($_.Exception.Message)"
    }
    
    # Generate export filename with timestamp
    $timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $fileName = "vCenter_Config_Export_$timestamp.json"
    $fullPath = Join-Path $ExportPath $fileName
    
    # Convert to JSON and save
    Write-LogInfo "Saving configuration export to: $fullPath" -Category "Save"
    try {
        $jsonStartTime = Get-Date
        $jsonContent = $exportData | ConvertTo-Json -Depth 10
        $jsonSize = [math]::Round($jsonContent.Length / 1KB, 2)
        
        $jsonContent | Out-File -FilePath $fullPath -Encoding UTF8
        
        $jsonTime = (Get-Date) - $jsonStartTime
        Write-LogSuccess "Export saved successfully in $($jsonTime.TotalSeconds) seconds" -Category "Save"
        Write-LogInfo "  File size: ${jsonSize}KB"
        
        # Verify file was created
        if (Test-Path $fullPath) {
            $fileInfo = Get-Item $fullPath
            Write-LogInfo "  File created: $($fileInfo.CreationTime)"
            Write-LogInfo "  Full path: $($fileInfo.FullName)"
        }
    }
    catch {
        Write-LogCritical "Failed to save export file: $($_.Exception.Message)" -Category "Save"
        throw $_
    }
    
    # Create summary statistics
    $stats = @{
        "vCenterServer" = $VCenterServer
        "vCenterVersion" = $connection.Version
        "FileSizeKB" = $jsonSize
        "ComponentCounts" = @{
            "Datacenters" = $exportData.Datacenters.Count
            "Clusters" = $exportData.Clusters.Count
            "VirtualSwitches" = $exportData.VirtualSwitches.Count
            "DistributedSwitches" = $exportData.DistributedSwitches.Count
            "PortGroups" = $exportData.PortGroups.Count
            "Datastores" = $exportData.Datastores.Count
            "Networks" = $exportData.Networks.Count
        }
        "ExportTimeSeconds" = [math]::Round($jsonTime.TotalSeconds, 2)
    }
    
    Write-LogSuccess "vCenter configuration export completed successfully"
    Write-LogInfo "Export file: $fileName"
    Write-LogInfo "Components exported: Datacenters($($exportData.Datacenters.Count)), Clusters($($exportData.Clusters.Count)), vSwitches($($exportData.VirtualSwitches.Count)), dvSwitches($($exportData.DistributedSwitches.Count)), PortGroups($($exportData.PortGroups.Count)), Datastores($($exportData.Datastores.Count)), Networks($($exportData.Networks.Count))"
    
    Stop-ScriptLogging -Success $true -Summary "vCenter config exported to $fileName - $($exportData.Datacenters.Count + $exportData.Clusters.Count + $exportData.VirtualSwitches.Count + $exportData.DistributedSwitches.Count + $exportData.PortGroups.Count + $exportData.Datastores.Count + $exportData.Networks.Count) components" -Statistics $stats
    
    # Return success result
    $result = @{
        Success = $true
        Message = "vCenter configuration export completed successfully"
        FilePath = $fullPath
        FileName = $fileName
        ComponentCount = $exportData.Datacenters.Count + $exportData.Clusters.Count + $exportData.VirtualSwitches.Count + $exportData.DistributedSwitches.Count + $exportData.PortGroups.Count + $exportData.Datastores.Count + $exportData.Networks.Count
        FileSize = $jsonSize
    }
    
    $result | ConvertTo-Json -Compress
}
catch {
    $errorMsg = "vCenter configuration export failed: $($_.Exception.Message)"
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
finally {
    # Disconnect from vCenter
    if ($global:DefaultVIServer -and $global:DefaultVIServer.IsConnected) {
        try {
            Write-LogInfo "Disconnecting from vCenter..." -Category "Cleanup"
            # DISCONNECT REMOVED - Using persistent connections managed by application
            Write-LogSuccess "Disconnected from vCenter successfully" -Category "Cleanup"
        }
        catch {
            Write-LogWarning "Failed to disconnect from vCenter: $($_.Exception.Message)" -Category "Cleanup"
        }
    }
}