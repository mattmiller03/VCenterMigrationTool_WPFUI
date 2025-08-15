using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;
using System.Collections.Generic;
using System;
using System.IO;

namespace VCenterMigrationTool.ViewModels;

public partial class HostMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<HostMigrationViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<ClusterNode> _sourceTopology = new();

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private string? _selectedTargetDatacenter;

    [ObservableProperty]
    private bool _isLoadingData;

    [ObservableProperty]
    private string _loadingStatus = "Ready to load data";

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private double _migrationProgress;

    [ObservableProperty]
    private string _migrationStatus = "Ready to migrate hosts";

    [ObservableProperty]
    private string _logOutput = "Migration log will appear here...";

    // Migration Options - Enhanced for VMHostConfigV2
    [ObservableProperty]
    private bool _preserveVmAssignments = true;

    [ObservableProperty]
    private bool _migrateHostProfiles = true;

    [ObservableProperty]
    private bool _updateDrsRules = false;

    [ObservableProperty]
    private bool _createBackupBeforeMigration = true;

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private int _operationTimeout = 600;

    [ObservableProperty]
    private string? _uplinkPortgroupName;

    // Host Action Selection
    [ObservableProperty]
    private string _selectedAction = "Migrate";

    [ObservableProperty]
    private string? _selectedBackupFile;

    public List<string> AvailableActions { get; } = new() { "Backup", "Restore", "Migrate" };

    public HostMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        IDialogService dialogService,
        ILogger<HostMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _dialogService = dialogService;
        _logger = logger;

        // Initialize backup path from configuration
        var config = _configurationService.GetConfiguration();
        BackupPath = Path.Combine(config.ExportPath ?? "Exports", "HostConfigs");
        }

    public async Task OnNavigatedToAsync ()
        {
        // Check if we have active connections
        if (_sharedConnectionService.SourceConnection != null && _sharedConnectionService.TargetConnection != null)
            {
            LoadingStatus = "Connections available - ready to load data";
            }
        else
            {
            LoadingStatus = "Please establish source and target connections on the Dashboard first";
            }

        await Task.CompletedTask;
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    /// <summary>
    /// Gets the count of selected hosts across all clusters
    /// </summary>
    public int SelectedHostCount => SourceTopology
        .SelectMany(cluster => cluster.Hosts)
        .Count(host => host.IsSelected);

    /// <summary>
    /// Determines if migration can be started
    /// </summary>
    public bool CanStartMigration =>
        SelectedHostCount > 0 &&
        SelectedTargetCluster != null &&
        !IsMigrating &&
        !IsLoadingData &&
        (SelectedAction != "Restore" || !string.IsNullOrEmpty(SelectedBackupFile));

    [RelayCommand]
    private async Task LoadSourceTopology ()
        {
        if (_sharedConnectionService.SourceConnection == null)
            {
            LogOutput = "Error: No source vCenter connection. Please connect on the Dashboard first.";
            return;
            }

        IsLoadingData = true;
        LoadingStatus = "Loading source vCenter topology...";
        LogOutput = "Starting source topology discovery...\n";

        try
            {
            var connection = _sharedConnectionService.SourceConnection;
            var password = _credentialService.GetPassword(connection);

            if (string.IsNullOrEmpty(password))
                {
                var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                    "Password Required",
                    $"Enter password for {connection.Username}@{connection.ServerAddress}:");

                if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                    LoadingStatus = "Password required to load topology";
                    IsLoadingData = false;
                    return;
                    }
                password = promptedPassword;
                }

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password }
            };

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                _logger.LogInformation("Added BypassModuleCheck for source topology script");
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            // Use the existing Get-EsxiHosts.ps1 script
            var topologyData = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterNode>(
                ".\\Scripts\\Get-EsxiHosts.ps1",
                scriptParams,
                logPath);

            if (topologyData?.Any() == true)
                {
                SourceTopology = new ObservableCollection<ClusterNode>(topologyData);
                LoadingStatus = $"Loaded {SourceTopology.Count} clusters with {SelectedHostCount} total hosts";
                LogOutput += $"Successfully loaded source topology:\n";

                foreach (var cluster in SourceTopology)
                    {
                    LogOutput += $"  - Cluster: {cluster.Name} ({cluster.Hosts.Count} hosts)\n";
                    foreach (var host in cluster.Hosts)
                        {
                        LogOutput += $"    - Host: {host.Name}\n";
                        }
                    }
                }
            else
                {
                LoadingStatus = "No topology data returned from source vCenter";
                LogOutput += "Warning: No clusters or hosts found in source vCenter.\n";
                LoadSampleSourceTopology();
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load source topology");
            LoadingStatus = "Failed to load source topology";
            LogOutput += $"Error loading source topology: {ex.Message}\n";
            LoadSampleSourceTopology();
            }
        finally
            {
            IsLoadingData = false;
            OnPropertyChanged(nameof(SelectedHostCount));
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    [RelayCommand]
    private async Task LoadTargetClusters ()
        {
        if (_sharedConnectionService.TargetConnection == null)
            {
            LogOutput += "Error: No target vCenter connection. Please connect on the Dashboard first.\n";
            return;
            }

        IsLoadingData = true;
        LoadingStatus = "Loading target clusters...";
        LogOutput += "Starting target cluster discovery...\n";

        try
            {
            var connection = _sharedConnectionService.TargetConnection;
            var password = _credentialService.GetPassword(connection);

            if (string.IsNullOrEmpty(password))
                {
                var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                    "Password Required",
                    $"Enter password for {connection.Username}@{connection.ServerAddress}:");

                if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                    LoadingStatus = "Password required to load clusters";
                    IsLoadingData = false;
                    return;
                    }
                password = promptedPassword;
                }

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password }
            };

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                _logger.LogInformation("Added BypassModuleCheck for target clusters script");
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            // Use the existing Get-Clusters.ps1 script
            var clusterData = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(
                ".\\Scripts\\Get-Clusters.ps1",
                scriptParams,
                logPath);

            if (clusterData?.Any() == true)
                {
                TargetClusters = new ObservableCollection<ClusterInfo>(clusterData);
                LoadingStatus = $"Loaded {TargetClusters.Count} target clusters";
                LogOutput += $"Successfully loaded target clusters:\n";

                foreach (var cluster in TargetClusters)
                    {
                    LogOutput += $"  - Target Cluster: {cluster.Name}\n";
                    }
                }
            else
                {
                LoadingStatus = "No clusters found in target vCenter";
                LogOutput += "Warning: No clusters found in target vCenter.\n";
                LoadSampleTargetClusters();
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load target clusters");
            LoadingStatus = "Failed to load target clusters";
            LogOutput += $"Error loading target clusters: {ex.Message}\n";
            LoadSampleTargetClusters();
            }
        finally
            {
            IsLoadingData = false;
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    [RelayCommand]
    private async Task ExecuteHostAction ()
        {
        if (!CanStartMigration)
            {
            LogOutput += "Error: Cannot start action. Check that requirements are met.\n";
            return;
            }

        IsMigrating = true;
        MigrationProgress = 0;
        MigrationStatus = $"Starting {SelectedAction}...";
        LogOutput += $"\n=== STARTING HOST {SelectedAction.ToUpper()} ===\n";

        try
            {
            var selectedHosts = SourceTopology
                .SelectMany(cluster => cluster.Hosts)
                .Where(host => host.IsSelected)
                .ToList();

            LogOutput += $"Processing {selectedHosts.Count} hosts with action: {SelectedAction}\n";
            LogOutput += LogActionOptions();

            double progressIncrement = 100.0 / selectedHosts.Count;

            for (int i = 0; i < selectedHosts.Count; i++)
                {
                var host = selectedHosts[i];
                MigrationStatus = $"{SelectedAction} host {i + 1} of {selectedHosts.Count}: {host.Name}";
                LogOutput += $"[{DateTime.Now:HH:mm:ss}] Starting {SelectedAction} of host: {host.Name}\n";

                try
                    {
                    await ExecuteVMHostConfigAction(host);
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] Completed {SelectedAction} of host: {host.Name}\n";
                    }
                catch (Exception ex)
                    {
                    LogOutput += $"[{DateTime.Now:HH:mm:ss}] ERROR in {SelectedAction} of host {host.Name}: {ex.Message}\n";
                    _logger.LogError(ex, "Error processing host {HostName}", host.Name);
                    }

                MigrationProgress += progressIncrement;
                await Task.Delay(500); // Brief pause for UI updates
                }

            MigrationStatus = $"Host {SelectedAction} completed successfully";
            MigrationProgress = 100;
            LogOutput += $"\n=== HOST {SelectedAction.ToUpper()} COMPLETED ===\n";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Host action failed");
            MigrationStatus = $"Host {SelectedAction} failed";
            LogOutput += $"\nERROR: Host {SelectedAction} failed: {ex.Message}\n";
            }
        finally
            {
            IsMigrating = false;
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    [RelayCommand]
    private void CancelMigration ()
        {
        if (IsMigrating)
            {
            IsMigrating = false;
            MigrationStatus = "Operation cancelled by user";
            LogOutput += $"\n[{DateTime.Now:HH:mm:ss}] Operation cancelled by user\n";
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    [RelayCommand]
    private void BrowseBackupPath ()
        {
        var dialog = new Microsoft.Win32.OpenFolderDialog
            {
            Title = "Select Backup Directory",
            InitialDirectory = BackupPath
            };

        if (dialog.ShowDialog() == true)
            {
            BackupPath = dialog.FolderName;
            }
        }

    [RelayCommand]
    private void BrowseBackupFile ()
        {
        var dialog = new Microsoft.Win32.OpenFileDialog
            {
            Title = "Select Backup File to Restore",
            Filter = "JSON Backup Files (*.json)|*.json|All Files (*.*)|*.*",
            InitialDirectory = BackupPath
            };

        if (dialog.ShowDialog() == true)
            {
            SelectedBackupFile = dialog.FileName;
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    private async Task ExecuteVMHostConfigAction (HostNode host)
        {
        // Ensure backup directory exists
        Directory.CreateDirectory(BackupPath);

        var scriptParams = new Dictionary<string, object>
        {
            { "Action", SelectedAction },
            { "VMHostName", host.Name },
            { "BackupPath", BackupPath },
            { "OperationTimeout", OperationTimeout }
        };

        // Add uplink portgroup name if specified
        if (!string.IsNullOrEmpty(UplinkPortgroupName))
            {
            scriptParams["UplinkPortgroupName"] = UplinkPortgroupName;
            }

        // Add log path
        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
        scriptParams["LogPath"] = logPath;

        // Handle action-specific parameters
        switch (SelectedAction)
            {
            case "Backup":
                await ExecuteBackupAction(scriptParams);
                break;
            case "Restore":
                await ExecuteRestoreAction(scriptParams);
                break;
            case "Migrate":
                await ExecuteMigrateAction(scriptParams);
                break;
            }
        }

    private async Task ExecuteBackupAction (Dictionary<string, object> scriptParams)
        {
        var sourceConnection = _sharedConnectionService.SourceConnection!;
        var password = await GetConnectionPassword(sourceConnection);

        scriptParams["vCenter"] = sourceConnection.ServerAddress;
        scriptParams["Credential"] = CreateCredentialParameter(sourceConnection.Username, password);

        await ExecuteVMHostConfigScript(scriptParams);
        }

    private async Task ExecuteRestoreAction (Dictionary<string, object> scriptParams)
        {
        if (string.IsNullOrEmpty(SelectedBackupFile))
            {
            throw new InvalidOperationException("Backup file must be selected for restore action");
            }

        var targetConnection = _sharedConnectionService.TargetConnection!;
        var password = await GetConnectionPassword(targetConnection);

        scriptParams["vCenter"] = targetConnection.ServerAddress;
        scriptParams["BackupFile"] = SelectedBackupFile;
        scriptParams["Credential"] = CreateCredentialParameter(targetConnection.Username, password);

        await ExecuteVMHostConfigScript(scriptParams);
        }

    private async Task ExecuteMigrateAction (Dictionary<string, object> scriptParams)
        {
        var sourceConnection = _sharedConnectionService.SourceConnection!;
        var targetConnection = _sharedConnectionService.TargetConnection!;

        var sourcePassword = await GetConnectionPassword(sourceConnection);
        var targetPassword = await GetConnectionPassword(targetConnection);

        // Get ESXi host credentials
        var (hostCredResult, hostPassword) = _dialogService.ShowPasswordDialog(
            "ESXi Host Credentials Required",
            $"Enter ESXi root password for direct host connection:");

        if (hostCredResult != true || string.IsNullOrEmpty(hostPassword))
            {
            throw new InvalidOperationException("ESXi host credentials are required for migration");
            }

        scriptParams["SourceVCenter"] = sourceConnection.ServerAddress;
        scriptParams["TargetVCenter"] = targetConnection.ServerAddress;
        scriptParams["SourceCredential"] = CreateCredentialParameter(sourceConnection.Username, sourcePassword);
        scriptParams["TargetCredential"] = CreateCredentialParameter(targetConnection.Username, targetPassword);
        scriptParams["ESXiHostCredential"] = CreateCredentialParameter("root", hostPassword);

        // Add target cluster information
        if (SelectedTargetCluster != null)
            {
            scriptParams["TargetClusterName"] = SelectedTargetCluster.Name;
            }

        if (!string.IsNullOrEmpty(SelectedTargetDatacenter))
            {
            scriptParams["TargetDatacenterName"] = SelectedTargetDatacenter;
            }

        await ExecuteVMHostConfigScript(scriptParams);
        }

    private async Task<string> GetConnectionPassword (VCenterConnection connection)
        {
        var password = _credentialService.GetPassword(connection);

        if (string.IsNullOrEmpty(password))
            {
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {connection.Username}@{connection.ServerAddress}:");

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                throw new InvalidOperationException($"Password required for {connection.ServerAddress}");
                }
            password = promptedPassword;
            }

        return password;
        }

    private string CreateCredentialParameter (string username, string password)
        {
        // For PowerShell script, we'll pass username and password separately
        // The script will create the credential object internally
        return $"{username}:{password}";
        }

    private async Task ExecuteVMHostConfigScript (Dictionary<string, object> scriptParams)
        {
        // Add BypassModuleCheck if PowerCLI is confirmed
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            }

        string result = await _powerShellService.RunScriptOptimizedAsync(
            ".\\Scripts\\Invoke-VMHostConfig.ps1",
            scriptParams);

        LogOutput += $"Script Output:\n{result}\n";

        // Check for success/failure in output
        if (result.Contains("SUCCESS:"))
            {
            LogOutput += "✅ Operation completed successfully\n";
            }
        else if (result.Contains("ERROR:"))
            {
            LogOutput += "❌ Operation encountered errors\n";
            }
        }

    private string LogActionOptions ()
        {
        var options = $"Action options:\n";
        options += $"  - Action: {SelectedAction}\n";
        options += $"  - Backup Path: {BackupPath}\n";
        options += $"  - Operation Timeout: {OperationTimeout} seconds\n";

        if (!string.IsNullOrEmpty(UplinkPortgroupName))
            {
            options += $"  - Uplink Portgroup: {UplinkPortgroupName}\n";
            }

        if (SelectedAction == "Restore" && !string.IsNullOrEmpty(SelectedBackupFile))
            {
            options += $"  - Restore from: {Path.GetFileName(SelectedBackupFile)}\n";
            }

        if (SelectedAction == "Migrate")
            {
            options += $"  - Preserve VM assignments: {PreserveVmAssignments}\n";
            options += $"  - Migrate host profiles: {MigrateHostProfiles}\n";
            options += $"  - Update DRS rules: {UpdateDrsRules}\n";
            options += $"  - Create backup: {CreateBackupBeforeMigration}\n";
            if (SelectedTargetCluster != null)
                {
                options += $"  - Target cluster: {SelectedTargetCluster.Name}\n";
                }
            }

        return options + "\n";
        }

    private void LoadSampleSourceTopology ()
        {
        var sampleTopology = new ObservableCollection<ClusterNode>
        {
            new ClusterNode
            {
                Name = "Production-Cluster-01",
                Hosts = new ObservableCollection<HostNode>
                {
                    new HostNode { Name = "esx-prod-01.lab.local", IsSelected = false },
                    new HostNode { Name = "esx-prod-02.lab.local", IsSelected = false },
                    new HostNode { Name = "esx-prod-03.lab.local", IsSelected = false }
                }
            },
            new ClusterNode
            {
                Name = "Development-Cluster-01",
                Hosts = new ObservableCollection<HostNode>
                {
                    new HostNode { Name = "esx-dev-01.lab.local", IsSelected = false },
                    new HostNode { Name = "esx-dev-02.lab.local", IsSelected = false }
                }
            }
        };

        SourceTopology = sampleTopology;
        LoadingStatus = $"Loaded sample topology - {SourceTopology.Count} clusters";
        LogOutput += "Loaded sample source topology for demonstration.\n";
        }

    private void LoadSampleTargetClusters ()
        {
        var sampleClusters = new ObservableCollection<ClusterInfo>
        {
            new ClusterInfo { Name = "Target-Production-Cluster" },
            new ClusterInfo { Name = "Target-Development-Cluster" },
            new ClusterInfo { Name = "Target-Staging-Cluster" }
        };

        TargetClusters = sampleClusters;
        LoadingStatus = $"Loaded sample target clusters - {TargetClusters.Count} clusters";
        LogOutput += "Loaded sample target clusters for demonstration.\n";
        }

    partial void OnSelectedActionChanged (string value)
        {
        OnPropertyChanged(nameof(CanStartMigration));
        }
    }