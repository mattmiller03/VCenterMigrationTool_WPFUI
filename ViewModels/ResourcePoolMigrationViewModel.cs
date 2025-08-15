using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

/// <summary>
/// ViewModel for managing resource pool export and import operations between vCenters
/// </summary>
public partial class ResourcePoolMigrationViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<ResourcePoolMigrationViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _sourceClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedSourceCluster;

    [ObservableProperty]
    private ObservableCollection<ClusterInfo> _targetClusters = new();

    [ObservableProperty]
    private ClusterInfo? _selectedTargetCluster;

    [ObservableProperty]
    private ObservableCollection<ResourcePoolInfo> _availableResourcePools = new();

    [ObservableProperty]
    private ObservableCollection<string> _exportFiles = new();

    [ObservableProperty]
    private string? _selectedExportFile;

    [ObservableProperty]
    private bool _isOperationRunning;

    [ObservableProperty]
    private double _operationProgress;

    [ObservableProperty]
    private string _operationStatus = "Ready to manage resource pools";

    [ObservableProperty]
    private string _logOutput = "Resource pool migration log will appear here...";

    // Export Options
    [ObservableProperty]
    private bool _exportAllPools = true;

    [ObservableProperty]
    private string _specificPoolNames = string.Empty;

    [ObservableProperty]
    private string _exportFileName = "ResourcePools";

    // Import Options
    [ObservableProperty]
    private bool _removeExistingPools = false;

    [ObservableProperty]
    private bool _moveVMsToResourcePools = true;

    [ObservableProperty]
    private string _lastReportPath = string.Empty;

    public ResourcePoolMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        IDialogService dialogService,
        ILogger<ResourcePoolMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _dialogService = dialogService;
        _logger = logger;
        }

    public async Task OnNavigatedToAsync ()
        {
        // Check if we have active connections
        if (_sharedConnectionService.SourceConnection != null && _sharedConnectionService.TargetConnection != null)
            {
            OperationStatus = "Connections available - ready to load data";
            await LoadClusters();
            }
        else
            {
            OperationStatus = "Please establish source and target connections on the Dashboard first";
            }

        // Load existing export files
        LoadExportFiles();
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task LoadClusters ()
        {
        if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
            {
            LogOutput += "Error: Both source and target vCenter connections are required.\n";
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationStatus = "Loading clusters from both vCenters...";
            LogOutput += "Loading cluster information...\n";

            // Load source clusters
            await LoadClustersFromConnection(_sharedConnectionService.SourceConnection, SourceClusters, "source");

            // Load target clusters
            await LoadClustersFromConnection(_sharedConnectionService.TargetConnection, TargetClusters, "target");

            OperationStatus = $"Loaded {SourceClusters.Count} source clusters and {TargetClusters.Count} target clusters";
            LogOutput += $"Cluster loading completed successfully.\n";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load clusters");
            OperationStatus = "Failed to load clusters";
            LogOutput += $"Error loading clusters: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private async Task LoadResourcePools ()
        {
        if (SelectedSourceCluster == null)
            {
            LogOutput += "Error: Please select a source cluster first.\n";
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationStatus = "Loading resource pools from source cluster...";
            LogOutput += $"Loading resource pools from cluster: {SelectedSourceCluster.Name}\n";

            var connection = _sharedConnectionService.SourceConnection!;
            var password = await GetConnectionPassword(connection);

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password },
                { "ClusterName", SelectedSourceCluster.Name }
            };

            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            var resourcePools = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ResourcePoolInfo>(
                ".\\Scripts\\Get-ResourcePools.ps1",
                scriptParams,
                logPath);

            if (resourcePools?.Any() == true)
                {
                AvailableResourcePools = new ObservableCollection<ResourcePoolInfo>(resourcePools.OrderBy(rp => rp.Name));
                OperationStatus = $"Loaded {AvailableResourcePools.Count} resource pools";
                LogOutput += $"Found {AvailableResourcePools.Count} resource pools:\n";

                foreach (var pool in AvailableResourcePools.Take(10))
                    {
                    LogOutput += $"  - {pool.Name} (Parent: {pool.ParentName})\n";
                    }

                if (AvailableResourcePools.Count > 10)
                    {
                    LogOutput += $"  ... and {AvailableResourcePools.Count - 10} more pools\n";
                    }
                }
            else
                {
                OperationStatus = "No custom resource pools found in selected cluster";
                LogOutput += "No custom resource pools found (excluding built-in pools).\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load resource pools");
            OperationStatus = "Failed to load resource pools";
            LogOutput += $"Error loading resource pools: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private async Task ExportResourcePools ()
        {
        if (SelectedSourceCluster == null)
            {
            LogOutput += "Error: Please select a source cluster for export.\n";
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationProgress = 0;
            OperationStatus = "Exporting resource pools...";
            LogOutput += $"\n=== STARTING RESOURCE POOL EXPORT ===\n";
            LogOutput += $"Source Cluster: {SelectedSourceCluster.Name}\n";

            var connection = _sharedConnectionService.SourceConnection!;
            var password = await GetConnectionPassword(connection);

            var exportPath = _configurationService.GetConfiguration().ExportPath ?? "Exports";
            Directory.CreateDirectory(exportPath);

            var outputJsonPath = Path.Combine(exportPath, $"{ExportFileName}.json");

            var scriptParams = new Dictionary<string, object>
            {
                { "SourceVC", connection.ServerAddress },
                { "SourceCred", CreateCredentialParameter(connection.Username, password) },
                { "OutputJson", outputJsonPath },
                { "LogPath", Path.Combine(_configurationService.GetConfiguration().LogPath ?? "Logs", "ResourcePoolExport.log") }
            };

            OperationProgress = 25;

            if (ExportAllPools)
                {
                scriptParams["All"] = true;
                scriptParams["ClusterName"] = SelectedSourceCluster.Name;
                LogOutput += "Export mode: All custom resource pools\n";
                }
            else if (!string.IsNullOrWhiteSpace(SpecificPoolNames))
                {
                var poolNames = SpecificPoolNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .ToArray();
                scriptParams["PoolNames"] = poolNames;
                LogOutput += $"Export mode: Specific pools - {string.Join(", ", poolNames)}\n";
                }
            else
                {
                LogOutput += "Error: Please specify either 'All Pools' or provide specific pool names.\n";
                return;
                }

            OperationProgress = 50;
            OperationStatus = "Executing export script...";

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\ResourcePool-export.ps1",
                scriptParams);

            OperationProgress = 100;
            OperationStatus = "Resource pool export completed";

            LogOutput += "\n=== EXPORT SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== EXPORT COMPLETED ===\n";

            // Refresh export files list
            LoadExportFiles();

            if (result.Contains("Exported") && result.Contains("pool definitions"))
                {
                LogOutput += "\n✅ Resource pools exported successfully!\n";
                OperationStatus = "Export completed successfully";
                }
            else
                {
                LogOutput += "\n⚠️ Export may have encountered issues. Please review the log.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Resource pool export failed");
            OperationStatus = "Resource pool export failed";
            LogOutput += $"\nERROR: Export failed: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private async Task ImportResourcePools ()
        {
        if (SelectedTargetCluster == null)
            {
            LogOutput += "Error: Please select a target cluster for import.\n";
            return;
            }

        if (string.IsNullOrEmpty(SelectedExportFile))
            {
            LogOutput += "Error: Please select an export file to import.\n";
            return;
            }

        try
            {
            IsOperationRunning = true;
            OperationProgress = 0;
            OperationStatus = "Importing resource pools...";
            LogOutput += $"\n=== STARTING RESOURCE POOL IMPORT ===\n";
            LogOutput += $"Target Cluster: {SelectedTargetCluster.Name}\n";
            LogOutput += $"Import File: {Path.GetFileName(SelectedExportFile)}\n";
            LogOutput += $"Remove Existing Pools: {RemoveExistingPools}\n";
            LogOutput += $"Move VMs to Pools: {MoveVMsToResourcePools}\n";

            var connection = _sharedConnectionService.TargetConnection!;
            var password = await GetConnectionPassword(connection);

            var reportsPath = Path.Combine(_configurationService.GetConfiguration().ExportPath ?? "Exports", "Reports");
            Directory.CreateDirectory(reportsPath);

            var reportPath = Path.Combine(reportsPath, $"ResourcePoolMigration_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            var scriptParams = new Dictionary<string, object>
            {
                { "DestVC", connection.ServerAddress },
                { "DestCred", CreateCredentialParameter(connection.Username, password) },
                { "InputJson", SelectedExportFile },
                { "TargetCluster", SelectedTargetCluster.Name },
                { "LogPath", Path.Combine(_configurationService.GetConfiguration().LogPath ?? "Logs", "ResourcePoolImport.log") },
                { "ReportPath", reportPath }
            };

            if (RemoveExistingPools)
                {
                scriptParams["RemoveAllPools"] = true;
                }

            if (MoveVMsToResourcePools)
                {
                scriptParams["MoveVMs"] = true;
                }

            OperationProgress = 25;
            OperationStatus = "Executing import script...";

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\ResourcePool-import.ps1",
                scriptParams);

            OperationProgress = 100;
            OperationStatus = "Resource pool import completed";

            LogOutput += "\n=== IMPORT SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== IMPORT COMPLETED ===\n";

            // Store report path for opening later
            LastReportPath = reportPath;

            if (result.Contains("Pools Created:") || result.Contains("HTML report generated"))
                {
                LogOutput += "\n✅ Resource pool import completed!\n";
                LogOutput += $"📊 HTML report generated: {Path.GetFileName(reportPath)}\n";
                OperationStatus = "Import completed successfully";
                }
            else
                {
                LogOutput += "\n⚠️ Import may have encountered issues. Please review the log.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Resource pool import failed");
            OperationStatus = "Resource pool import failed";
            LogOutput += $"\nERROR: Import failed: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private void BrowseExportFile ()
        {
        var dialog = new Microsoft.Win32.OpenFileDialog
            {
            Title = "Select Resource Pool Export File",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            InitialDirectory = _configurationService.GetConfiguration().ExportPath ?? "Exports"
            };

        if (dialog.ShowDialog() == true)
            {
            SelectedExportFile = dialog.FileName;
            }
        }

    [RelayCommand]
    private void OpenReport ()
        {
        if (!string.IsNullOrEmpty(LastReportPath) && File.Exists(LastReportPath))
            {
            try
                {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                    FileName = LastReportPath,
                    UseShellExecute = true
                    });
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Failed to open report");
                LogOutput += $"Error opening report: {ex.Message}\n";
                }
            }
        else
            {
            LogOutput += "No report available to open.\n";
            }
        }

    [RelayCommand]
    private void RefreshExportFiles ()
        {
        LoadExportFiles();
        LogOutput += "Export files list refreshed.\n";
        }

    private async Task LoadClustersFromConnection (VCenterConnection connection, ObservableCollection<ClusterInfo> clusters, string connectionType)
        {
        try
            {
            var password = await GetConnectionPassword(connection);

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", connection.ServerAddress },
                { "Username", connection.Username },
                { "Password", password }
            };

            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                scriptParams["BypassModuleCheck"] = true;
                }

            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";

            var clusterData = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(
                ".\\Scripts\\Get-Clusters.ps1",
                scriptParams,
                logPath);

            if (clusterData?.Any() == true)
                {
                clusters.Clear();
                foreach (var cluster in clusterData.OrderBy(c => c.Name))
                    {
                    clusters.Add(cluster);
                    }

                LogOutput += $"Loaded {clusters.Count} {connectionType} clusters: {string.Join(", ", clusters.Select(c => c.Name))}\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load {ConnectionType} clusters", connectionType);
            LogOutput += $"Error loading {connectionType} clusters: {ex.Message}\n";
            }
        }

    private void LoadExportFiles ()
        {
        try
            {
            var exportPath = _configurationService.GetConfiguration().ExportPath ?? "Exports";

            if (Directory.Exists(exportPath))
                {
                var jsonFiles = Directory.GetFiles(exportPath, "*ResourcePool*.json", SearchOption.AllDirectories)
                    .Union(Directory.GetFiles(exportPath, "*pool*.json", SearchOption.AllDirectories))
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToArray();

                ExportFiles.Clear();
                foreach (var file in jsonFiles)
                    {
                    ExportFiles.Add(file);
                    }

                if (ExportFiles.Any())
                    {
                    SelectedExportFile = ExportFiles.First();
                    }
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load export files");
            LogOutput += $"Error loading export files: {ex.Message}\n";
            }
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
    }