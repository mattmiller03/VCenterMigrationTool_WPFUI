using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels;

/// <summary>
/// ViewModel for managing VM folder structure migration between vCenters
/// </summary>
public partial class FolderStructureMigrationViewModel : ObservableObject
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly CredentialService _credentialService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<FolderStructureMigrationViewModel> _logger;

    [ObservableProperty]
    private string _sourceDatacenterName = string.Empty;

    [ObservableProperty]
    private string _targetDatacenterName = string.Empty;

    [ObservableProperty]
    private bool _isOperationRunning;

    [ObservableProperty]
    private double _operationProgress;

    [ObservableProperty]
    private string _operationStatus = "Ready to copy folder structure";

    [ObservableProperty]
    private string _logOutput = "Folder structure migration log will appear here...";

    public FolderStructureMigrationViewModel (
        HybridPowerShellService powerShellService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        CredentialService credentialService,
        IDialogService dialogService,
        ILogger<FolderStructureMigrationViewModel> logger)
        {
        _powerShellService = powerShellService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _credentialService = credentialService;
        _dialogService = dialogService;
        _logger = logger;
        }

    [RelayCommand]
    private async Task CopyFolderStructure ()
        {
        if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
            {
            LogOutput = "Error: Both source and target vCenter connections are required.\n";
            return;
            }

        if (string.IsNullOrWhiteSpace(SourceDatacenterName) || string.IsNullOrWhiteSpace(TargetDatacenterName))
            {
            LogOutput = "Error: Both source and target datacenter names are required.\n";
            return;
            }

        IsOperationRunning = true;
        OperationProgress = 0;
        OperationStatus = "Copying VM folder structure...";
        LogOutput = "Starting folder structure copy operation...\n";

        try
            {
            var sourcePassword = await GetConnectionPassword(_sharedConnectionService.SourceConnection);
            var targetPassword = await GetConnectionPassword(_sharedConnectionService.TargetConnection);

            var scriptParams = new Dictionary<string, object>
            {
                { "SourceVCenter", _sharedConnectionService.SourceConnection.ServerAddress },
                { "TargetVCenter", _sharedConnectionService.TargetConnection.ServerAddress },
                { "SourceDatacenterName", SourceDatacenterName },
                { "TargetDatacenterName", TargetDatacenterName },
                { "SourceUser", _sharedConnectionService.SourceConnection.Username },
                { "TargetUser", _sharedConnectionService.TargetConnection.Username },
                { "SourcePassword", ConvertToSecureString(sourcePassword) },
                { "TargetPassword", ConvertToSecureString(targetPassword) }
            };

            // Add BypassModuleCheck if PowerCLI is confirmed
            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                // The copy script doesn't have this parameter, but we can add it for consistency
                _logger.LogInformation("PowerCLI confirmed installed for folder structure copy");
                }

            LogOutput += $"Source: {_sharedConnectionService.SourceConnection.ServerAddress} -> Datacenter: {SourceDatacenterName}\n";
            LogOutput += $"Target: {_sharedConnectionService.TargetConnection.ServerAddress} -> Datacenter: {TargetDatacenterName}\n\n";

            OperationProgress = 25;
            OperationStatus = "Executing folder structure copy script...";

            string result = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\Active\\copy-vmfolderstructure.ps1",
                scriptParams);

            OperationProgress = 100;
            OperationStatus = "Folder structure copy completed";

            LogOutput += "\n=== FOLDER STRUCTURE COPY OUTPUT ===\n";
            LogOutput += result + "\n";
            LogOutput += "=== OPERATION COMPLETED ===\n";

            if (result.Contains("Script finished.") && !result.Contains("An error occurred"))
                {
                OperationStatus = "Folder structure copied successfully!";
                LogOutput += "\n✅ Folder structure has been successfully copied to the target vCenter.\n";
                }
            else if (result.Contains("An error occurred"))
                {
                OperationStatus = "Folder structure copy completed with errors";
                LogOutput += "\n⚠️ Operation completed but some errors occurred. Please review the log.\n";
                }
            else
                {
                OperationStatus = "Folder structure copy process completed";
                LogOutput += "\n📋 Operation completed. Please verify the results in the target vCenter.\n";
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Folder structure copy failed");
            OperationStatus = "Folder structure copy failed";
            LogOutput += $"\nERROR: Folder structure copy failed: {ex.Message}\n";
            }
        finally
            {
            IsOperationRunning = false;
            }
        }

    [RelayCommand]
    private async Task LoadDatacenterNames ()
        {
        if (_sharedConnectionService.SourceConnection == null || _sharedConnectionService.TargetConnection == null)
            {
            LogOutput = "Error: Both source and target vCenter connections are required to load datacenter names.\n";
            return;
            }

        try
            {
            LogOutput += "Loading available datacenter names...\n";

            // Get source datacenters
            var sourcePassword = await GetConnectionPassword(_sharedConnectionService.SourceConnection);
            var sourceParams = new Dictionary<string, object>
            {
                { "VCenterServer", _sharedConnectionService.SourceConnection.ServerAddress },
                { "Username", _sharedConnectionService.SourceConnection.Username },
                { "Password", sourcePassword }
            };

            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                sourceParams["BypassModuleCheck"] = true;
                }

            var sourceResult = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\Active\\Infrastructure Discovery\\Get-Datacenters.ps1",
                sourceParams);

            // Get target datacenters
            var targetPassword = await GetConnectionPassword(_sharedConnectionService.TargetConnection);
            var targetParams = new Dictionary<string, object>
            {
                { "VCenterServer", _sharedConnectionService.TargetConnection.ServerAddress },
                { "Username", _sharedConnectionService.TargetConnection.Username },
                { "Password", targetPassword }
            };

            if (HybridPowerShellService.PowerCliConfirmedInstalled)
                {
                targetParams["BypassModuleCheck"] = true;
                }

            var targetResult = await _powerShellService.RunScriptOptimizedAsync(
                ".\\Scripts\\Active\\Infrastructure Discovery\\Get-Datacenters.ps1",
                targetParams);

            LogOutput += $"Source vCenter datacenters: {sourceResult}\n";
            LogOutput += $"Target vCenter datacenters: {targetResult}\n";
            LogOutput += "Use the datacenter names exactly as shown above.\n\n";
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Failed to load datacenter names");
            LogOutput += $"Error loading datacenter names: {ex.Message}\n";
            }
        }

    private Task<string> GetConnectionPassword (VCenterConnection connection)
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

        return Task.FromResult(password);
        }

    private System.Security.SecureString ConvertToSecureString (string password)
        {
        var secureString = new System.Security.SecureString();
        foreach (char c in password)
            {
            secureString.AppendChar(c);
            }
        secureString.MakeReadOnly();
        return secureString;
        }
    }