using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;

namespace VCenterMigrationTool.ViewModels;

public partial class EsxiHostsViewModel : ObservableObject
    {
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly ConfigurationService _configurationService;
        private readonly ILogger<EsxiHostsViewModel> _logger;


        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _sourceClusters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _targetClusters = new();

        [ObservableProperty]
        private ClusterInfo? _selectedSourceCluster;

        [ObservableProperty]
        private ClusterInfo? _selectedTargetCluster;

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _selectedSourceHosts = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _availableTargetHosts = new();

        [ObservableProperty]
        private string _migrationStatus = "Ready";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _loadingMessage = "";

        [ObservableProperty]
        private string _sourceConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _targetConnectionStatus = "Not connected";

        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        // Operation mode flags
        [ObservableProperty]
        private bool _isMigrationMode = true;

        [ObservableProperty]
        private bool _isBackupMode = false;
        
        [ObservableProperty]
        private bool _isMigrating;

        [ObservableProperty]
        private bool _isBackingUp;

        [ObservableProperty]
        private string _migrationProgress = "";

        [ObservableProperty]
        private string _backupProgress = "";


    public EsxiHostsViewModel (
            PersistentExternalConnectionService persistentConnectionService,
            SharedConnectionService sharedConnectionService,
            ConfigurationService configurationService,
            ILogger<EsxiHostsViewModel> logger)
        {
            _persistentConnectionService = persistentConnectionService;
            _sharedConnectionService = sharedConnectionService;
            _configurationService = configurationService;
            _logger = logger;
        }

    /// <summary>
    /// Initialize the view model and load data
    /// </summary>
    public async Task InitializeAsync ()
        {
        await CheckConnectionsAndLoadData();
        }

    /// <summary>
    /// Check connections and load cluster/host data
    /// </summary>
    private async Task CheckConnectionsAndLoadData ()
        {
        IsLoading = true;
        LoadingMessage = "Checking connections...";

        try
            {
            // Check source connection
            var sourceConnected = await _persistentConnectionService.IsConnectedAsync("source");
            if (sourceConnected && _sharedConnectionService.SourceConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("source");
                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ {_sharedConnectionService.SourceConnection.ServerAddress}";
                _logger.LogInformation("Source vCenter connected: {Server}", _sharedConnectionService.SourceConnection.ServerAddress);

                // Load source data
                await LoadSourceClusters();
                }
            else
                {
                IsSourceConnected = false;
                SourceConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Source vCenter not connected");
                }

            // Check target connection (optional for migration mode)
            var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");
            if (targetConnected && _sharedConnectionService.TargetConnection != null)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("target");
                IsTargetConnected = true;
                TargetConnectionStatus = $"✅ {_sharedConnectionService.TargetConnection.ServerAddress}";
                _logger.LogInformation("Target vCenter connected: {Server}", _sharedConnectionService.TargetConnection.ServerAddress);

                // Load target data
                await LoadTargetClusters();
                }
            else
                {
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Not connected";
                _logger.LogWarning("Target vCenter not connected");
                }

            // Update status based on what's connected
            UpdateOperationStatus();
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error checking connections");
            MigrationStatus = $"❌ Error: {ex.Message}";
            }
        finally
            {
            IsLoading = false;
            LoadingMessage = "";
            }
        }


    private void UpdateOperationStatus ()
    {
        if (!IsSourceConnected)
        {
            MigrationStatus = "⚠️ Please connect to source vCenter from the Dashboard";
        }
        else if (IsMigrationMode && !IsTargetConnected)
        {
            MigrationStatus = "ℹ️ Source connected - Backup operations available. Connect target for migration.";
            // Switch to backup mode if only source is connected
            IsBackupMode = true;
            IsMigrationMode = false;
        }
        else if (IsSourceConnected && IsTargetConnected)
        {
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            var targetHostCount = TargetClusters.Sum(c => c.HostCount);
            MigrationStatus = $"✅ Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts • Target: {TargetClusters.Count} clusters, {targetHostCount} hosts";
            // Enable both modes
            IsMigrationMode = true;
            IsBackupMode = true;
        }
        else
        {
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            MigrationStatus = $"✅ Backup Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts";
        }
    }

    /// <summary>
    /// Load clusters and hosts from both vCenters
    /// </summary>
    private async Task LoadClustersAndHosts ()
        {
        LoadingMessage = "Loading clusters and hosts...";

        try
            {
            // Load source clusters and hosts
            _logger.LogInformation("Loading source clusters and hosts...");
            var sourceClustersTask = LoadSourceClusters();

            // Load target clusters and hosts
            _logger.LogInformation("Loading target clusters and hosts...");
            var targetClustersTask = LoadTargetClusters();

            await Task.WhenAll(sourceClustersTask, targetClustersTask);

            // Format the status message with proper spacing
            var sourceHostCount = SourceClusters.Sum(c => c.HostCount);
            var targetHostCount = TargetClusters.Sum(c => c.HostCount);

            MigrationStatus = $"Ready • Source: {SourceClusters.Count} clusters, {sourceHostCount} hosts • Target: {TargetClusters.Count} clusters, {targetHostCount} hosts";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading clusters and hosts");
            MigrationStatus = $"❌ Error loading data: {ex.Message}";
            }
        }

    /// <summary>
    /// Load source clusters and their hosts
    /// </summary>
    private async Task LoadSourceClusters ()
        {
        try
            {
            var script = @"
                $clusters = Get-Cluster -ErrorAction SilentlyContinue
                $result = @()
                
                foreach ($cluster in $clusters) {
                    $hosts = Get-VMHost -Location $cluster -ErrorAction SilentlyContinue
                    
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = $hosts.Count
                        TotalCpuGhz = [math]::Round(($hosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($hosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $hosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $clusterInfo.Hosts += $hostInfo
                    }
                    
                    $result += $clusterInfo
                }
                
                # Also get standalone hosts (not in any cluster)
                $standaloneHosts = Get-VMHost -ErrorAction SilentlyContinue | Where-Object { $_.Parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Cluster] }
                
                if ($standaloneHosts.Count -gt 0) {
                    $standaloneCluster = @{
                        Name = 'Standalone Hosts'
                        Id = 'standalone'
                        HostCount = $standaloneHosts.Count
                        TotalCpuGhz = [math]::Round(($standaloneHosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($standaloneHosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $standaloneHosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $standaloneCluster.Hosts += $hostInfo
                    }
                    
                    $result += $standaloneCluster
                }
                
                $result | ConvertTo-Json -Depth 10
            ";

            var result = await _persistentConnectionService.ExecuteCommandAsync("source", script);

            if (result.StartsWith("ERROR:"))
                {
                _logger.LogError("Failed to load source clusters: {Error}", result);
                return;
                }

            // Parse the JSON result
            var clusters = JsonSerializer.Deserialize<List<dynamic>>(result);

            SourceClusters.Clear();

            if (clusters != null)
                {
                foreach (var cluster in clusters)
                    {
                    var clusterInfo = new ClusterInfo
                        {
                        Name = cluster.GetProperty("Name").GetString(),
                        Id = cluster.GetProperty("Id").GetString(),
                        HostCount = cluster.GetProperty("HostCount").GetInt32(),
                        TotalCpuGhz = cluster.GetProperty("TotalCpuGhz").GetDouble(),
                        TotalMemoryGB = cluster.GetProperty("TotalMemoryGB").GetDouble()
                        };

                    var hosts = cluster.GetProperty("Hosts").EnumerateArray();
                    foreach (var host in hosts)
                        {
                        var esxiHost = new EsxiHost
                            {
                            Name = host.GetProperty("Name").GetString(),
                            Id = host.GetProperty("Id").GetString(),
                            ClusterName = clusterInfo.Name,
                            ConnectionState = host.GetProperty("ConnectionState").GetString(),
                            PowerState = host.GetProperty("PowerState").GetString(),
                            CpuCores = host.GetProperty("CpuCores").GetInt32(),
                            CpuMhz = host.GetProperty("CpuMhz").GetInt32(),
                            MemoryGB = host.GetProperty("MemoryGB").GetDouble(),
                            Version = host.GetProperty("Version").GetString(),
                            Build = host.GetProperty("Build").GetString(),
                            Model = host.GetProperty("Model").GetString(),
                            Vendor = host.GetProperty("Vendor").GetString(),
                            VmCount = host.GetProperty("VMs").GetInt32()
                            };
                        clusterInfo.Hosts.Add(esxiHost);
                        }

                    SourceClusters.Add(clusterInfo);
                    SubscribeToHostSelectionEvents(SourceClusters);
                    }

                _logger.LogInformation("Loaded {Count} source clusters", SourceClusters.Count);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading source clusters");
            }
        }

    /// <summary>
    /// Load target clusters and their hosts
    /// </summary>
    private async Task LoadTargetClusters ()
        {
        try
            {
            // Use the same script as source but on target connection
            var script = @"
                $clusters = Get-Cluster -ErrorAction SilentlyContinue
                $result = @()
                
                foreach ($cluster in $clusters) {
                    $hosts = Get-VMHost -Location $cluster -ErrorAction SilentlyContinue
                    
                    $clusterInfo = @{
                        Name = $cluster.Name
                        Id = $cluster.Id
                        HostCount = $hosts.Count
                        TotalCpuGhz = [math]::Round(($hosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($hosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $hosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $clusterInfo.Hosts += $hostInfo
                    }
                    
                    $result += $clusterInfo
                }
                
                # Also get standalone hosts
                $standaloneHosts = Get-VMHost -ErrorAction SilentlyContinue | Where-Object { $_.Parent -isnot [VMware.VimAutomation.ViCore.Types.V1.Inventory.Cluster] }
                
                if ($standaloneHosts.Count -gt 0) {
                    $standaloneCluster = @{
                        Name = 'Standalone Hosts'
                        Id = 'standalone'
                        HostCount = $standaloneHosts.Count
                        TotalCpuGhz = [math]::Round(($standaloneHosts | Measure-Object -Property CpuTotalMhz -Sum).Sum / 1000, 2)
                        TotalMemoryGB = [math]::Round(($standaloneHosts | Measure-Object -Property MemoryTotalGB -Sum).Sum, 2)
                        Hosts = @()
                    }
                    
                    foreach ($vmhost in $standaloneHosts) {
                        $hostInfo = @{
                            Name = $vmhost.Name
                            Id = $vmhost.Id
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Vendor = $vmhost.Manufacturer
                            VMs = (Get-VM -Location $vmhost -ErrorAction SilentlyContinue).Count
                        }
                        $standaloneCluster.Hosts += $hostInfo
                    }
                    
                    $result += $standaloneCluster
                }
                
                $result | ConvertTo-Json -Depth 10
            ";

            var result = await _persistentConnectionService.ExecuteCommandAsync("target", script);

            if (result.StartsWith("ERROR:"))
                {
                _logger.LogError("Failed to load target clusters: {Error}", result);
                return;
                }

            // Parse the JSON result
            var clusters = JsonSerializer.Deserialize<List<dynamic>>(result);

            TargetClusters.Clear();

            if (clusters != null)
                {
                foreach (var cluster in clusters)
                    {
                    var clusterInfo = new ClusterInfo
                        {
                        Name = cluster.GetProperty("Name").GetString(),
                        Id = cluster.GetProperty("Id").GetString(),
                        HostCount = cluster.GetProperty("HostCount").GetInt32(),
                        TotalCpuGhz = cluster.GetProperty("TotalCpuGhz").GetDouble(),
                        TotalMemoryGB = cluster.GetProperty("TotalMemoryGB").GetDouble()
                        };

                    var hosts = cluster.GetProperty("Hosts").EnumerateArray();
                    foreach (var host in hosts)
                        {
                        var esxiHost = new EsxiHost
                            {
                            Name = host.GetProperty("Name").GetString(),
                            Id = host.GetProperty("Id").GetString(),
                            ClusterName = clusterInfo.Name,
                            ConnectionState = host.GetProperty("ConnectionState").GetString(),
                            PowerState = host.GetProperty("PowerState").GetString(),
                            CpuCores = host.GetProperty("CpuCores").GetInt32(),
                            CpuMhz = host.GetProperty("CpuMhz").GetInt32(),
                            MemoryGB = host.GetProperty("MemoryGB").GetDouble(),
                            Version = host.GetProperty("Version").GetString(),
                            Build = host.GetProperty("Build").GetString(),
                            Model = host.GetProperty("Model").GetString(),
                            Vendor = host.GetProperty("Vendor").GetString(),
                            VmCount = host.GetProperty("VMs").GetInt32()
                            };
                        clusterInfo.Hosts.Add(esxiHost);
                        }

                    TargetClusters.Add(clusterInfo);
                    }

                _logger.LogInformation("Loaded {Count} target clusters", TargetClusters.Count);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading target clusters");
            }
        }

    [RelayCommand]
    private async Task RefreshData ()
    {
        // Use the general IsLoading for refresh operations
        await CheckConnectionsAndLoadData();
    }

    [RelayCommand]
    private void SelectAllSourceHosts ()
    {
        if (SelectedSourceCluster != null)
        {
            foreach (var host in SelectedSourceCluster.Hosts)
            {
                host.IsSelected = true; // This will trigger the selection event
            }
        }
    }
    [RelayCommand]
    private async Task BackupSelectedHosts ()
        {
        if (SelectedSourceHosts.Count == 0)
            {
            MigrationStatus = "⚠️ Please select hosts to backup";
            return;
            }

        IsBackingUp = true;
        BackupProgress = $"Starting backup of {SelectedSourceHosts.Count} hosts...";

        try
            {
            var backupPath = Path.Combine(
                _configurationService.GetConfiguration().ExportPath ?? "Backups",
                $"ESXi_Backup_{DateTime.Now:yyyyMMdd_HHmmss}"
            );

            Directory.CreateDirectory(backupPath);

            int completed = 0;
            foreach (var host in SelectedSourceHosts)
                {
                completed++;
                BackupProgress = $"Backing up {host.Name} ({completed}/{SelectedSourceHosts.Count})...";

                var backupScript = $@"
                try {{
                    $vmhost = Get-VMHost -Name '{host.Name}' -ErrorAction Stop
                    
                    # Network Configuration - Convert to simple objects
                    $virtualSwitches = @()
                    $vSwitches = Get-VirtualSwitch -VMHost $vmhost -ErrorAction SilentlyContinue
                    foreach ($vSwitch in $vSwitches) {{
                        $virtualSwitches += @{{
                            Name = $vSwitch.Name
                            NumPorts = $vSwitch.NumPorts
                            NumPortsAvailable = $vSwitch.NumPortsAvailable
                            Mtu = $vSwitch.Mtu
                            Nic = ($vSwitch.Nic -join ',')
                        }}
                    }}
                    
                    # Port Groups
                    $portGroups = @()
                    $pgs = Get-VirtualPortGroup -VMHost $vmhost -ErrorAction SilentlyContinue
                    foreach ($pg in $pgs) {{
                        $portGroups += @{{
                            Name = $pg.Name
                            VirtualSwitchName = $pg.VirtualSwitchName
                            VLanId = $pg.VLanId
                        }}
                    }}
                    
                    # VMKernel Adapters
                    $vmkernelAdapters = @()
                    $vmks = Get-VMHostNetworkAdapter -VMHost $vmhost -VMKernel -ErrorAction SilentlyContinue
                    foreach ($vmk in $vmks) {{
                        $vmkernelAdapters += @{{
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
                        }}
                    }}
                    
                    # Storage Configuration
                    $datastores = @()
                    $ds = Get-Datastore -VMHost $vmhost -ErrorAction SilentlyContinue
                    foreach ($datastore in $ds) {{
                        $datastores += @{{
                            Name = $datastore.Name
                            CapacityGB = [math]::Round($datastore.CapacityGB, 2)
                            FreeSpaceGB = [math]::Round($datastore.FreeSpaceGB, 2)
                            Type = $datastore.Type
                            FileSystemVersion = $datastore.FileSystemVersion
                            Accessible = $datastore.Accessible
                            State = $datastore.State.ToString()
                        }}
                    }}
                    
                    # Storage Adapters
                    $storageAdapters = @()
                    $hbas = Get-VMHostHba -VMHost $vmhost -ErrorAction SilentlyContinue
                    foreach ($hba in $hbas) {{
                        $storageAdapters += @{{
                            Device = $hba.Device
                            Type = $hba.Type
                            Model = $hba.Model
                            Driver = $hba.Driver
                            Status = $hba.Status.ToString()
                        }}
                    }}
                    
                    # Advanced Settings
                    $advancedSettings = @{{}}
                    $advSettings = Get-AdvancedSetting -Entity $vmhost -ErrorAction SilentlyContinue
                    foreach ($setting in $advSettings) {{
                        $advancedSettings[$setting.Name] = $setting.Value
                    }}
                    
                    # Services
                    $services = @()
                    $hostServices = Get-VMHostService -VMHost $vmhost -ErrorAction SilentlyContinue
                    foreach ($service in $hostServices) {{
                        $services += @{{
                            Key = $service.Key
                            Label = $service.Label
                            Running = $service.Running
                            Required = $service.Required
                            Policy = $service.Policy.ToString()
                        }}
                    }}
                    
                    # NTP Servers
                    $ntpServers = @()
                    $ntps = Get-VMHostNtpServer -VMHost $vmhost -ErrorAction SilentlyContinue
                    if ($ntps) {{
                        $ntpServers = $ntps
                    }}
                    
                    # DNS Configuration
                    $dnsConfig = @{{}}
                    try {{
                        $network = Get-VMHostNetwork -VMHost $vmhost -ErrorAction SilentlyContinue
                        if ($network) {{
                            $dnsConfig = @{{
                                DnsAddress = $network.DnsAddress
                                SearchDomain = $network.SearchDomain
                                HostName = $network.HostName
                                DomainName = $network.DomainName
                            }}
                        }}
                    }} catch {{
                        $dnsConfig = @{{ Error = 'Unable to retrieve DNS configuration' }}
                    }}
                    
                    # Syslog Configuration
                    $syslogConfig = @{{}}
                    try {{
                        $syslogSetting = Get-AdvancedSetting -Entity $vmhost -Name 'Syslog.global.logHost' -ErrorAction SilentlyContinue
                        if ($syslogSetting) {{
                            $syslogConfig = @{{
                                LogHost = $syslogSetting.Value
                            }}
                        }}
                    }} catch {{
                        $syslogConfig = @{{ LogHost = '' }}
                    }}
                    
                    # Create the final configuration object
                    $hostConfig = @{{
                        HostInfo = @{{
                            Name = $vmhost.Name
                            Version = $vmhost.Version
                            Build = $vmhost.Build
                            Model = $vmhost.Model
                            Manufacturer = $vmhost.Manufacturer
                            ProcessorType = $vmhost.ProcessorType
                            ConnectionState = $vmhost.ConnectionState.ToString()
                            PowerState = $vmhost.PowerState.ToString()
                            CpuCores = $vmhost.NumCpu
                            CpuMhz = $vmhost.CpuTotalMhz
                            MemoryGB = [math]::Round($vmhost.MemoryTotalGB, 2)
                        }}
                        NetworkConfiguration = @{{
                            VirtualSwitches = $virtualSwitches
                            PortGroups = $portGroups
                            VMKernelAdapters = $vmkernelAdapters
                            DnsConfiguration = $dnsConfig
                        }}
                        StorageConfiguration = @{{
                            Datastores = $datastores
                            StorageAdapters = $storageAdapters
                        }}
                        AdvancedSettings = $advancedSettings
                        Services = $services
                        NtpServers = $ntpServers
                        SyslogConfiguration = $syslogConfig
                        BackupMetadata = @{{
                            BackupDate = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
                            BackupUser = $env:USERNAME
                            PowerCLIVersion = (Get-Module VMware.PowerCLI).Version.ToString()
                            ScriptVersion = '2.0'
                        }}
                    }}
                    
                    # Convert to JSON with error handling
                    $json = $hostConfig | ConvertTo-Json -Depth 15 -Compress
                    Write-Output $json
                    
                }} catch {{
                    Write-Output ""ERROR: $($_.Exception.Message)""
                }}
            ";

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", backupScript);

                if (!result.StartsWith("ERROR:"))
                    {
                    try
                        {
                        // Validate JSON before saving
                        var testJson = JsonSerializer.Deserialize<JsonElement>(result);

                        var fileName = Path.Combine(backupPath, $"{host.Name}_config_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                        await File.WriteAllTextAsync(fileName, result);

                        _logger.LogInformation("Successfully backed up host {Host} to {File}", host.Name, fileName);
                        }
                    catch (JsonException ex)
                        {
                        _logger.LogError("JSON validation failed for host {Host}: {Error}", host.Name, ex.Message);

                        // Save the raw result for debugging
                        var debugFileName = Path.Combine(backupPath, $"{host.Name}_debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                        await File.WriteAllTextAsync(debugFileName, result);

                        throw new InvalidOperationException($"JSON validation failed for host {host.Name}: {ex.Message}");
                        }
                    }
                else
                    {
                    _logger.LogError("PowerShell error for host {Host}: {Error}", host.Name, result);
                    throw new InvalidOperationException($"PowerShell error for host {host.Name}: {result}");
                    }
                }

            MigrationStatus = $"✅ Successfully backed up {SelectedSourceHosts.Count} hosts to {backupPath}";
            BackupProgress = "Backup completed successfully!";

            // Keep the success message visible for a moment
            await Task.Delay(2000);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during host backup");
            MigrationStatus = $"❌ Backup failed: {ex.Message}";
            BackupProgress = $"Backup failed: {ex.Message}";

            // Keep the error message visible for a moment
            await Task.Delay(3000);
            }
        finally
            {
            IsBackingUp = false;
            BackupProgress = "";
            }
        }

    [RelayCommand]
    private void ClearSourceHostSelection ()
    {
        if (SelectedSourceCluster != null)
        {
            foreach (var host in SelectedSourceCluster.Hosts)
            {
                host.IsSelected = false; // This will trigger the selection event
            }
        }
    }

    [RelayCommand]
    private async Task MigrateSelectedHosts ()
        {
        if (!IsTargetConnected)
            {
            MigrationStatus = "⚠️ Please connect to target vCenter for migration";
            return;
            }

        if (SelectedSourceHosts.Count == 0)
            {
            MigrationStatus = "⚠️ Please select hosts to migrate";
            return;
            }

        if (SelectedTargetCluster == null)
            {
            MigrationStatus = "⚠️ Please select a target cluster";
            return;
            }

        IsMigrating = true;
        MigrationProgress = $"Starting migration of {SelectedSourceHosts.Count} hosts...";

        try
            {
            int completed = 0;
            foreach (var host in SelectedSourceHosts)
                {
                completed++;
                MigrationProgress = $"Migrating {host.Name} ({completed}/{SelectedSourceHosts.Count})...";

                // Build migration script
                var migrateScript = $@"
                $sourceHost = Get-VMHost -Name '{host.Name}' -ErrorAction Stop
                
                # Put host in maintenance mode
                Write-Output 'Entering maintenance mode...'
                Set-VMHost -VMHost $sourceHost -State Maintenance -Evacuate:$true -Confirm:$false
                
                # Disconnect from source vCenter
                Write-Output 'Disconnecting from source vCenter...'
                Disconnect-VIServer -Server $sourceHost -Confirm:$false
                
                Write-Output 'Host ready for migration to target vCenter'
                'SUCCESS'
            ";

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", migrateScript);

                if (result.Contains("SUCCESS"))
                    {
                    _logger.LogInformation("Host {Host} prepared for migration", host.Name);

                    // Now add to target cluster
                    var addScript = $@"
                    $targetCluster = Get-Cluster -Name '{SelectedTargetCluster.Name}' -ErrorAction Stop
                    
                    # Add host to target cluster
                    Write-Output 'Adding host to target cluster...'
                    Add-VMHost -Name '{host.Name}' -Location $targetCluster -User 'root' -Password 'YourPassword' -Force -Confirm:$false
                    
                    Write-Output 'Host successfully migrated'
                    'SUCCESS'
                ";

                    // Note: You'll need to handle host credentials properly here
                    result = await _persistentConnectionService.ExecuteCommandAsync("target", addScript);

                    if (result.Contains("SUCCESS"))
                        {
                        _logger.LogInformation("Host {Host} successfully migrated to {Cluster}",
                            host.Name, SelectedTargetCluster.Name);
                        }
                    }
                }

            MigrationStatus = $"✅ Successfully migrated {SelectedSourceHosts.Count} hosts";
            MigrationProgress = "Migration completed successfully!";

            // Refresh the data
            await RefreshData();

            // Keep the success message visible for a moment
            await Task.Delay(2000);
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error during host migration");
            MigrationStatus = $"❌ Migration failed: {ex.Message}";
            MigrationProgress = $"Migration failed: {ex.Message}";

            // Keep the error message visible for a moment
            await Task.Delay(3000);
            }
        finally
            {
            IsMigrating = false;
            MigrationProgress = "";
            }
        }

    /// <summary>
    /// Handle host selection changes
    /// </summary>
    private void OnHostSelectionChanged (EsxiHost host, bool isSelected)
    {
        if (isSelected && !SelectedSourceHosts.Contains(host))
        {
            SelectedSourceHosts.Add(host);
        }
        else if (!isSelected && SelectedSourceHosts.Contains(host))
        {
            SelectedSourceHosts.Remove(host);
        }
    }
    /// <summary>
    /// Subscribe to host selection events when clusters are loaded
    /// </summary>
    private void SubscribeToHostSelectionEvents (IEnumerable<ClusterInfo> clusters)
    {
        foreach (var cluster in clusters)
        {
            foreach (var host in cluster.Hosts)
            {
                // Unsubscribe first to avoid duplicate subscriptions
                host.SelectionChanged -= OnHostSelectionChanged;
                // Subscribe to selection changes
                host.SelectionChanged += OnHostSelectionChanged;
            }
        }
    }
    partial void OnSelectedSourceClusterChanged (ClusterInfo? value)
    {
        if (value != null)
        {
            _logger.LogInformation("Selected source cluster: {Cluster} with {Count} hosts",
                value.Name, value.Hosts.Count);
        }

        // Clear selection from all hosts in all clusters
        foreach (var cluster in SourceClusters)
        {
            foreach (var host in cluster.Hosts)
            {
                if (host.IsSelected)
                {
                    host.IsSelected = false;
                }
            }
        }

        // Clear the selected hosts collection
        SelectedSourceHosts.Clear();
    }

    partial void OnSelectedTargetClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _logger.LogInformation("Selected target cluster: {Cluster}", value.Name);

            // Update available hosts for display
            AvailableTargetHosts.Clear();
            foreach (var host in value.Hosts)
                {
                AvailableTargetHosts.Add(host);
                }
            }
        }
    }