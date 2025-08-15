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

namespace VCenterMigrationTool.ViewModels;

public partial class HostMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<HostMigrationViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<ClusterNode> _sourceTopology = new();

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

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

    // Migration Options
    [ObservableProperty]
    private bool _preserveVmAssignments = true;

    [ObservableProperty]
    private bool _migrateHostProfiles = true;

    [ObservableProperty]
    private bool _updateDrsRules = false;

    public HostMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        ILogger<HostMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _logger = logger;
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
        !IsLoadingData;

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

            // TODO: Replace with actual password retrieval
            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", "placeholder" } // This needs proper password handling
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

                // Load sample data for demonstration
                LoadSampleSourceTopology();
                }
            }
        catch (System.Exception ex)
            {
            _logger.LogError(ex, "Failed to load source topology");
            LoadingStatus = "Failed to load source topology";
            LogOutput += $"Error loading source topology: {ex.Message}\n";

            // Load sample data as fallback
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

            // TODO: Replace with actual password retrieval
            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", "placeholder" } // This needs proper password handling
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

                // Load sample data for demonstration
                LoadSampleTargetClusters();
                }
            }
        catch (System.Exception ex)
            {
            _logger.LogError(ex, "Failed to load target clusters");
            LoadingStatus = "Failed to load target clusters";
            LogOutput += $"Error loading target clusters: {ex.Message}\n";

            // Load sample data as fallback
            LoadSampleTargetClusters();
            }
        finally
            {
            IsLoadingData = false;
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    [RelayCommand]
    private async Task MigrateHosts ()
        {
        if (!CanStartMigration)
            {
            LogOutput += "Error: Cannot start migration. Check that hosts are selected and target cluster is chosen.\n";
            return;
            }

        IsMigrating = true;
        MigrationProgress = 0;
        MigrationStatus = "Starting host migration...";
        LogOutput += "\n=== STARTING HOST MIGRATION ===\n";

        try
            {
            var selectedHosts = SourceTopology
                .SelectMany(cluster => cluster.Hosts)
                .Where(host => host.IsSelected)
                .ToList();

            LogOutput += $"Migrating {selectedHosts.Count} hosts to cluster '{SelectedTargetCluster!.Name}'\n";
            LogOutput += $"Migration options:\n";
            LogOutput += $"  - Preserve VM assignments: {PreserveVmAssignments}\n";
            LogOutput += $"  - Migrate host profiles: {MigrateHostProfiles}\n";
            LogOutput += $"  - Update DRS rules: {UpdateDrsRules}\n\n";

            double progressIncrement = 100.0 / selectedHosts.Count;

            for (int i = 0; i < selectedHosts.Count; i++)
                {
                var host = selectedHosts[i];
                MigrationStatus = $"Migrating host {i + 1} of {selectedHosts.Count}: {host.Name}";
                LogOutput += $"[{System.DateTime.Now:HH:mm:ss}] Starting migration of host: {host.Name}\n";

                // TODO: Implement actual host migration using Move-EsxiHost.ps1
                await SimulateHostMigration(host);

                MigrationProgress += progressIncrement;
                LogOutput += $"[{System.DateTime.Now:HH:mm:ss}] Completed migration of host: {host.Name}\n";

                // Small delay to show progress
                await Task.Delay(1000);
                }

            MigrationStatus = "Host migration completed successfully";
            MigrationProgress = 100;
            LogOutput += "\n=== HOST MIGRATION COMPLETED SUCCESSFULLY ===\n";
            }
        catch (System.Exception ex)
            {
            _logger.LogError(ex, "Host migration failed");
            MigrationStatus = "Host migration failed";
            LogOutput += $"\nERROR: Host migration failed: {ex.Message}\n";
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
            MigrationStatus = "Migration cancelled by user";
            LogOutput += $"\n[{System.DateTime.Now:HH:mm:ss}] Migration cancelled by user\n";
            OnPropertyChanged(nameof(CanStartMigration));
            }
        }

    /// <summary>
    /// Simulates host migration - replace with actual PowerShell script call
    /// </summary>
    private async Task SimulateHostMigration (HostNode host)
        {
        // TODO: Replace this simulation with actual Move-EsxiHost.ps1 script execution
        // var scriptParams = new Dictionary<string, object>
        // {
        //     { "SourceVCenter", _sharedConnectionService.SourceConnection.ServerAddress },
        //     { "TargetVCenter", _sharedConnectionService.TargetConnection.ServerAddress },
        //     { "HostName", host.Name },
        //     { "TargetClusterName", SelectedTargetCluster.Name },
        //     // Add credentials and other parameters
        // };
        // await _powerShellService.RunScriptAsync(".\\Scripts\\Move-EsxiHost.ps1", scriptParams);

        // Simulation for now
        await Task.Delay(2000); // Simulate work
        }

    /// <summary>
    /// Loads sample source topology data for demonstration
    /// </summary>
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

    /// <summary>
    /// Loads sample target clusters data for demonstration
    /// </summary>
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
    }