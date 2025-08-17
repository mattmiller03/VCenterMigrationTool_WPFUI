using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

// Update your DashboardViewModel to use persistent connections

public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly PersistentExternalConnectionService _persistentConnectionService; // External process-based
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly ConfigurationService _configurationService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<DashboardViewModel> _logger;

    // Observable properties
    [ObservableProperty]
    private string _scriptOutput = "Script output will be displayed here...";

    public ObservableCollection<VCenterConnection> Profiles { get; }

    [ObservableProperty]
    private VCenterConnection? _selectedSourceProfile;

    [ObservableProperty]
    private VCenterConnection? _selectedTargetProfile;

    [ObservableProperty]
    private string _sourceConnectionStatus = "Connection not active";

    [ObservableProperty]
    private string _targetConnectionStatus = "Connection not active";

    [ObservableProperty]
    private bool _isJobRunning;

    [ObservableProperty]
    private string _currentJobText = "No active jobs.";

    [ObservableProperty]
    private int _jobProgress;

    // Constructor with PersistentExternalConnectionService
    public DashboardViewModel (
        HybridPowerShellService powerShellService,
        PersistentExternalConnectionService persistentConnectionService, // External PowerShell processes
        ConnectionProfileService profileService,
        CredentialService credentialService,
        SharedConnectionService sharedConnectionService,
        ConfigurationService configurationService,
        IDialogService dialogService,
        ILogger<DashboardViewModel> logger)
        {
        _powerShellService = powerShellService;
        _persistentConnectionService = persistentConnectionService; // Assign the external service
        _profileService = profileService;
        _credentialService = credentialService;
        _sharedConnectionService = sharedConnectionService;
        _configurationService = configurationService;
        _dialogService = dialogService;
        _logger = logger;

        Profiles = _profileService.Profiles;
        }
        public async Task OnNavigatedToAsync ()
        {
            // Optionally check connection status when navigating to the dashboard
            await CheckConnectionStatus();
        }

        // Clean up connections when navigating away
        public async Task OnNavigatedFromAsync ()
        {
            // Optionally disconnect when leaving dashboard
            // await _persistentConnectionService.DisconnectAllAsync();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Checks if persistent connections are still active
        /// </summary>
        private async Task CheckConnectionStatus ()
            {
            try
                {
                // Check source connection
                var sourceConnected = await _persistentConnectionService.IsConnectedAsync("source");
                if (sourceConnected)
                    {
                    var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("source");
                    if (isConnected && _sharedConnectionService.SourceConnection != null)
                        {
                        SourceConnectionStatus = $"✅ Connected - {_sharedConnectionService.SourceConnection.ServerAddress} (v{version})";
                        }
                    }
                else if (_sharedConnectionService.SourceConnection != null)
                    {
                    // Connection was lost
                    SourceConnectionStatus = "⚠️ Connection lost - reconnect required";
                    _sharedConnectionService.SourceConnection = null;
                    }

                // Check target connection
                var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");
                if (targetConnected)
                    {
                    var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("target");
                    if (isConnected && _sharedConnectionService.TargetConnection != null)
                        {
                        TargetConnectionStatus = $"✅ Connected - {_sharedConnectionService.TargetConnection.ServerAddress} (v{version})";
                        }
                    }
                else if (_sharedConnectionService.TargetConnection != null)
                    {
                    // Connection was lost
                    TargetConnectionStatus = "⚠️ Connection lost - reconnect required";
                    _sharedConnectionService.TargetConnection = null;
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error checking connection status");
                }
            }
        
        [RelayCommand]
        private async Task OnConnectSource ()
            {
            if (SelectedSourceProfile is null) return;

            IsJobRunning = true;
            SourceConnectionStatus = "Establishing persistent connection...";
            ScriptOutput = string.Empty;

            _logger.LogInformation("=== ESTABLISHING PERSISTENT SOURCE CONNECTION ===");

            string? password = _credentialService.GetPassword(SelectedSourceProfile);
            string finalPassword;

            if (string.IsNullOrEmpty(password))
                {
                var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                    "Password Required",
                    $"Enter password for {SelectedSourceProfile.Username}@{SelectedSourceProfile.ServerAddress}:"
                );

                if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                    SourceConnectionStatus = "Connection not active";
                    IsJobRunning = false;
                    return;
                    }

                finalPassword = promptedPassword;
                }
            else
                {
                finalPassword = password;
                }

            try
                {
                // Use the persistent external connection service
                var (success, message, sessionId) = await _persistentConnectionService.ConnectAsync(
                    SelectedSourceProfile,
                    finalPassword,
                    isSource: true,
                    bypassModuleCheck: HybridPowerShellService.PowerCliConfirmedInstalled);

                if (success)
                    {
                    _sharedConnectionService.SourceConnection = SelectedSourceProfile;

                    // Get connection details
                    var (isConnected, sid, version) = _persistentConnectionService.GetConnectionInfo("source");

                    SourceConnectionStatus = $"✅ Connected - {SelectedSourceProfile.ServerAddress} (v{version})";
                    ScriptOutput = $"Persistent connection established!\n" +
                                  $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                                  $"Session ID: {sessionId}\n" +
                                  $"Version: {version}\n\n" +
                                  $"Connection will remain active for all operations.\n" +
                                  $"Use 'Disconnect' to close the connection.";

                    _logger.LogInformation("✅ Persistent source connection established (Session: {SessionId})", sessionId);
                    }
                else
                    {
                    SourceConnectionStatus = "❌ Connection failed";
                    ScriptOutput = $"Connection failed: {message}";
                    _sharedConnectionService.SourceConnection = null;

                    _logger.LogError("Failed to establish persistent connection: {Message}", message);
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error establishing persistent connection");
                SourceConnectionStatus = "❌ Connection error";
                ScriptOutput = $"Error: {ex.Message}";
                }
            finally
                {
                IsJobRunning = false;
                }
            }

        [RelayCommand]
        private async Task OnConnectTarget ()
            {
            if (SelectedTargetProfile is null) return;

            IsJobRunning = true;
            TargetConnectionStatus = "Establishing persistent connection...";
            ScriptOutput = string.Empty;

            _logger.LogInformation("=== ESTABLISHING PERSISTENT TARGET CONNECTION ===");

            string? password = _credentialService.GetPassword(SelectedTargetProfile);
            string finalPassword;

            if (string.IsNullOrEmpty(password))
                {
                var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                    "Password Required",
                    $"Enter password for {SelectedTargetProfile.Username}@{SelectedTargetProfile.ServerAddress}:"
                );

                if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                    TargetConnectionStatus = "Connection not active";
                    IsJobRunning = false;
                    return;
                    }

                finalPassword = promptedPassword;
                }
            else
                {
                finalPassword = password;
                }

            try
                {
                // Use the persistent external connection service
                var (success, message, sessionId) = await _persistentConnectionService.ConnectAsync(
                    SelectedTargetProfile,
                    finalPassword,
                    isSource: false,
                    bypassModuleCheck: HybridPowerShellService.PowerCliConfirmedInstalled);

                if (success)
                    {
                    _sharedConnectionService.TargetConnection = SelectedTargetProfile;

                    // Get connection details
                    var (isConnected, sid, version) = _persistentConnectionService.GetConnectionInfo("target");

                    TargetConnectionStatus = $"✅ Connected - {SelectedTargetProfile.ServerAddress} (v{version})";
                    ScriptOutput = $"Persistent connection established!\n" +
                                  $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                                  $"Session ID: {sessionId}\n" +
                                  $"Version: {version}\n\n" +
                                  $"Connection will remain active for all operations.\n" +
                                  $"Use 'Disconnect' to close the connection.";

                    _logger.LogInformation("✅ Persistent target connection established (Session: {SessionId})", sessionId);
                    }
                else
                    {
                    TargetConnectionStatus = "❌ Connection failed";
                    ScriptOutput = $"Connection failed: {message}";
                    _sharedConnectionService.TargetConnection = null;

                    _logger.LogError("Failed to establish persistent connection: {Message}", message);
                    }
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error establishing persistent connection");
                TargetConnectionStatus = "❌ Connection error";
                ScriptOutput = $"Error: {ex.Message}";
                }
            finally
                {
                IsJobRunning = false;
                }
            }

    // Add disconnect commands
    [RelayCommand]
    private async Task OnDisconnectSource ()
    {
        IsJobRunning = true;
        SourceConnectionStatus = "Disconnecting...";
        ScriptOutput = string.Empty;

        try
        {
            _logger.LogInformation("Disconnecting source vCenter connection...");

            // Disconnect using the persistent connection service
            await _persistentConnectionService.DisconnectAsync("source");

            // Clear the shared connection
            _sharedConnectionService.SourceConnection = null;

            // Update UI
            SourceConnectionStatus = "Connection not active";
            ScriptOutput = "Source vCenter connection has been closed.";

            _logger.LogInformation("✅ Source vCenter connection disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting source vCenter");
            SourceConnectionStatus = "❌ Disconnect error";
            ScriptOutput = $"Failed to disconnect: {ex.Message}";
        }
        finally
        {
            IsJobRunning = false;
        }
    }

    [RelayCommand]
    private async Task OnDisconnectTarget ()
    {
        IsJobRunning = true;
        TargetConnectionStatus = "Disconnecting...";
        ScriptOutput = string.Empty;

        try
        {
            _logger.LogInformation("Disconnecting target vCenter connection...");

            // Disconnect using the persistent connection service
            await _persistentConnectionService.DisconnectAsync("target");

            // Clear the shared connection
            _sharedConnectionService.TargetConnection = null;

            // Update UI
            TargetConnectionStatus = "Connection not active";
            ScriptOutput = "Target vCenter connection has been closed.";

            _logger.LogInformation("✅ Target vCenter connection disconnected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting target vCenter");
            TargetConnectionStatus = "❌ Disconnect error";
            ScriptOutput = $"Failed to disconnect: {ex.Message}";
        }
        finally
        {
            IsJobRunning = false;
        }
    }

    // Update test job to use persistent connection
    [RelayCommand]
        private async Task OnRunTestJob ()
            {
            if (IsJobRunning) return;

            var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("source");
            if (!isConnected)
                {
                ScriptOutput = "Error: Please connect to source vCenter first.";
                return;
                }

            IsJobRunning = true;
            JobProgress = 0;
            CurrentJobText = $"Running commands on persistent connection...";
            ScriptOutput = string.Empty;

            try
                {
                _logger.LogInformation("Running test job on persistent connection (Session: {SessionId})", sessionId);

                // Run commands using the persistent connection
                CurrentJobText = "Getting VM inventory...";
                JobProgress = 25;

                var result = await _persistentConnectionService.ExecuteCommandAsync("source", @"
                Write-Output '=== VM Inventory ==='
                $vms = Get-VM
                Write-Output ""Total VMs: $($vms.Count)""
                Write-Output """"
                Write-Output 'First 5 VMs:'
                $vms | Select-Object -First 5 Name, PowerState, NumCpu, MemoryGB | Format-Table | Out-String
                
                Write-Output '=== Host Information ==='
                $hosts = Get-VMHost
                Write-Output ""Total Hosts: $($hosts.Count)""
                $hosts | Select-Object Name, ConnectionState, PowerState | Format-Table | Out-String
                
                Write-Output '=== Datastore Summary ==='
                $datastores = Get-Datastore
                Write-Output ""Total Datastores: $($datastores.Count)""
                $datastores | Select-Object Name, FreeSpaceGB, CapacityGB | Format-Table | Out-String
            ");

                JobProgress = 100;
                ScriptOutput = result;
                CurrentJobText = "Test job completed successfully.";

                _logger.LogInformation("Test job completed successfully");
                }
            catch (Exception ex)
                {
                _logger.LogError(ex, "Error running test job");
                ScriptOutput = $"Error: {ex.Message}";
                CurrentJobText = "Test job failed.";
                }
            finally
                {
                IsJobRunning = false;
                }
            }

    }