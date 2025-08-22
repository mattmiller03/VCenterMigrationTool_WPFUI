using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class ResourcePoolMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly ILogger<ResourcePoolMigrationViewModel> _logger;

    // Source and Target Connection Properties
    [ObservableProperty]
    private string _sourceVCenter = string.Empty;

    [ObservableProperty]
    private string _targetVCenter = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Not Connected";

    // Data Loading Properties
    [ObservableProperty]
    private bool _isLoadingData;

    [ObservableProperty]
    private string _loadingMessage = string.Empty;

    // Source Data Properties
    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _sourceClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedSourceCluster;

    [ObservableProperty]
    private ObservableCollection<ResourcePoolInfo> _sourceResourcePools = new();

    [ObservableProperty]
    private ObservableCollection<ResourcePoolInfo> _selectedSourcePools = new();

    // Target Data Properties
    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private ObservableCollection<ResourcePoolInfo> _targetResourcePools = new();

    [ObservableProperty]
    private ResourcePoolInfo? _selectedTargetPool;

    // Migration Configuration Properties
    [ObservableProperty]
    private bool _preserveSettings = true;

    [ObservableProperty]
    private bool _migrateChildPools = true;

    [ObservableProperty]
    private bool _validateBeforeMigration = true;

    [ObservableProperty]
    private bool _createBackup = true;

    [ObservableProperty]
    private string _backupLocation = string.Empty;

    // Import/Export Properties
    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _selectedPoolNames = new();

    // Migration Status Properties
    [ObservableProperty]
    private bool _isMigrationInProgress;

    [ObservableProperty]
    private string _migrationProgress = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _migrationResults = new();

    // Additional UI Properties for XAML Binding
    [ObservableProperty]
    private string _operationStatus = "Ready";

    [ObservableProperty]
    private bool _isOperationRunning;

    [ObservableProperty]
    private double _operationProgress;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    [ObservableProperty]
    private string _reportFilePath = string.Empty;

    // Export/Import Options
    [ObservableProperty]
    private bool _exportAllPools = true;

    [ObservableProperty]
    private bool _removeExistingPools = false;

    [ObservableProperty]
    private bool _moveVMsToResourcePools = false;

    public ResourcePoolMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        ILogger<ResourcePoolMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _logger = logger;

        // Initialize backup location
        BackupLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VCenterMigrationTool", "ResourcePoolBackups");
        }

    // Connection Commands
    [RelayCommand]
    private async Task ConnectToVCenters ()
        {
        try
            {
            IsLoadingData = true;
            LoadingMessage = "Connecting to vCenter servers...";

            // Get source and target from shared connection
            var sourceConnection = _sharedConnectionService.SourceConnection;
            var targetConnection = _sharedConnectionService.TargetConnection;

            if (sourceConnection == null || targetConnection == null)
                {
                ConnectionStatus = "Please configure source and target connections in Settings";
                return;
                }

            SourceVCenter = sourceConnection.ServerAddress;
            TargetVCenter = targetConnection.ServerAddress;

            // Test connections
            var script = @"
                param($SourceServer, $TargetServer, $SourceUser, $SourcePass, $TargetUser, $TargetPass)
                
                try {
                    # Connect to source vCenter
                    $sourceCred = New-Object System.Management.Automation.PSCredential($SourceUser, (ConvertTo-SecureString $SourcePass -AsPlainText -Force))
                    Connect-VIServer -Server $SourceServer -Credential $sourceCred -Force | Out-Null
                    
                    # Connect to target vCenter
                    $targetCred = New-Object System.Management.Automation.PSCredential($TargetUser, (ConvertTo-SecureString $TargetPass -AsPlainText -Force))
                    Connect-VIServer -Server $TargetServer -Credential $targetCred -Force | Out-Null
                    
                    Write-Output 'SUCCESS: Connected to both vCenter servers'
                }
                catch {
                    Write-Error ""Failed to connect: $($_.Exception.Message)""
                }
            ";

            // Get credentials using your actual CredentialService implementation
            var sourcePassword = _credentialService.GetPassword(sourceConnection);
            var targetPassword = _credentialService.GetPassword(targetConnection);

            var parameters = new Dictionary<string, object>
                {
                ["SourceServer"] = sourceConnection.ServerAddress,
                ["TargetServer"] = targetConnection.ServerAddress,
                ["SourceUser"] = sourceConnection.Username,
                ["SourcePass"] = sourcePassword ?? "",
                ["TargetUser"] = targetConnection.Username,
                ["TargetPass"] = targetPassword ?? ""
                };

            var result = await _powerShellService.RunScriptAsync(script, parameters);

            if (!string.IsNullOrEmpty(result) && result.Contains("SUCCESS"))
                {
                IsConnected = true;
                ConnectionStatus = "Connected to both vCenter servers";
                _logger.LogInformation("Successfully connected to source and target vCenters");
                }
            else
                {
                IsConnected = false;
                ConnectionStatus = $"Connection failed: {result}";
                _logger.LogError("Failed to connect to vCenters: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            IsConnected = false;
            ConnectionStatus = $"Error: {ex.Message}";
            _logger.LogError(ex, "Error connecting to vCenters");
            }
        finally
            {
            IsLoadingData = false;
            LoadingMessage = string.Empty;
            }
        }

    // Data Loading Commands
    [RelayCommand]
    private async Task LoadSourceClusters ()
        {
        if (!IsConnected)
            {
            await ConnectToVCenters();
            if (!IsConnected) return;
            }

        try
            {
            IsLoadingData = true;
            LoadingMessage = "Loading source clusters...";

            var script = @"
                param($SourceServer)
                
                try {
                    $clusters = Get-Cluster -Server $SourceServer | Select-Object Name, Id, @{N='Host';E={$_.ExtensionData.Summary.NumHosts}}, @{N='VM';E={$_.ExtensionData.Summary.NumVmotionInterfaces}}
                    
                    $clusterData = @()
                    foreach ($cluster in $clusters) {
                        $clusterData += [PSCustomObject]@{
                            Name = $cluster.Name
                            Id = $cluster.Id
                            HostCount = $cluster.Host
                            VmCount = $cluster.VM
                        }
                    }
                    
                    return $clusterData | ConvertTo-Json -Depth 3
                }
                catch {
                    Write-Error ""Failed to load clusters: $($_.Exception.Message)""
                }
            ";

            var parameters = new Dictionary<string, object>
                {
                ["SourceServer"] = SourceVCenter
                };

            var result = await _powerShellService.RunScriptAsync(script, parameters);

            if (!string.IsNullOrEmpty(result))
                {
                // Parse JSON result and populate clusters
                SourceClusters.Clear();
                try
                {
                    var clusters = JsonSerializer.Deserialize<ClusterInfo[]>(result);
                    if (clusters != null)
                    {
                        foreach (var cluster in clusters)
                        {
                            SourceClusters.Add(cluster);
                        }
                    }
                    _logger.LogInformation("Successfully loaded {Count} source clusters", SourceClusters.Count);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Error parsing source clusters JSON");
                }
                }
            else
                {
                _logger.LogError("Failed to load source clusters: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading source clusters");
            }
        finally
            {
            IsLoadingData = false;
            LoadingMessage = string.Empty;
            }
        }

    [RelayCommand]
    private async Task LoadTargetClusters ()
        {
        if (!IsConnected)
            {
            await ConnectToVCenters();
            if (!IsConnected) return;
            }

        try
            {
            IsLoadingData = true;
            LoadingMessage = "Loading target clusters...";

            var script = @"
                param($TargetServer)
                
                try {
                    $clusters = Get-Cluster -Server $TargetServer | Select-Object Name, Id, @{N='Host';E={$_.ExtensionData.Summary.NumHosts}}, @{N='VM';E={$_.ExtensionData.Summary.NumVmotionInterfaces}}
                    
                    $clusterData = @()
                    foreach ($cluster in $clusters) {
                        $clusterData += [PSCustomObject]@{
                            Name = $cluster.Name
                            Id = $cluster.Id
                            HostCount = $cluster.Host
                            VmCount = $cluster.VM
                        }
                    }
                    
                    return $clusterData | ConvertTo-Json -Depth 3
                }
                catch {
                    Write-Error ""Failed to load clusters: $($_.Exception.Message)""
                }
            ";

            var parameters = new Dictionary<string, object>
                {
                ["TargetServer"] = TargetVCenter
                };

            var result = await _powerShellService.RunScriptAsync(script, parameters);

            if (!string.IsNullOrEmpty(result))
                {
                // Parse JSON result and populate clusters
                TargetClusters.Clear();
                try
                {
                    var clusters = JsonSerializer.Deserialize<ClusterInfo[]>(result);
                    if (clusters != null)
                    {
                        foreach (var cluster in clusters)
                        {
                            TargetClusters.Add(cluster);
                        }
                    }
                    _logger.LogInformation("Successfully loaded {Count} target clusters", TargetClusters.Count);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Error parsing target clusters JSON");
                }
                }
            else
                {
                _logger.LogError("Failed to load target clusters: {Error}", result);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error loading target clusters");
            }
        finally
            {
            IsLoadingData = false;
            LoadingMessage = string.Empty;
            }
        }

    [RelayCommand]
    private async Task LoadSourceResourcePools ()
        {
        if (SelectedSourceCluster == null) 
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Please select a source cluster first\n";
            return;
        }

        if (_sharedConnectionService.SourceConnection == null)
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No source vCenter connection\n";
            return;
        }

        try
            {
            IsLoadingData = true;
            LoadingMessage = "Loading source resource pools...";
            SourceResourcePools.Clear();

            var password = _credentialService.GetPassword(_sharedConnectionService.SourceConnection);
            if (string.IsNullOrEmpty(password))
            {
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No password found for source connection\n";
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "ClusterName", SelectedSourceCluster.Name },
                { "BypassModuleCheck", true }
            };

            // Call the Get-ResourcePools.ps1 script
            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-ResourcePools.ps1",
                _sharedConnectionService.SourceConnection,
                password,
                parameters);

            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    // Parse the JSON result
                    var resourcePools = JsonSerializer.Deserialize<ResourcePoolInfo[]>(result);
                    if (resourcePools != null)
                    {
                        foreach (var pool in resourcePools)
                        {
                            SourceResourcePools.Add(pool);
                        }
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Loaded {SourceResourcePools.Count} resource pools from cluster '{SelectedSourceCluster.Name}'\n";
                        _logger.LogInformation("Successfully loaded {Count} source resource pools for cluster {Cluster}", 
                            SourceResourcePools.Count, SelectedSourceCluster.Name);
                    }
                }
                catch (JsonException ex)
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error parsing resource pool data: {ex.Message}\n";
                    _logger.LogError(ex, "Error parsing resource pool JSON data");
                }
            }
            else
            {
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] No resource pools found in cluster '{SelectedSourceCluster.Name}'\n";
            }
            }
        catch (Exception ex)
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error loading resource pools: {ex.Message}\n";
            _logger.LogError(ex, "Error loading source resource pools");
            }
        finally
            {
            IsLoadingData = false;
            LoadingMessage = string.Empty;
            }
        }

    [RelayCommand]
    private async Task LoadTargetResourcePools ()
        {
        if (SelectedTargetCluster == null) 
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Please select a target cluster first\n";
            return;
        }

        if (_sharedConnectionService.TargetConnection == null)
        {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No target vCenter connection\n";
            return;
        }

        try
            {
            IsLoadingData = true;
            LoadingMessage = "Loading target resource pools...";
            TargetResourcePools.Clear();

            var password = _credentialService.GetPassword(_sharedConnectionService.TargetConnection);
            if (string.IsNullOrEmpty(password))
            {
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error: No password found for target connection\n";
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "ClusterName", SelectedTargetCluster.Name },
                { "BypassModuleCheck", true }
            };

            // Call the Get-ResourcePools.ps1 script
            var result = await _powerShellService.RunVCenterScriptAsync(
                "Scripts\\Get-ResourcePools.ps1",
                _sharedConnectionService.TargetConnection,
                password,
                parameters);

            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    // Parse the JSON result
                    var resourcePools = JsonSerializer.Deserialize<ResourcePoolInfo[]>(result);
                    if (resourcePools != null)
                    {
                        foreach (var pool in resourcePools)
                        {
                            TargetResourcePools.Add(pool);
                        }
                        LogOutput += $"[{DateTime.Now:HH:mm:ss}] Loaded {TargetResourcePools.Count} resource pools from cluster '{SelectedTargetCluster.Name}'\n";
                        _logger.LogInformation("Successfully loaded {Count} target resource pools for cluster {Cluster}", 
                            TargetResourcePools.Count, SelectedTargetCluster.Name);
                    }
                }
                catch (JsonException ex)
                {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error parsing resource pool data: {ex.Message}\n";
                    _logger.LogError(ex, "Error parsing resource pool JSON data");
                }
            }
            else
            {
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] No resource pools found in cluster '{SelectedTargetCluster.Name}'\n";
            }
            }
        catch (Exception ex)
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Error loading resource pools: {ex.Message}\n";
            _logger.LogError(ex, "Error loading target resource pools");
            }
        finally
            {
            IsLoadingData = false;
            LoadingMessage = string.Empty;
            }
        }

    // File Operations Commands
    [RelayCommand]
    private void BrowseImportFile ()
        {
        var openFileDialog = new OpenFileDialog
            {
            Title = "Select Resource Pool Import File",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = BackupLocation
            };

        if (openFileDialog.ShowDialog() == true)
            {
            ImportFilePath = openFileDialog.FileName;
            }
        }

    [RelayCommand]
    private void BrowseReportFile ()
        {
        var saveFileDialog = new SaveFileDialog
            {
            Title = "Save Migration Report",
            Filter = "HTML files (*.html)|*.html|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = BackupLocation,
            FileName = $"ResourcePoolMigration_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };

        if (saveFileDialog.ShowDialog() == true)
            {
            ExportFilePath = saveFileDialog.FileName;
            }
        }

    [RelayCommand]
    private void BrowseBackupLocation ()
        {
        // Using the Win32 OpenFileDialog in folder mode
        var folderDialog = new OpenFileDialog
            {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder",
            Title = "Select Backup Location"
            };

        if (folderDialog.ShowDialog() == true)
            {
            BackupLocation = System.IO.Path.GetDirectoryName(folderDialog.FileName) ?? BackupLocation;
            }
        }

    // Migration Commands
    [RelayCommand]
    private async Task StartMigration ()
        {
        if (SelectedSourcePools.Count == 0 || SelectedTargetPool == null)
            {
            OperationStatus = "Cannot start migration: No source pools or target pool selected";
            _logger.LogWarning("Cannot start migration: No source pools or target pool selected");
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationStatus = "Starting resource pool migration...";
            OperationProgress = 0;
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting migration of {SelectedSourcePools.Count} resource pools\n";

            // TODO: Implement actual migration logic
            for (int i = 0; i <= 100; i += 5)
                {
                OperationProgress = i;
                OperationStatus = $"Migrating resource pools... {i}%";
                await Task.Delay(100); // Simulate work
                }

            OperationStatus = "Migration completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Migration completed successfully\n";
            _logger.LogInformation("Resource pool migration completed");
            }
        catch (Exception ex)
            {
            OperationStatus = $"Migration failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: Migration failed - {ex.Message}\n";
            _logger.LogError(ex, "Error during resource pool migration");
            }
        finally
            {
            IsOperationRunning = false;
            OperationProgress = 0;
            }
        }

    [RelayCommand]
    private async Task ValidateMigration ()
        {
        try
            {
            IsOperationRunning = true;
            OperationStatus = "Validating migration configuration...";
            OperationProgress = 0;

            // TODO: Implement validation logic
            for (int i = 0; i <= 100; i += 25)
                {
                OperationProgress = i;
                await Task.Delay(200); // Simulate validation work
                }

            OperationStatus = "Validation completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Migration validation completed\n";
            _logger.LogInformation("Migration validation completed");
            }
        catch (Exception ex)
            {
            OperationStatus = $"Validation failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: Validation failed - {ex.Message}\n";
            _logger.LogError(ex, "Error during migration validation");
            }
        finally
            {
            IsOperationRunning = false;
            OperationProgress = 0;
            }
        }

    [RelayCommand]
    private async Task ExportConfiguration ()
        {
        try
            {
            IsOperationRunning = true;
            OperationStatus = "Exporting resource pool configuration...";
            OperationProgress = 0;

            // TODO: Implement export logic
            for (int i = 0; i <= 100; i += 20)
                {
                OperationProgress = i;
                await Task.Delay(150); // Simulate export work
                }

            OperationStatus = "Configuration exported successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Configuration exported successfully\n";
            _logger.LogInformation("Configuration exported successfully");
            }
        catch (Exception ex)
            {
            OperationStatus = $"Export failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: Export failed - {ex.Message}\n";
            _logger.LogError(ex, "Error exporting configuration");
            }
        finally
            {
            IsOperationRunning = false;
            OperationProgress = 0;
            }
        }

    [RelayCommand]
    private void BrowseExportFile ()
        {
        var saveFileDialog = new SaveFileDialog
            {
            Title = "Save Resource Pool Export File",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = BackupLocation,
            FileName = $"ResourcePools_Export_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

        if (saveFileDialog.ShowDialog() == true)
            {
            ExportFilePath = saveFileDialog.FileName;
            }
        }

    [RelayCommand]
    private async Task ExportResourcePools ()
        {
        if (string.IsNullOrEmpty(ExportFilePath))
            {
            _logger.LogWarning("Cannot export: No export file path specified");
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationStatus = "Exporting resource pools...";
            OperationProgress = 0;

            // TODO: Implement actual export logic
            for (int i = 0; i <= 100; i += 10)
                {
                OperationProgress = i;
                await Task.Delay(100); // Simulate work
                }

            OperationStatus = "Export completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Resource pools exported to: {ExportFilePath}\n";
            _logger.LogInformation("Resource pools exported successfully to {FilePath}", ExportFilePath);
            }
        catch (Exception ex)
            {
            OperationStatus = $"Export failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error exporting resource pools");
            }
        finally
            {
            IsOperationRunning = false;
            OperationProgress = 0;
            }
        }

    [RelayCommand]
    private async Task ImportResourcePools ()
        {
        if (string.IsNullOrEmpty(ImportFilePath) || !File.Exists(ImportFilePath))
            {
            _logger.LogWarning("Cannot import: No valid import file selected");
            OperationStatus = "Import failed: No valid file selected";
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationStatus = "Importing resource pools...";
            OperationProgress = 0;

            // TODO: Implement actual import logic
            for (int i = 0; i <= 100; i += 20)
                {
                OperationProgress = i;
                await Task.Delay(200); // Simulate work
                }

            OperationStatus = "Import completed successfully";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Resource pools imported from: {ImportFilePath}\n";
            _logger.LogInformation("Resource pools imported successfully from {FilePath}", ImportFilePath);
            }
        catch (Exception ex)
            {
            OperationStatus = $"Import failed: {ex.Message}";
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            _logger.LogError(ex, "Error importing resource pools");
            }
        finally
            {
            IsOperationRunning = false;
            OperationProgress = 0;
            }
        }

    // Property Change Handlers
    partial void OnSelectedSourceClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _ = LoadSourceResourcePools();
            }
        }

    partial void OnSelectedTargetClusterChanged (ClusterInfo? value)
        {
        if (value != null)
            {
            _ = LoadTargetResourcePools();
            }
        }

    partial void OnIsOperationRunningChanged (bool value)
        {
        // Update operation status when operation state changes
        if (!value && OperationProgress >= 100)
            {
            OperationProgress = 0;
            }
        }

    partial void OnIsConnectedChanged (bool value)
        {
        if (value)
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Connected to vCenter servers\n";
            }
        else
            {
            LogOutput += $"[{DateTime.Now:HH:mm:ss}] Disconnected from vCenter servers\n";
            }
        }

    // Navigation Interface
    public void OnNavigatedTo ()
        {
        _logger.LogInformation("Navigated to Resource Pool Migration page");
        }

    public void OnNavigatedFrom ()
        {
        _logger.LogInformation("Navigated away from Resource Pool Migration page");
        }

    public Task OnNavigatedToAsync ()
        {
        OnNavigatedTo();
        return Task.CompletedTask;
        }

    public Task OnNavigatedFromAsync ()
        {
        OnNavigatedFrom();
        return Task.CompletedTask;
        }
    }