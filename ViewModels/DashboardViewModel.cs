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
using System.Windows.Media;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
    private readonly HybridPowerShellService _powerShellService;
    private readonly PersistentExternalConnectionService _persistentConnectionService;
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly SharedConnectionService _sharedConnectionService;
    private readonly VCenterInventoryService _inventoryService;
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
    private string _currentJobText = "No active jobs.";

    [ObservableProperty]
    private int _jobProgress;

    // Connection state properties
    [ObservableProperty]
    private bool _isSourceConnected;

    [ObservableProperty]
    private bool _isTargetConnected;

    [ObservableProperty]
    private bool _isJobRunning;

    [ObservableProperty]
    private string _sourceConnectionDetails = "";

    [ObservableProperty]
    private string _targetConnectionDetails = "";

    // Inventory summary properties
    [ObservableProperty]
    private string _sourceInventorySummary = "No inventory loaded";

    [ObservableProperty]
    private string _targetInventorySummary = "No inventory loaded";

    [ObservableProperty]
    private bool _hasSourceInventory;

    [ObservableProperty]
    private bool _hasTargetInventory;

    // Computed properties for UI binding
    public bool IsSourceDisconnected => !IsSourceConnected;
    public bool IsTargetDisconnected => !IsTargetConnected;
    public bool CanConnectSource => SelectedSourceProfile != null && !IsSourceConnected && !IsJobRunning;
    public bool CanConnectTarget => SelectedTargetProfile != null && !IsTargetConnected && !IsJobRunning;
    public bool CanDisconnectSource => IsSourceConnected && !IsJobRunning;
    public bool CanDisconnectTarget => IsTargetConnected && !IsJobRunning;

    // Connection status visual properties
    public Brush SourceConnectionBackgroundBrush => IsSourceConnected 
        ? new SolidColorBrush(Color.FromRgb(220, 255, 220)) // Light green
        : new SolidColorBrush(Color.FromRgb(255, 245, 245)); // Light red

    public Brush SourceConnectionBorderBrush => IsSourceConnected 
        ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) // Light green border
        : new SolidColorBrush(Color.FromRgb(255, 182, 193)); // Light pink border

    public Brush SourceConnectionTextBrush => IsSourceConnected 
        ? new SolidColorBrush(Color.FromRgb(0, 100, 0)) // Dark green text
        : new SolidColorBrush(Color.FromRgb(139, 69, 19)); // Brown text

    public Brush TargetConnectionBackgroundBrush => IsTargetConnected 
        ? new SolidColorBrush(Color.FromRgb(220, 255, 220)) // Light green
        : new SolidColorBrush(Color.FromRgb(255, 245, 245)); // Light red

    public Brush TargetConnectionBorderBrush => IsTargetConnected 
        ? new SolidColorBrush(Color.FromRgb(144, 238, 144)) // Light green border
        : new SolidColorBrush(Color.FromRgb(255, 182, 193)); // Light pink border

    public Brush TargetConnectionTextBrush => IsTargetConnected 
        ? new SolidColorBrush(Color.FromRgb(0, 100, 0)) // Dark green text
        : new SolidColorBrush(Color.FromRgb(139, 69, 19)); // Brown text

    // Helper methods to notify all related connection properties
    private void NotifySourceConnectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsSourceDisconnected));
        OnPropertyChanged(nameof(CanConnectSource));
        OnPropertyChanged(nameof(CanDisconnectSource));
        OnPropertyChanged(nameof(SourceConnectionBackgroundBrush));
        OnPropertyChanged(nameof(SourceConnectionBorderBrush));
        OnPropertyChanged(nameof(SourceConnectionTextBrush));
    }

    private void NotifyTargetConnectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsTargetDisconnected));
        OnPropertyChanged(nameof(CanConnectTarget));
        OnPropertyChanged(nameof(CanDisconnectTarget));
        OnPropertyChanged(nameof(TargetConnectionBackgroundBrush));
        OnPropertyChanged(nameof(TargetConnectionBorderBrush));
        OnPropertyChanged(nameof(TargetConnectionTextBrush));
    }

    private void NotifyJobStatePropertiesChanged()
    {
        OnPropertyChanged(nameof(CanConnectSource));
        OnPropertyChanged(nameof(CanConnectTarget));
        OnPropertyChanged(nameof(CanDisconnectSource));
        OnPropertyChanged(nameof(CanDisconnectTarget));
    }

    // Partial methods for ObservableProperty change notifications
    partial void OnIsSourceConnectedChanged(bool value)
    {
        NotifySourceConnectionPropertiesChanged();
    }

    partial void OnIsTargetConnectedChanged(bool value)
    {
        NotifyTargetConnectionPropertiesChanged();
    }

    public DashboardViewModel (
        HybridPowerShellService powerShellService,
        PersistentExternalConnectionService persistentConnectionService,
        ConnectionProfileService profileService,
        CredentialService credentialService,
        SharedConnectionService sharedConnectionService,
        VCenterInventoryService inventoryService,
        ConfigurationService configurationService,
        IDialogService dialogService,
        ILogger<DashboardViewModel> logger)
        {
        _powerShellService = powerShellService;
        _persistentConnectionService = persistentConnectionService;
        _profileService = profileService;
        _credentialService = credentialService;
        _sharedConnectionService = sharedConnectionService;
        _inventoryService = inventoryService;
        _configurationService = configurationService;
        _dialogService = dialogService;
        _logger = logger;

        Profiles = _profileService.Profiles;
        }

    public async Task OnNavigatedToAsync ()
        {
        await CheckConnectionStatus();
        UpdateInventorySummaries();
        }

    public async Task OnNavigatedFromAsync ()
        {
        // Keep connections alive when navigating away
        await Task.CompletedTask;
        }

    /// <summary>
    /// Updates inventory summaries for both source and target vCenters
    /// </summary>
    private void UpdateInventorySummaries()
    {
        // Update source inventory summary
        var sourceInventory = _sharedConnectionService.GetSourceInventory();
        if (sourceInventory != null && _sharedConnectionService.SourceConnection != null)
        {
            HasSourceInventory = true;
            var stats = sourceInventory.Statistics;
            SourceInventorySummary = $"{stats.DatacenterCount} DCs • {stats.ClusterCount} Clusters • {stats.HostCount} Hosts • {stats.VirtualMachineCount} VMs • {stats.DatastoreCount} Datastores\n" +
                                   $"Resources: {stats.TotalCpuGhz:F1} GHz CPU • {stats.TotalMemoryGB:F0} GB RAM • {stats.TotalDatastoreCapacityGB:F0} GB Storage\n" +
                                   $"Last Updated: {sourceInventory.LastUpdated:yyyy-MM-dd HH:mm:ss}";
        }
        else
        {
            HasSourceInventory = false;
            SourceInventorySummary = IsSourceConnected ? "Loading inventory..." : "No inventory loaded";
        }

        // Update target inventory summary
        var targetInventory = _sharedConnectionService.GetTargetInventory();
        if (targetInventory != null && _sharedConnectionService.TargetConnection != null)
        {
            HasTargetInventory = true;
            var stats = targetInventory.Statistics;
            TargetInventorySummary = $"{stats.DatacenterCount} DCs • {stats.ClusterCount} Clusters • {stats.HostCount} Hosts • {stats.VirtualMachineCount} VMs • {stats.DatastoreCount} Datastores\n" +
                                   $"Resources: {stats.TotalCpuGhz:F1} GHz CPU • {stats.TotalMemoryGB:F0} GB RAM • {stats.TotalDatastoreCapacityGB:F0} GB Storage\n" +
                                   $"Last Updated: {targetInventory.LastUpdated:yyyy-MM-dd HH:mm:ss}";
        }
        else
        {
            HasTargetInventory = false;
            TargetInventorySummary = IsTargetConnected ? "Loading inventory..." : "No inventory loaded";
        }
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
                    IsSourceConnected = true;
                    SourceConnectionStatus = $"✅ Connected - {_sharedConnectionService.SourceConnection.ServerAddress} (v{version})";
                    SourceConnectionDetails = $"Session: {sessionId}\nVersion: {version}";
                    }
                }
            else if (_sharedConnectionService.SourceConnection != null)
                {
                // Connection was lost
                IsSourceConnected = false;
                SourceConnectionStatus = "⚠️ Connection lost - reconnect required";
                SourceConnectionDetails = "";
                _sharedConnectionService.SourceConnection = null;
                }

            // Check target connection
            var targetConnected = await _persistentConnectionService.IsConnectedAsync("target");
            if (targetConnected)
                {
                var (isConnected, sessionId, version) = _persistentConnectionService.GetConnectionInfo("target");
                if (isConnected && _sharedConnectionService.TargetConnection != null)
                    {
                    IsTargetConnected = true;
                    TargetConnectionStatus = $"✅ Connected - {_sharedConnectionService.TargetConnection.ServerAddress} (v{version})";
                    TargetConnectionDetails = $"Session: {sessionId}\nVersion: {version}";
                    }
                }
            else if (_sharedConnectionService.TargetConnection != null)
                {
                // Connection was lost
                IsTargetConnected = false;
                TargetConnectionStatus = "⚠️ Connection lost - reconnect required";
                TargetConnectionDetails = "";
                _sharedConnectionService.TargetConnection = null;
                }

            // Notify UI of property changes
            OnPropertyChanged(nameof(IsSourceDisconnected));
            OnPropertyChanged(nameof(IsTargetDisconnected));
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
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
            var (success, message, sessionId) = await _persistentConnectionService.ConnectAsync(
                SelectedSourceProfile,
                finalPassword,
                isSource: true,
                bypassModuleCheck: HybridPowerShellService.PowerCliConfirmedInstalled);

            if (success)
                {
                // Use the new inventory-enabled method
                var inventoryLoaded = await _sharedConnectionService.SetSourceConnectionAsync(SelectedSourceProfile);

                var (isConnected, sid, version) = _persistentConnectionService.GetConnectionInfo("source");

                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ Connected - {SelectedSourceProfile.ServerAddress} (v{version})";
                SourceConnectionDetails = $"Session: {sessionId}\nVersion: {version}";

                var inventoryMessage = inventoryLoaded ? "\n✅ vCenter inventory loaded successfully!" : "\n⚠️  Inventory loading failed";
                
                ScriptOutput = $"Persistent connection established!\n" +
                              $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                              $"Session ID: {sessionId}\n" +
                              $"Version: {version}{inventoryMessage}\n\n" +
                              $"Connection will remain active for all operations.\n" +
                              $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Persistent source connection established (Session: {SessionId})", sessionId);

                // Update inventory summary
                UpdateInventorySummaries();

                // Notify property changes
                OnPropertyChanged(nameof(IsSourceDisconnected));
                OnPropertyChanged(nameof(CanConnectSource));
                }
            else
                {
                IsSourceConnected = false;
                SourceConnectionStatus = "❌ Connection failed";
                SourceConnectionDetails = "";
                ScriptOutput = $"Connection failed: {message}";
                _sharedConnectionService.SourceConnection = null;

                _logger.LogError("Failed to establish persistent connection: {Message}", message);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error establishing persistent connection");
            IsSourceConnected = false;
            SourceConnectionStatus = "❌ Connection error";
            SourceConnectionDetails = "";
            ScriptOutput = $"Error: {ex.Message}";
            }
        finally
            {
            IsJobRunning = false;
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
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
            var (success, message, sessionId) = await _persistentConnectionService.ConnectAsync(
                SelectedTargetProfile,
                finalPassword,
                isSource: false,
                bypassModuleCheck: HybridPowerShellService.PowerCliConfirmedInstalled);

            if (success)
                {
                // Use the new inventory-enabled method
                var inventoryLoaded = await _sharedConnectionService.SetTargetConnectionAsync(SelectedTargetProfile);

                var (isConnected, sid, version) = _persistentConnectionService.GetConnectionInfo("target");

                IsTargetConnected = true;
                TargetConnectionStatus = $"✅ Connected - {SelectedTargetProfile.ServerAddress} (v{version})";
                TargetConnectionDetails = $"Session: {sessionId}\nVersion: {version}";

                var inventoryMessage = inventoryLoaded ? "\n✅ vCenter inventory loaded successfully!" : "\n⚠️  Inventory loading failed";

                ScriptOutput = $"Persistent connection established!\n" +
                              $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                              $"Session ID: {sessionId}\n" +
                              $"Version: {version}{inventoryMessage}\n\n" +
                              $"Connection will remain active for all operations.\n" +
                              $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Persistent target connection established (Session: {SessionId})", sessionId);

                // Update inventory summary
                UpdateInventorySummaries();

                // Notify property changes
                OnPropertyChanged(nameof(IsTargetDisconnected));
                OnPropertyChanged(nameof(CanConnectTarget));
                }
            else
                {
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Connection failed";
                TargetConnectionDetails = "";
                ScriptOutput = $"Connection failed: {message}";
                _sharedConnectionService.TargetConnection = null;

                _logger.LogError("Failed to establish persistent connection: {Message}", message);
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error establishing persistent connection");
            IsTargetConnected = false;
            TargetConnectionStatus = "❌ Connection error";
            TargetConnectionDetails = "";
            ScriptOutput = $"Error: {ex.Message}";
            }
        finally
            {
            IsJobRunning = false;
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
            }
        }

    [RelayCommand]
    private async Task OnDisconnectSource ()
        {
        IsJobRunning = true;
        SourceConnectionStatus = "Disconnecting...";
        ScriptOutput = string.Empty;

        try
            {
            _logger.LogInformation("Disconnecting source vCenter connection...");

            await _persistentConnectionService.DisconnectAsync("source");

            _sharedConnectionService.ClearSourceConnection();

            IsSourceConnected = false;
            SourceConnectionStatus = "Connection not active";
            SourceConnectionDetails = "";
            ScriptOutput = "Source vCenter connection has been closed.";

            _logger.LogInformation("✅ Source vCenter connection disconnected successfully");

            // Notify property changes
            OnPropertyChanged(nameof(IsSourceDisconnected));
            OnPropertyChanged(nameof(CanConnectSource));
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
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
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

            await _persistentConnectionService.DisconnectAsync("target");

            _sharedConnectionService.ClearTargetConnection();

            IsTargetConnected = false;
            TargetConnectionStatus = "Connection not active";
            TargetConnectionDetails = "";
            ScriptOutput = "Target vCenter connection has been closed.";

            _logger.LogInformation("✅ Target vCenter connection disconnected successfully");

            // Notify property changes
            OnPropertyChanged(nameof(IsTargetDisconnected));
            OnPropertyChanged(nameof(CanConnectTarget));
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
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
            }
        }

    [RelayCommand]
    private async Task OnRefreshInventory ()
        {
        if (IsJobRunning) return;

        IsJobRunning = true;
        CurrentJobText = "Refreshing inventory...";
        
        try
            {
            var refreshTasks = new List<Task>();
            
            if (IsSourceConnected)
            {
                refreshTasks.Add(_sharedConnectionService.RefreshSourceInventoryAsync());
            }
            
            if (IsTargetConnected)
            {
                refreshTasks.Add(_sharedConnectionService.RefreshTargetInventoryAsync());
            }

            if (refreshTasks.Count > 0)
            {
                await Task.WhenAll(refreshTasks);
                UpdateInventorySummaries();
                CurrentJobText = "Inventory refreshed successfully.";
            }
            else
            {
                CurrentJobText = "No active connections to refresh.";
            }

            _logger.LogInformation("Inventory refresh completed");
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error refreshing inventory");
            CurrentJobText = "Inventory refresh failed.";
            }
        finally
            {
            IsJobRunning = false;
            }
        }

    [RelayCommand]
    private void OnNavigateToObjects()
    {
        // This will be handled by the navigation service in the View's code-behind
        // We'll trigger this via the View
    }

    [RelayCommand]
    private void OnNavigateToMigration()
    {
        // This will be handled by the navigation service in the View's code-behind
        // We'll trigger this via the View
    }

    [RelayCommand]
    private void OnNavigateToLogs()
    {
        // This will be handled by the navigation service in the View's code-behind
        // We'll trigger this via the View
    }

    // Property change handlers for selection changes
    partial void OnSelectedSourceProfileChanged (VCenterConnection? value)
        {
        OnPropertyChanged(nameof(CanConnectSource));
        }

    partial void OnSelectedTargetProfileChanged (VCenterConnection? value)
        {
        OnPropertyChanged(nameof(CanConnectTarget));
        }

    partial void OnIsJobRunningChanged (bool value)
        {
        NotifyJobStatePropertiesChanged();
        }
    }