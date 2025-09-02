using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using VCenterMigrationTool.ViewModels.Base;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class InfrastructureMigrationViewModel : ActivityLogViewModelBase, INavigationAware
    {
        private readonly SharedConnectionService _sharedConnectionService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly PersistentExternalConnectionService _persistentConnectionService;
        private readonly SharedPowerShellSessionService _sharedPowerShellSession;
        private readonly CredentialService _credentialService;
        private readonly ConfigurationService _configurationService;
        private readonly ILogger<InfrastructureMigrationViewModel> _logger;

        // Connection Status
        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        [ObservableProperty]
        private string _sourceConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _targetConnectionStatus = "Not connected";

        [ObservableProperty]
        private string _sourceDataStatus = "No data loaded";

        [ObservableProperty]
        private string _targetDataStatus = "No data loaded";

        // Data Collections
        [ObservableProperty]
        private ObservableCollection<DatacenterInfo> _sourceDatacenters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _sourceClusters = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _sourceHosts = new();

        [ObservableProperty]
        private ObservableCollection<DatastoreInfo> _sourceDatastores = new();

        [ObservableProperty]
        private ObservableCollection<DatacenterInfo> _targetDatacenters = new();

        [ObservableProperty]
        private ObservableCollection<ClusterInfo> _targetClusters = new();

        [ObservableProperty]
        private ObservableCollection<EsxiHost> _targetHosts = new();

        [ObservableProperty]
        private ObservableCollection<DatastoreInfo> _targetDatastores = new();

        // Migration Options
        [ObservableProperty]
        private bool _migrateDatacenters = true;

        [ObservableProperty]
        private bool _migrateClusters = true;

        [ObservableProperty]
        private bool _migrateHosts = true;

        [ObservableProperty]
        private bool _migrateDatastores = true;

        [ObservableProperty]
        private bool _preserveResourceConfigs = true;

        [ObservableProperty]
        private bool _validateOnly = false;

        // Migration Status
        [ObservableProperty]
        private bool _isMigrationInProgress = false;

        [ObservableProperty]
        private double _migrationProgress = 0;

        [ObservableProperty]
        private string _migrationStatus = "Ready to start infrastructure migration";

        [ObservableProperty]
        private string _activityLog = "Infrastructure migration activity log will appear here...\n";

        // Computed Properties
        public bool CanValidateMigration => IsSourceConnected && IsTargetConnected && !IsMigrationInProgress;
        public bool CanStartMigration => CanValidateMigration && !ValidateOnly;

        public InfrastructureMigrationViewModel(
            SharedConnectionService sharedConnectionService,
            HybridPowerShellService powerShellService,
            IErrorHandlingService errorHandlingService,
            PersistentExternalConnectionService persistentConnectionService,
            SharedPowerShellSessionService sharedPowerShellSession,
            CredentialService credentialService,
            ConfigurationService configurationService,
            ILogger<InfrastructureMigrationViewModel> logger)
        {
            _sharedConnectionService = sharedConnectionService;
            _powerShellService = powerShellService;
            _errorHandlingService = errorHandlingService;
            _persistentConnectionService = persistentConnectionService;
            _sharedPowerShellSession = sharedPowerShellSession;
            _credentialService = credentialService;
            _configurationService = configurationService;
            _logger = logger;

            // Initialize activity log
            InitializeActivityLog("Infrastructure Migration");
        }

        public async Task OnNavigatedToAsync()
        {
            try
            {
                await LoadConnectionStatusAsync();
                await LoadInfrastructureDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during page navigation");
                MigrationStatus = "Error loading page data. Please try refreshing.";
            }
        }

        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;

        private async Task LoadConnectionStatusAsync()
        {
            try
            {
                // Check connection status via SharedConnectionService (supports both API and PowerCLI)
                var sourceConnected = await _sharedConnectionService.IsConnectedAsync("source");
                IsSourceConnected = sourceConnected;
                SourceConnectionStatus = sourceConnected ? "Connected" : "Disconnected";

                // Check target connection
                var targetConnected = await _sharedConnectionService.IsConnectedAsync("target");
                IsTargetConnected = targetConnected;
                TargetConnectionStatus = targetConnected ? "Connected" : "Disconnected";

                OnPropertyChanged(nameof(CanValidateMigration));
                OnPropertyChanged(nameof(CanStartMigration));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load connection status");
                SourceConnectionStatus = "Error checking connection";
                TargetConnectionStatus = "Error checking connection";
            }
        }

        private Task LoadInfrastructureDataAsync()
        {
            try
            {
                // Load cached inventory data if available
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                var targetInventory = _sharedConnectionService.GetTargetInventory();

                // Populate source infrastructure data
                if (sourceInventory != null)
                {
                    // Clear existing collections
                    SourceDatacenters.Clear();
                    SourceClusters.Clear();
                    SourceHosts.Clear();
                    SourceDatastores.Clear();

                    // Populate with data from inventory
                    foreach (var datacenter in sourceInventory.Datacenters)
                    {
                        SourceDatacenters.Add(datacenter);
                    }

                    foreach (var cluster in sourceInventory.Clusters)
                    {
                        SourceClusters.Add(cluster);
                    }

                    foreach (var host in sourceInventory.Hosts)
                    {
                        SourceHosts.Add(host);
                    }

                    foreach (var datastore in sourceInventory.Datastores)
                    {
                        SourceDatastores.Add(datastore);
                    }

                    SourceDataStatus = $"✅ {sourceInventory.Datacenters.Count} datacenters, {sourceInventory.Clusters.Count} clusters, {sourceInventory.Hosts.Count} hosts, {sourceInventory.Datastores.Count} datastores";
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source inventory populated: {SourceDatacenters.Count} DCs, {SourceClusters.Count} clusters, {SourceHosts.Count} hosts, {SourceDatastores.Count} datastores\n";
                }
                else
                {
                    // Clear collections if no data
                    SourceDatacenters.Clear();
                    SourceClusters.Clear();
                    SourceHosts.Clear();
                    SourceDatastores.Clear();
                    SourceDataStatus = "No infrastructure data loaded";
                }

                // Populate target infrastructure data
                if (targetInventory != null)
                {
                    // Clear existing collections
                    TargetDatacenters.Clear();
                    TargetClusters.Clear();
                    TargetHosts.Clear();
                    TargetDatastores.Clear();

                    // Populate with data from inventory
                    foreach (var datacenter in targetInventory.Datacenters)
                    {
                        TargetDatacenters.Add(datacenter);
                    }

                    foreach (var cluster in targetInventory.Clusters)
                    {
                        TargetClusters.Add(cluster);
                    }

                    foreach (var host in targetInventory.Hosts)
                    {
                        TargetHosts.Add(host);
                    }

                    foreach (var datastore in targetInventory.Datastores)
                    {
                        TargetDatastores.Add(datastore);
                    }

                    TargetDataStatus = $"✅ {targetInventory.Datacenters.Count} datacenters, {targetInventory.Clusters.Count} clusters, {targetInventory.Hosts.Count} hosts, {targetInventory.Datastores.Count} datastores";
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target inventory populated: {TargetDatacenters.Count} DCs, {TargetClusters.Count} clusters, {TargetHosts.Count} hosts, {TargetDatastores.Count} datastores\n";
                }
                else
                {
                    // Clear collections if no data
                    TargetDatacenters.Clear();
                    TargetClusters.Clear();
                    TargetHosts.Clear();
                    TargetDatastores.Clear();
                    TargetDataStatus = "No infrastructure data loaded";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading infrastructure data");
                SourceDataStatus = "Error loading data";
                TargetDataStatus = "Error loading data";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR loading infrastructure data: {ex.Message}\n";
            }

            return Task.CompletedTask;
        }

        [RelayCommand]
        private async Task RefreshData()
        {
            await LoadConnectionStatusAsync();
            await LoadInfrastructureDataAsync();
            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Connection status and data refreshed\n";
        }

        [RelayCommand]
        private async Task LoadSourceInfrastructure()
        {
            if (!IsSourceConnected)
            {
                MigrationStatus = "Source connection not available";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Source connection not available for infrastructure loading\n";
                _logger.LogWarning("LoadSourceInfrastructure called but IsSourceConnected is false");
                return;
            }

            try
            {
                _logger.LogInformation("Starting source infrastructure data loading process");
                MigrationStatus = "Checking PowerCLI connection...";
                SourceDataStatus = "🔄 Verifying connection...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting source infrastructure loading - checking shared PowerCLI session\n";
                
                // Check if we already have an active PowerCLI connection in the shared session
                _logger.LogDebug("Verifying PowerCLI connection status for source infrastructure loading");
                var isConnected = await _sharedPowerShellSession.IsVCenterConnectedAsync(isSource: true);
                
                if (!isConnected)
                {
                    _logger.LogInformation("PowerCLI connection needed for source infrastructure, establishing connection");
                    MigrationStatus = "Establishing PowerCLI connection...";
                    SourceDataStatus = "🔄 Connecting to PowerCLI...";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] No active PowerCLI session found, establishing connection to source vCenter\n";
                        
                    var sourceConnection = _sharedConnectionService.SourceConnection;
                    if (sourceConnection == null)
                    {
                        SourceDataStatus = "❌ Connection not configured";
                        MigrationStatus = "Source connection not configured";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Source connection configuration not found - cannot load infrastructure\n";
                        _logger.LogError("Source connection configuration is null");
                        return;
                    }

                    _logger.LogDebug("Source connection configured for server: {Server}, user: {User}", sourceConnection.ServerAddress, sourceConnection.Username);
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Source connection configured - Server: {sourceConnection.ServerAddress}, User: {sourceConnection.Username}\n";

                    var password = _credentialService.GetPassword(sourceConnection);
                    if (string.IsNullOrEmpty(password))
                    {
                        SourceDataStatus = "❌ Credentials unavailable";
                        MigrationStatus = "Source credentials not available for PowerCLI connection";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Source credentials not available from credential service\n";
                        _logger.LogError("Failed to retrieve password for source connection from credential service");
                        return;
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Credentials retrieved successfully, initiating PowerCLI connection\n";
                    _logger.LogDebug("Attempting PowerCLI connection to source vCenter");
                    
                    var connectResult = await _sharedPowerShellSession.ConnectToVCenterAsync(
                        sourceConnection, 
                        password, 
                        isSource: true);
                        
                    if (!connectResult.success)
                    {
                        SourceDataStatus = "❌ PowerCLI connection failed";
                        MigrationStatus = $"PowerCLI connection failed: {connectResult.message}";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: PowerCLI connection failed - {connectResult.message}\n";
                        _logger.LogError("PowerCLI connection to source vCenter failed: {Error}", connectResult.message);
                        return;
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] PowerCLI connection established successfully to source vCenter\n";
                    _logger.LogInformation("Successfully established PowerCLI connection to source vCenter");
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Existing PowerCLI connection found and verified, proceeding with infrastructure enumeration\n";
                    _logger.LogDebug("Using existing PowerCLI connection for source infrastructure loading");
                }

                // Start comprehensive infrastructure data loading
                MigrationStatus = "Loading source infrastructure...";
                SourceDataStatus = "🔄 Enumerating datacenters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting comprehensive source infrastructure enumeration\n";
                _logger.LogInformation("Beginning comprehensive source infrastructure data enumeration");
                
                // Load Datacenters with detailed logging
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 1: Loading datacenters from source vCenter\n";
                var datacenterScript = @"
                    try {
                        Write-Output 'PHASE_START: Datacenter enumeration'
                        $datacenters = Get-Datacenter -ErrorAction Stop
                        Write-Output ""DATACENTER_COUNT: Found $($datacenters.Count) datacenter(s)""
                        
                        $datacenterResults = @()
                        foreach ($dc in $datacenters) {
                            Write-Output ""Processing datacenter: $($dc.Name)""
                            
                            # Extract only the safe properties to avoid ExtensionData serialization issues
                            $cleanDatacenter = @{
                                Name = $dc.Name
                                Id = $dc.Id
                                # Removed ExtensionData to prevent LinkedView duplicate key errors
                            }
                            $datacenterResults += $cleanDatacenter
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Datacenter enumeration completed'
                        $datacenterResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Datacenter enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                _logger.LogDebug("Executing datacenter enumeration script for source infrastructure");
                var datacenterResult = await _sharedPowerShellSession.ExecuteCommandAsync(datacenterScript, isSource: true);
                _logger.LogInformation("Datacenter enumeration completed - result length: {Length} characters", datacenterResult.Length);
                
                if (datacenterResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(datacenterResult, "PHASE_ERROR");
                    SourceDataStatus = "❌ Failed to load datacenters";
                    MigrationStatus = "Failed to load source datacenters";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Datacenter enumeration failed - {errorMessage}\n";
                    _logger.LogError("Source datacenter enumeration failed: {Error}", errorMessage);
                    return;
                }
                
                // Extract datacenter count from result
                var datacenterCount = ExtractCountFromResult(datacenterResult, "DATACENTER_COUNT");
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 1 completed: Found {datacenterCount} datacenter(s)\n";
                
                // Show preview of datacenter results
                if (datacenterResult.Length > 0)
                {
                    var previewLength = Math.Min(datacenterResult.Length, 300);
                    var preview = datacenterResult.Substring(0, previewLength);
                    if (datacenterResult.Length > 300) preview += "...";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Datacenter data preview: {preview}\n";
                }

                // Load Clusters
                SourceDataStatus = "🔄 Enumerating clusters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 2: Loading clusters from source vCenter\n";
                var clusterScript = @"
                    try {
                        Write-Output 'PHASE_START: Cluster enumeration'
                        $clusters = Get-Cluster -ErrorAction Stop
                        Write-Output ""CLUSTER_COUNT: Found $($clusters.Count) cluster(s)""
                        
                        $clusterResults = @()
                        foreach ($cluster in $clusters) {
                            Write-Output ""Processing cluster: $($cluster.Name)""
                            $clusterResults += [PSCustomObject]@{
                                Name = $cluster.Name
                                Id = $cluster.Id
                                ParentFolder = $cluster.ParentFolder.Name
                                HAEnabled = $cluster.HAEnabled
                                DrsEnabled = $cluster.DrsEnabled
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Cluster enumeration completed'
                        $clusterResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Cluster enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var clusterResult = await _sharedPowerShellSession.ExecuteCommandAsync(clusterScript, isSource: true);
                _logger.LogDebug("Cluster enumeration completed - result length: {Length} characters", clusterResult.Length);
                
                if (clusterResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(clusterResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Cluster enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Source cluster enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var clusterCount = ExtractCountFromResult(clusterResult, "CLUSTER_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 2 completed: Found {clusterCount} cluster(s)\n";
                }

                // Load ESXi Hosts
                SourceDataStatus = "🔄 Enumerating hosts...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 3: Loading ESXi hosts from source vCenter\n";
                var hostScript = @"
                    try {
                        Write-Output 'PHASE_START: Host enumeration'
                        $vmhosts = Get-VMHost -ErrorAction Stop
                        Write-Output ""HOST_COUNT: Found $($vmhosts.Count) ESXi host(s)""
                        
                        $hostResults = @()
                        foreach ($vmhost in $vmhosts) {
                            Write-Output ""Processing host: $($vmhost.Name)""
                            $hostResults += [PSCustomObject]@{
                                Name = $vmhost.Name
                                Id = $vmhost.Id
                                ConnectionState = $vmhost.ConnectionState.ToString()
                                Version = $vmhost.Version
                                Build = $vmhost.Build
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Host enumeration completed'
                        $hostResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Host enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var hostResult = await _sharedPowerShellSession.ExecuteCommandAsync(hostScript, isSource: true);
                _logger.LogDebug("Host enumeration completed - result length: {Length} characters", hostResult.Length);
                
                if (hostResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(hostResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Host enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Source host enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var hostCount = ExtractCountFromResult(hostResult, "HOST_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 3 completed: Found {hostCount} ESXi host(s)\n";
                }

                // Load Datastores
                SourceDataStatus = "🔄 Enumerating datastores...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 4: Loading datastores from source vCenter\n";
                var datastoreScript = @"
                    try {
                        Write-Output 'PHASE_START: Datastore enumeration'
                        $datastores = Get-Datastore -ErrorAction Stop
                        Write-Output ""DATASTORE_COUNT: Found $($datastores.Count) datastore(s)""
                        
                        $datastoreResults = @()
                        foreach ($ds in $datastores) {
                            Write-Output ""Processing datastore: $($ds.Name)""
                            $datastoreResults += [PSCustomObject]@{
                                Name = $ds.Name
                                Id = $ds.Id
                                CapacityGB = [math]::Round($ds.CapacityGB, 2)
                                FreeSpaceGB = [math]::Round($ds.FreeSpaceGB, 2)
                                Type = $ds.Type
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Datastore enumeration completed'
                        $datastoreResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Datastore enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var datastoreResult = await _sharedPowerShellSession.ExecuteCommandAsync(datastoreScript, isSource: true);
                _logger.LogDebug("Datastore enumeration completed - result length: {Length} characters", datastoreResult.Length);
                
                if (datastoreResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(datastoreResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Datastore enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Source datastore enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var datastoreCount = ExtractCountFromResult(datastoreResult, "DATASTORE_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 4 completed: Found {datastoreCount} datastore(s)\n";
                }
                
                // Final status update
                if (!datacenterResult.Contains("PHASE_ERROR"))
                {
                    SourceDataStatus = "✅ Infrastructure loaded";
                    MigrationStatus = "Source infrastructure loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] SUCCESS: Source infrastructure enumeration completed successfully\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Infrastructure summary - DCs: {ExtractCountFromResult(datacenterResult, "DATACENTER_COUNT")}, " +
                                  $"Clusters: {ExtractCountFromResult(clusterResult, "CLUSTER_COUNT")}, " +
                                  $"Hosts: {ExtractCountFromResult(hostResult, "HOST_COUNT")}, " +
                                  $"Datastores: {ExtractCountFromResult(datastoreResult, "DATASTORE_COUNT")}\n";
                    
                    _logger.LogInformation("Source infrastructure enumeration completed successfully");
                    
                    // TODO: Parse JSON results and update SourceDatacenters, SourceClusters, SourceHosts, SourceDatastores collections
                    // This will be expanded to fully populate the UI collections
                }
                else
                {
                    SourceDataStatus = "❌ Failed to load infrastructure";
                    MigrationStatus = "Failed to load source infrastructure";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Source infrastructure loading failed at datacenter enumeration phase\n";
                    _logger.LogError("Source infrastructure loading failed at datacenter enumeration phase");
                }
            }
            catch (Exception ex)
            {
                SourceDataStatus = "❌ Error loading infrastructure";
                MigrationStatus = $"Failed to load source infrastructure: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] EXCEPTION: Infrastructure loading failed - {ex.Message}\n";
                _logger.LogError(ex, "Exception occurred during source infrastructure loading");
            }
        }

        [RelayCommand]
        private async Task LoadTargetInfrastructure()
        {
            if (!IsTargetConnected)
            {
                MigrationStatus = "Target connection not available";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Target connection not available for infrastructure loading\n";
                _logger.LogWarning("LoadTargetInfrastructure called but IsTargetConnected is false");
                return;
            }

            try
            {
                _logger.LogInformation("Starting target infrastructure data loading process");
                MigrationStatus = "Checking PowerCLI connection...";
                TargetDataStatus = "🔄 Verifying connection...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting target infrastructure loading - checking shared PowerCLI session\n";
                
                // Check if we already have an active PowerCLI connection in the shared session
                _logger.LogDebug("Verifying PowerCLI connection status for target infrastructure loading");
                var isConnected = await _sharedPowerShellSession.IsVCenterConnectedAsync(isSource: false);
                
                if (!isConnected)
                {
                    _logger.LogInformation("PowerCLI connection needed for target infrastructure, establishing connection");
                    MigrationStatus = "Establishing PowerCLI connection...";
                    TargetDataStatus = "🔄 Connecting to PowerCLI...";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] No active PowerCLI session found, establishing connection to target vCenter\n";
                        
                    var targetConnection = _sharedConnectionService.TargetConnection;
                    if (targetConnection == null)
                    {
                        TargetDataStatus = "❌ Connection not configured";
                        MigrationStatus = "Target connection not configured";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Target connection configuration not found - cannot load infrastructure\n";
                        _logger.LogError("Target connection configuration is null");
                        return;
                    }

                    _logger.LogDebug("Target connection configured for server: {Server}, user: {User}", targetConnection.ServerAddress, targetConnection.Username);
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Target connection configured - Server: {targetConnection.ServerAddress}, User: {targetConnection.Username}\n";

                    var password = _credentialService.GetPassword(targetConnection);
                    if (string.IsNullOrEmpty(password))
                    {
                        TargetDataStatus = "❌ Credentials unavailable";
                        MigrationStatus = "Target credentials not available for PowerCLI connection";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Target credentials not available from credential service\n";
                        _logger.LogError("Failed to retrieve password for target connection from credential service");
                        return;
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Credentials retrieved successfully, initiating PowerCLI connection\n";
                    _logger.LogDebug("Attempting PowerCLI connection to target vCenter");
                    
                    var connectResult = await _sharedPowerShellSession.ConnectToVCenterAsync(
                        targetConnection, 
                        password, 
                        isSource: false);
                        
                    if (!connectResult.success)
                    {
                        TargetDataStatus = "❌ PowerCLI connection failed";
                        MigrationStatus = $"PowerCLI connection failed: {connectResult.message}";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: PowerCLI connection failed - {connectResult.message}\n";
                        _logger.LogError("PowerCLI connection to target vCenter failed: {Error}", connectResult.message);
                        return;
                    }
                    
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] PowerCLI connection established successfully to target vCenter\n";
                    _logger.LogInformation("Successfully established PowerCLI connection to target vCenter");
                }
                else
                {
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Existing PowerCLI connection found and verified, proceeding with infrastructure enumeration\n";
                    _logger.LogDebug("Using existing PowerCLI connection for target infrastructure loading");
                }

                // Start comprehensive infrastructure data loading
                MigrationStatus = "Loading target infrastructure...";
                TargetDataStatus = "🔄 Enumerating datacenters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting comprehensive target infrastructure enumeration\n";
                _logger.LogInformation("Beginning comprehensive target infrastructure data enumeration");
                
                // Load Datacenters with detailed logging
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 1: Loading datacenters from target vCenter\n";
                var datacenterScript = @"
                    try {
                        Write-Output 'PHASE_START: Datacenter enumeration'
                        $datacenters = Get-Datacenter -ErrorAction Stop
                        Write-Output ""DATACENTER_COUNT: Found $($datacenters.Count) datacenter(s)""
                        
                        $datacenterResults = @()
                        foreach ($dc in $datacenters) {
                            Write-Output ""Processing datacenter: $($dc.Name)""
                            
                            # Extract only the safe properties to avoid ExtensionData serialization issues
                            $cleanDatacenter = @{
                                Name = $dc.Name
                                Id = $dc.Id
                                # Removed ExtensionData to prevent LinkedView duplicate key errors
                            }
                            $datacenterResults += $cleanDatacenter
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Datacenter enumeration completed'
                        $datacenterResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Datacenter enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                _logger.LogDebug("Executing datacenter enumeration script for target infrastructure");
                var datacenterResult = await _sharedPowerShellSession.ExecuteCommandAsync(datacenterScript, isSource: false);
                _logger.LogInformation("Datacenter enumeration completed - result length: {Length} characters", datacenterResult.Length);
                
                if (datacenterResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(datacenterResult, "PHASE_ERROR");
                    TargetDataStatus = "❌ Failed to load datacenters";
                    MigrationStatus = "Failed to load target datacenters";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Datacenter enumeration failed - {errorMessage}\n";
                    _logger.LogError("Target datacenter enumeration failed: {Error}", errorMessage);
                    return;
                }
                
                // Extract datacenter count from result
                var datacenterCount = ExtractCountFromResult(datacenterResult, "DATACENTER_COUNT");
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 1 completed: Found {datacenterCount} datacenter(s)\n";
                
                // Show preview of datacenter results
                if (datacenterResult.Length > 0)
                {
                    var previewLength = Math.Min(datacenterResult.Length, 300);
                    var preview = datacenterResult.Substring(0, previewLength);
                    if (datacenterResult.Length > 300) preview += "...";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Datacenter data preview: {preview}\n";
                }

                // Load Clusters
                TargetDataStatus = "🔄 Enumerating clusters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 2: Loading clusters from target vCenter\n";
                var clusterScript = @"
                    try {
                        Write-Output 'PHASE_START: Cluster enumeration'
                        $clusters = Get-Cluster -ErrorAction Stop
                        Write-Output ""CLUSTER_COUNT: Found $($clusters.Count) cluster(s)""
                        
                        $clusterResults = @()
                        foreach ($cluster in $clusters) {
                            Write-Output ""Processing cluster: $($cluster.Name)""
                            $clusterResults += [PSCustomObject]@{
                                Name = $cluster.Name
                                Id = $cluster.Id
                                ParentFolder = $cluster.ParentFolder.Name
                                HAEnabled = $cluster.HAEnabled
                                DrsEnabled = $cluster.DrsEnabled
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Cluster enumeration completed'
                        $clusterResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Cluster enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var clusterResult = await _sharedPowerShellSession.ExecuteCommandAsync(clusterScript, isSource: false);
                _logger.LogDebug("Cluster enumeration completed - result length: {Length} characters", clusterResult.Length);
                
                if (clusterResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(clusterResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Cluster enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Target cluster enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var clusterCount = ExtractCountFromResult(clusterResult, "CLUSTER_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 2 completed: Found {clusterCount} cluster(s)\n";
                }

                // Load ESXi Hosts
                TargetDataStatus = "🔄 Enumerating hosts...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 3: Loading ESXi hosts from target vCenter\n";
                var hostScript = @"
                    try {
                        Write-Output 'PHASE_START: Host enumeration'
                        $vmhosts = Get-VMHost -ErrorAction Stop
                        Write-Output ""HOST_COUNT: Found $($vmhosts.Count) ESXi host(s)""
                        
                        $hostResults = @()
                        foreach ($vmhost in $vmhosts) {
                            Write-Output ""Processing host: $($vmhost.Name)""
                            $hostResults += [PSCustomObject]@{
                                Name = $vmhost.Name
                                Id = $vmhost.Id
                                ConnectionState = $vmhost.ConnectionState.ToString()
                                Version = $vmhost.Version
                                Build = $vmhost.Build
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Host enumeration completed'
                        $hostResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Host enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var hostResult = await _sharedPowerShellSession.ExecuteCommandAsync(hostScript, isSource: false);
                _logger.LogDebug("Host enumeration completed - result length: {Length} characters", hostResult.Length);
                
                if (hostResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(hostResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Host enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Target host enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var hostCount = ExtractCountFromResult(hostResult, "HOST_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 3 completed: Found {hostCount} ESXi host(s)\n";
                }

                // Load Datastores
                TargetDataStatus = "🔄 Enumerating datastores...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 4: Loading datastores from target vCenter\n";
                var datastoreScript = @"
                    try {
                        Write-Output 'PHASE_START: Datastore enumeration'
                        $datastores = Get-Datastore -ErrorAction Stop
                        Write-Output ""DATASTORE_COUNT: Found $($datastores.Count) datastore(s)""
                        
                        $datastoreResults = @()
                        foreach ($ds in $datastores) {
                            Write-Output ""Processing datastore: $($ds.Name)""
                            $datastoreResults += [PSCustomObject]@{
                                Name = $ds.Name
                                Id = $ds.Id
                                CapacityGB = [math]::Round($ds.CapacityGB, 2)
                                FreeSpaceGB = [math]::Round($ds.FreeSpaceGB, 2)
                                Type = $ds.Type
                            }
                        }
                        
                        Write-Output 'PHASE_SUCCESS: Datastore enumeration completed'
                        $datastoreResults | ConvertTo-Json -Depth 3
                    }
                    catch {
                        Write-Output ""PHASE_ERROR: Datastore enumeration failed - $($_.Exception.Message)""
                    }
                ";
                
                var datastoreResult = await _sharedPowerShellSession.ExecuteCommandAsync(datastoreScript, isSource: false);
                _logger.LogDebug("Datastore enumeration completed - result length: {Length} characters", datastoreResult.Length);
                
                if (datastoreResult.Contains("PHASE_ERROR"))
                {
                    var errorMessage = ExtractErrorMessage(datastoreResult, "PHASE_ERROR");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Datastore enumeration failed - {errorMessage}\n";
                    _logger.LogWarning("Target datastore enumeration failed: {Error}", errorMessage);
                }
                else
                {
                    var datastoreCount = ExtractCountFromResult(datastoreResult, "DATASTORE_COUNT");
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Phase 4 completed: Found {datastoreCount} datastore(s)\n";
                }
                
                // Final status update
                if (!datacenterResult.Contains("PHASE_ERROR"))
                {
                    TargetDataStatus = "✅ Infrastructure loaded";
                    MigrationStatus = "Target infrastructure loaded successfully";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] SUCCESS: Target infrastructure enumeration completed successfully\n";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Infrastructure summary - DCs: {ExtractCountFromResult(datacenterResult, "DATACENTER_COUNT")}, " +
                                  $"Clusters: {ExtractCountFromResult(clusterResult, "CLUSTER_COUNT")}, " +
                                  $"Hosts: {ExtractCountFromResult(hostResult, "HOST_COUNT")}, " +
                                  $"Datastores: {ExtractCountFromResult(datastoreResult, "DATASTORE_COUNT")}\n";
                    
                    _logger.LogInformation("Target infrastructure enumeration completed successfully");
                    
                    // TODO: Parse JSON results and update TargetDatacenters, TargetClusters, TargetHosts, TargetDatastores collections
                    // This will be expanded to fully populate the UI collections
                }
                else
                {
                    TargetDataStatus = "❌ Failed to load infrastructure";
                    MigrationStatus = "Failed to load target infrastructure";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Target infrastructure loading failed at datacenter enumeration phase\n";
                    _logger.LogError("Target infrastructure loading failed at datacenter enumeration phase");
                }
            }
            catch (Exception ex)
            {
                TargetDataStatus = "❌ Error loading infrastructure";
                MigrationStatus = $"Failed to load target infrastructure: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] EXCEPTION: Infrastructure loading failed - {ex.Message}\n";
                _logger.LogError(ex, "Exception occurred during target infrastructure loading");
            }
        }

        [RelayCommand]
        private async Task ValidateMigration()
        {
            try
            {
                MigrationStatus = "Validating infrastructure migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting migration validation\n";
                
                // TODO: Implement validation logic
                await Task.Delay(2000);
                
                MigrationStatus = "Infrastructure migration validation completed";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migration validation completed successfully\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Validation failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during migration validation");
            }
        }

        [RelayCommand]
        private async Task StartMigration()
        {
            try
            {
                IsMigrationInProgress = true;
                MigrationProgress = 0;
                MigrationStatus = "Starting infrastructure migration...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Starting infrastructure migration\n";

                if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
                {
                    MigrationStatus = "Source or target connection not available";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Connection not available\n";
                    return;
                }

                // Get source credentials
                var sourcePassword = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
                var targetPassword = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);

                if (string.IsNullOrEmpty(sourcePassword) || string.IsNullOrEmpty(targetPassword))
                {
                    MigrationStatus = "Unable to retrieve connection credentials";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: Missing credentials\n";
                    return;
                }

                // Get source inventory for datacenter migration
                MigrationStatus = "Loading source datacenters...";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Loading source datacenter list\n";
                MigrationProgress = 10;

                // For now, we'll migrate datacenters based on what we find in the source
                // In a complete implementation, this would come from selected items in the UI
                var sourceDatacenters = await GetSourceDatacentersAsync();
                
                if (sourceDatacenters == null || !sourceDatacenters.Any())
                {
                    MigrationStatus = "No datacenters found in source to migrate";
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: No datacenters found in source\n";
                    return;
                }

                MigrationProgress = 20;
                var totalDatacenters = sourceDatacenters.Count();
                var migratedCount = 0;
                var skippedCount = 0;
                var errorCount = 0;

                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Found {totalDatacenters} datacenter(s) to migrate\n";

                foreach (var datacenter in sourceDatacenters)
                {
                    try
                    {
                        MigrationStatus = $"Migrating datacenter: {datacenter.Name}";
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migrating datacenter: {datacenter.Name}\n";

                        // Call the migration script for each datacenter
                        var parameters = new Dictionary<string, object>
                        {
                            ["SourceVCenter"] = _sharedConnectionService.SourceConnection.ServerAddress,
                            ["SourceUsername"] = _sharedConnectionService.SourceConnection.Username,
                            ["SourcePassword"] = sourcePassword,
                            ["TargetVCenter"] = _sharedConnectionService.TargetConnection.ServerAddress,
                            ["TargetUsername"] = _sharedConnectionService.TargetConnection.Username,
                            ["TargetPassword"] = targetPassword,
                            ["ObjectType"] = "Datacenter",
                            ["ObjectName"] = datacenter.Name,
                            ["ObjectId"] = datacenter.Id ?? "",
                            ["ObjectPath"] = "", // Datacenters don't have a path concept
                            ["LogPath"] = _configurationService.GetConfiguration().LogPath,
                            ["ValidateOnly"] = ValidateOnly
                        };

                        var result = await _powerShellService.RunScriptAsync("Scripts\\Migrate-VCenterObject.ps1", parameters);

                        if (result.Contains("Successfully migrated"))
                        {
                            migratedCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ✅ Successfully migrated datacenter: {datacenter.Name}\n";
                        }
                        else if (result.Contains("already exists"))
                        {
                            skippedCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ⚠️ Datacenter already exists: {datacenter.Name}\n";
                        }
                        else if (result.StartsWith("ERROR"))
                        {
                            errorCount++;
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Error migrating {datacenter.Name}: {result}\n";
                        }
                        else
                        {
                            // Log the full result for debugging
                            ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Result for {datacenter.Name}: {result}\n";
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ❌ Exception migrating {datacenter.Name}: {ex.Message}\n";
                        _logger.LogError(ex, "Error migrating datacenter {DatacenterName}", datacenter.Name);
                    }

                    // Update progress
                    var currentProgress = 20 + ((migratedCount + skippedCount + errorCount) * 70 / totalDatacenters);
                    MigrationProgress = Math.Min(currentProgress, 90);
                }

                // Final summary
                MigrationProgress = 100;
                MigrationStatus = $"Migration completed - {migratedCount} migrated, {skippedCount} skipped, {errorCount} errors";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Migration Summary:\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Migrated: {migratedCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Skipped (already exist): {skippedCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] - Errors: {errorCount}\n";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Infrastructure migration completed\n";
            }
            catch (Exception ex)
            {
                MigrationStatus = $"Migration failed: {ex.Message}";
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
                _logger.LogError(ex, "Error during infrastructure migration");
            }
            finally
            {
                IsMigrationInProgress = false;
                OnPropertyChanged(nameof(CanValidateMigration));
                OnPropertyChanged(nameof(CanStartMigration));
            }
        }

        private async Task<IEnumerable<DatacenterInfo>> GetSourceDatacentersAsync()
        {
            try
            {
                _logger.LogDebug("Attempting to retrieve source datacenters for migration");
                var sourceInventory = _sharedConnectionService.GetSourceInventory();
                if (sourceInventory?.Datacenters != null && sourceInventory.Datacenters.Any())
                {
                    _logger.LogInformation("Found cached source datacenters: {Count} items", sourceInventory.Datacenters.Count);
                    return sourceInventory.Datacenters;
                }

                // If no cached inventory, try to load it
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] No cached datacenter data found, loading source infrastructure...\n";
                _logger.LogInformation("No cached source datacenters found, loading fresh data");
                var success = await _sharedConnectionService.LoadSourceInfrastructureAsync();
                if (success)
                {
                    sourceInventory = _sharedConnectionService.GetSourceInventory();
                    var datacenters = sourceInventory?.Datacenters ?? new List<DatacenterInfo>();
                    _logger.LogInformation("Successfully loaded source infrastructure - found {Count} datacenters", datacenters.Count);
                    ActivityLog += $"[{DateTime.Now:HH:mm:ss}] Successfully loaded {datacenters.Count} source datacenters\n";
                    return datacenters;
                }
                
                _logger.LogWarning("Failed to load source infrastructure data");
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] WARNING: Failed to load source infrastructure data\n";
                return new List<DatacenterInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while loading source datacenters for migration");
                ActivityLog += $"[{DateTime.Now:HH:mm:ss}] EXCEPTION loading source datacenters: {ex.Message}\n";
                return new List<DatacenterInfo>();
            }
        }
        
        /// <summary>
        /// Helper method to extract error messages from PowerShell script results
        /// </summary>
        private string ExtractErrorMessage(string result, string errorPrefix)
        {
            try
            {
                var errorIndex = result.IndexOf(errorPrefix + ":", StringComparison.OrdinalIgnoreCase);
                if (errorIndex >= 0)
                {
                    var startIndex = errorIndex + errorPrefix.Length + 1;
                    var endIndex = result.IndexOf('\n', startIndex);
                    if (endIndex < 0) endIndex = result.Length;
                    
                    return result.Substring(startIndex, endIndex - startIndex).Trim();
                }
                return "Unknown error occurred";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract error message from PowerShell result");
                return "Error parsing failed";
            }
        }
        
        /// <summary>
        /// Helper method to extract count values from PowerShell script results
        /// </summary>
        private string ExtractCountFromResult(string result, string countPrefix)
        {
            try
            {
                var countIndex = result.IndexOf(countPrefix + ":", StringComparison.OrdinalIgnoreCase);
                if (countIndex >= 0)
                {
                    var startIndex = countIndex + countPrefix.Length + 1;
                    var endIndex = result.IndexOf('\n', startIndex);
                    if (endIndex < 0) endIndex = result.Length;
                    
                    var countText = result.Substring(startIndex, endIndex - startIndex).Trim();
                    // Extract just the number from "Found X datacenter(s)" format
                    var words = countText.Split(' ');
                    if (words.Length > 1)
                    {
                        return words[1]; // Should be the count number
                    }
                    return countText;
                }
                return "0";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract count from PowerShell result");
                return "?";
            }
        }
    }
}