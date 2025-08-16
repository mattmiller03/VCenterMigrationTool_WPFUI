using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    private ObservableCollection<ResourcePoolInfo> _sourceResourcePools = new();

    [ObservableProperty]
    private bool _isLoadingData;

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
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _selectedPoolNames = new();

    // Import Options
    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private bool _removeExistingPools = false;

    [ObservableProperty]
    private bool _moveVMsToResourcePools = true;

    [ObservableProperty]
    private string _reportFilePath = string.Empty;

    // Additional properties for UI binding
    [ObservableProperty]
    private bool _isLoadingData;

    [ObservableProperty]
    private ObservableCollection<string> _selectedPoolNames = new();

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

        // Initialize default paths
        var exportPath = _configurationService.GetConfiguration().ExportPath ?? "Exports";
        ExportFilePath = Path.Combine(exportPath, "ResourcePools.json");
        ReportFilePath = Path.Combine(exportPath, "ResourcePoolMigration_Report.html");
        }

    public async Task OnNavigatedToAsync ()
        {
        // Check if we have active connections
        if (_sharedConnectionService.SourceConnection != null && _sharedConnectionService.TargetConnection != null)
            {
            OperationStatus = "Connections available - ready to load data";
            }
        else
            {
            OperationStatus = "Please establish source and target connections on the Dashboard first";
            }

        await Task.CompletedTask;
        }

    public async Task OnNavigatedFromAsync () => await Task.CompletedTask;

    [RelayCommand]
    private async Task LoadSourceClusters ()
        {
        if (_sharedConnectionService.SourceConnection == null)
            {
            LogOutput = "Error: No source vCenter connection. Please connect on the Dashboard first.\n";
            return;
            }

        IsLoadingData = true;
        OperationStatus = "Loading source clusters...";
        LogOutput = "Starting source cluster discovery...\n";

        try
            {
            var connection = _sharedConnectionService.SourceConnection;
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

            var clusters = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(
                ".\\Scripts\\Get-Clusters.ps1",
                scriptParams,
                logPath);

            if (clusters?.Any() == true)
                {
                SourceClusters = new ObservableCollection<ClusterInfo>(clusters);
                OperationStatus = $"Loaded {SourceClusters.Count} source clusters";
                LogOutput += $"Successfully loaded source clusters:\n";
                foreach (var cluster in SourceClusters)
                    {
                    LogOutput += $"  - {cluster.Name}\n";
                    }
                }
            else
                {
                OperationStatus = "No clusters found in source vCenter";
                LogOutput += "Warning: No clusters found in source vCenter.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load source clusters");
            OperationStatus = "Failed to load source clusters";
            LogOutput += $"Error loading source clusters: {ex.Message}\n";
            }
        finally
            {
            IsLoadingData = false;
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
        OperationStatus = "Loading target clusters...";
        LogOutput += "Starting target cluster discovery...\n";

        try
            {
            var connection = _sharedConnectionService.TargetConnection;
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

            var clusters = await _powerShellService.RunScriptAndGetObjectsOptimizedAsync<ClusterInfo>(
                ".\\Scripts\\Get-Clusters.ps1",
                scriptParams,
                logPath);

            if (clusters?.Any() == true)
                {
                TargetClusters = new ObservableCollection<ClusterInfo>(clusters);
                OperationStatus = $"Loaded {TargetClusters.Count} target clusters";
                LogOutput += $"Successfully loaded target clusters:\n";
                foreach (var cluster in TargetClusters)
                    {
                    LogOutput += $"  - {cluster.Name}\n";
                    }
                }
            else
                {
                OperationStatus = "No clusters found in target vCenter";
                LogOutput += "Warning: No clusters found in target vCenter.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load target clusters");
            OperationStatus = "Failed to load target clusters";
            LogOutput += $"Error loading target clusters: {ex.Message}\n";
            }
        finally
            {
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task LoadResourcePools ()
        {
        if (_sharedConnectionService.SourceConnection == null || SelectedSourceCluster == null)
            {
            LogOutput += "Error: Source connection and cluster selection required.\n";
            return;
            }

        IsLoadingData = true;
        OperationStatus = "Loading resource pools...";
        LogOutput += $"Loading resource pools from cluster: {SelectedSourceCluster.Name}...\n";

        try
            {
            var connection = _sharedConnectionService.SourceConnection;
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
                SourceResourcePools = new ObservableCollection<ResourcePoolInfo>(resourcePools);
                OperationStatus = $"Loaded {SourceResourcePools.Count} resource pools";
                LogOutput += $"Successfully loaded resource pools:\n";
                foreach (var pool in SourceResourcePools)
                    {
                    LogOutput += $"  - {pool.Name} ({pool.VmCount} VMs)\n";
                    }
                }
            else
                {
                OperationStatus = "No custom resource pools found";
                LogOutput += "No custom resource pools found in the selected cluster.\n";
                SourceResourcePools.Clear();
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
            IsLoadingData = false;
            }
        }

    [RelayCommand]
    private async Task ExportResourcePools ()
        {
        if (_sharedConnectionService.SourceConnection == null || SelectedSourceCluster == null)
            {
            LogOutput += "Error: Source connection and cluster selection required for export.\n";
            return;
            }

        if (string.IsNullOrWhiteSpace(ExportFilePath))
            {
            LogOutput += "Error: Export file path is required.\n";
            return;
            }

        IsOperationRunning = true;
        OperationProgress = 0;
        OperationStatus = "Exporting resource pools...";
        LogOutput += $"\n=== STARTING RESOURCE POOL EXPORT ===\n";

        try
            {
            var connection = _sharedConnectionService.SourceConnection;
            var password = await GetConnectionPassword(connection);

            // Ensure export directory exists
            var exportDir = Path.GetDirectoryName(ExportFilePath);
            if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
                {
                Directory.CreateDirectory(exportDir);
                }

            var scriptParams = new Dictionary<string, object>
            {
                { "SourceVC", connection.ServerAddress },
                { "SourceCred", CreateCredentialParameter(connection.Username, password) },
                { "OutputJson", ExportFilePath },
                { "ClusterName", SelectedSourceCluster.Name },
                { "LogPath", Path.Combine(_configurationService.GetConfiguration().LogPath ?? "Logs", "ResourcePool-Export.log") }
            };

            if (ExportAllPools)
                {
                scriptParams["All"] = true;
                LogOutput += $"Exporting all resource pools from cluster: {SelectedSourceCluster.Name}\n";
                }
            else
                {
                if (!SelectedPoolNames.Any())
                    {
                    LogOutput += "Error: No resource pools selected for export.\n";
                    return;
                    }
                scriptParams["PoolNames"] = SelectedPoolNames.ToArray();
                LogOutput += $"Exporting selected resource pools: {string.Join(", ", SelectedPoolNames)}\n";
                }

            OperationProgress = 25;
            OperationStatus = "Executing resource pool export script...";

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\ResourcePool-export.ps1",
                scriptParams);

            OperationProgress = 100;
            OperationStatus = "Resource pool export completed";

            LogOutput += "\n=== EXPORT SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== EXPORT COMPLETED ===\n";

            if (result.Contains("Exported") && result.Contains("pool definitions"))
                {
                OperationStatus = "Resource pools exported successfully!";
                LogOutput += $"\n✅ Resource pools exported to timestamped file.\n";
                LogOutput += $"Check the export directory for the timestamped JSON file.\n";
                }
            else
                {
                OperationStatus = "Export completed with issues - check logs";
                LogOutput += $"\n⚠️ Export may have encountered issues. Please review the output above.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Resource pool export failed");
            OperationStatus = "Resource pool export failed";
            LogOutput += $"\nERROR: Resource pool export failed: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private async Task ImportResourcePools ()
        {
        if (_sharedConnectionService.TargetConnection == null || SelectedTargetCluster == null)
            {
            LogOutput += "Error: Target connection and cluster selection required for import.\n";
            return;
            }

        if (string.IsNullOrWhiteSpace(ImportFilePath) || !File.Exists(ImportFilePath))
            {
            LogOutput += "Error: Valid import file path is required.\n";
            return;
            }

        IsOperationRunning = true;
        OperationProgress = 0;
        OperationStatus = "Importing resource pools...";
        LogOutput += $"\n=== STARTING RESOURCE POOL IMPORT ===\n";

        try
            {
            var connection = _sharedConnectionService.TargetConnection;
            var password = await GetConnectionPassword(connection);

            var scriptParams = new Dictionary<string, object>
            {
                { "DestVC", connection.ServerAddress },
                { "DestCred", CreateCredentialParameter(connection.Username, password) },
                { "InputJson", ImportFilePath },
                { "TargetCluster", SelectedTargetCluster.Name },
                { "LogPath", Path.Combine(_configurationService.GetConfiguration().LogPath ?? "Logs", "ResourcePool-Import.log") }
            };

            if (!string.IsNullOrWhiteSpace(ReportFilePath))
                {
                // Ensure report directory exists
                var reportDir = Path.GetDirectoryName(ReportFilePath);
                if (!string.IsNullOrEmpty(reportDir) && !Directory.Exists(reportDir))
                    {
                    Directory.CreateDirectory(reportDir);
                    }
                scriptParams["ReportPath"] = ReportFilePath;
                }

            if (RemoveExistingPools)
                {
                scriptParams["RemoveAllPools"] = true;
                LogOutput += "⚠️ Option enabled: Remove existing resource pools before import\n";
                }

            if (MoveVMsToResourcePools)
                {
                scriptParams["MoveVMs"] = true;
                LogOutput += "📦 Option enabled: Move VMs to their designated resource pools\n";
                }

            LogOutput += $"Target cluster: {SelectedTargetCluster.Name}\n";
            LogOutput += $"Import file: {Path.GetFileName(ImportFilePath)}\n\n";

            OperationProgress = 25;
            OperationStatus = "Executing resource pool import script...";

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\ResourcePool-import.ps1",
                scriptParams);

            OperationProgress = 100;
            OperationStatus = "Resource pool import completed";

            LogOutput += "\n=== IMPORT SCRIPT OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== IMPORT COMPLETED ===\n";

            if (result.Contains("HTML report generated"))
                {
                OperationStatus = "Resource pools imported successfully!";
                LogOutput += $"\n✅ Resource pools imported successfully.\n";
                LogOutput += $"📊 Detailed HTML report generated for review.\n";

                // Extract report path from output if available
                if (result.Contains("HTML report generated at:"))
                    {
                    var reportPathMatch = System.Text.RegularExpressions.Regex.Match(result, @"HTML report generated at: (.+)");
                    if (reportPathMatch.Success)
                        {
                        var actualReportPath = reportPathMatch.Groups[1].Value.Trim();
                        LogOutput += $"Report location: {actualReportPath}\n";
                        }
                    }
                }
            else if (result.Contains("IMPORT SUMMARY"))
                {
                OperationStatus = "Import completed - check report for details";
                LogOutput += $"\n📋 Import process completed. Please review the detailed output above.\n";
                }
            else
                {
                OperationStatus = "Import completed with issues - check logs";
                LogOutput += $"\n⚠️ Import may have encountered issues. Please review the output above.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Resource pool import failed");
            OperationStatus = "Resource pool import failed";
            LogOutput += $"\nERROR: Resource pool import failed: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private void BrowseExportFile ()
        {
        var dialog = new Microsoft.Win32.SaveFileDialog
            {
            Title = "Select Export File Location",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            FileName = "ResourcePools.json",
            InitialDirectory = _configurationService.GetConfiguration().ExportPath ?? "Exports"
            };

        if (dialog.ShowDialog() == true)
            {
            ExportFilePath = dialog.FileName;
            }
        }

    [RelayCommand]
    private void BrowseImportFile ()
        {
        var dialog = new Microsoft.Win32.OpenFileDialog
            {
            Title = "Select Import File",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            InitialDirectory = _configurationService.GetConfiguration().ExportPath ?? "Exports"
            };

        if (dialog.ShowDialog() == true)
            {
            ImportFilePath = dialog.FileName;
            }
        }

    [RelayCommand]
    private void BrowseReportFile ()
        {
        var dialog = new Microsoft.Win32.SaveFileDialog
            {
            Title = "Select Report File Location",
            Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*",
            DefaultExt = "html",
            FileName = $"ResourcePoolMigration_{DateTime.Now:yyyyMMdd_HHmmss}.html",
            InitialDirectory = _configurationService.GetConfiguration().ExportPath ?? "Exports"
            };

        if (dialog.ShowDialog() == true)
            {
            ReportFilePath = dialog.FileName;
            }
        }

    [RelayCommand]
    private void TogglePoolSelection (ResourcePoolInfo? pool)
        {
        if (pool == null) return;

        if (SelectedPoolNames.Contains(pool.Name))
            {
            SelectedPoolNames.Remove(pool.Name);
            }
        else
            {
            SelectedPoolNames.Add(pool.Name);
            }
        }

    [RelayCommand]
    private void SelectAllPools ()
        {
        SelectedPoolNames.Clear();
        foreach (var pool in SourceResourcePools)
            {
            SelectedPoolNames.Add(pool.Name);
            }
        }

    [RelayCommand]
    private void UnselectAllPools ()
        {
        SelectedPoolNames.Clear();
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