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
    private readonly VSphereApiService _vSphereApiService;
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
    private string _sourceConnectionStatus = "⭕ Connection not active";

    [ObservableProperty]
    private string _targetConnectionStatus = "⭕ Connection not active";

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

    // Activity log properties
    [ObservableProperty]
    private string _activityLog = "vCenter Migration Tool - Activity Log\n" +
                                 "=====================================\n" +
                                 "[INFO] Application started\n" +
                                 "[INFO] Ready for connections...\n";

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

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
        VSphereApiService vSphereApiService,
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
        _vSphereApiService = vSphereApiService;
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
        await Task.Run(UpdateInventorySummaries);
        }

    public async Task OnNavigatedFromAsync ()
        {
        // Keep connections alive when navigating away
        await Task.CompletedTask;
        }

    /// <summary>
    /// Updates inventory summaries for both source and target vCenters
    /// Uses vSphere API for quick counts on dashboard
    /// </summary>
    private async void UpdateInventorySummaries()
    {
        // Update source inventory summary using API
        if (IsSourceConnected && _sharedConnectionService.SourceConnection != null)
        {
            try
            {
                var sourceCounts = await _sharedConnectionService.GetInventoryCountsAsync("source");
                if (sourceCounts != null)
                {
                    HasSourceInventory = true;
                    SourceInventorySummary = $"{sourceCounts.DatacenterCount} DCs • {sourceCounts.ClusterCount} Clusters • {sourceCounts.HostCount} Hosts • {sourceCounts.VmCount} VMs • {sourceCounts.DatastoreCount} Datastores\n" +
                                           $"Basic counts via vSphere API • Use vCenter Objects page for detailed inventory\n" +
                                           $"Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
                else
                {
                    HasSourceInventory = false;
                    SourceInventorySummary = "Error loading inventory counts";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get source inventory counts via API");
                HasSourceInventory = false;
                SourceInventorySummary = "Error loading inventory counts";
            }
        }
        else
        {
            HasSourceInventory = false;
            SourceInventorySummary = IsSourceConnected ? "Loading inventory..." : "No inventory loaded";
        }

        // Update target inventory summary using API
        if (IsTargetConnected && _sharedConnectionService.TargetConnection != null)
        {
            try
            {
                var targetCounts = await _sharedConnectionService.GetInventoryCountsAsync("target");
                if (targetCounts != null)
                {
                    HasTargetInventory = true;
                    TargetInventorySummary = $"{targetCounts.DatacenterCount} DCs • {targetCounts.ClusterCount} Clusters • {targetCounts.HostCount} Hosts • {targetCounts.VmCount} VMs • {targetCounts.DatastoreCount} Datastores\n" +
                                           $"Basic counts via vSphere API • Use vCenter Objects page for detailed inventory\n" +
                                           $"Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
                else
                {
                    HasTargetInventory = false;
                    TargetInventorySummary = "Error loading inventory counts";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get target inventory counts via API");
                HasTargetInventory = false;
                TargetInventorySummary = "Error loading inventory counts";
            }
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
        SourceConnectionStatus = "🔄 Initializing connection...";
        ScriptOutput = string.Empty;
        LogMessage($"Initiating source connection to {SelectedSourceProfile.ServerAddress}", "INFO");

        _logger.LogInformation("=== ESTABLISHING PERSISTENT SOURCE CONNECTION ===");
        _logger.LogInformation("Selected Source Profile: {Server} | User: {Username}", 
            SelectedSourceProfile.ServerAddress, SelectedSourceProfile.Username);
        _logger.LogInformation("PowerCLI Bypass Status: {PowerCliStatus}", 
            HybridPowerShellService.PowerCliConfirmedInstalled);

        // Step 1: Get credentials
        SourceConnectionStatus = "🔐 Retrieving credentials...";
        await Task.Delay(100); // Allow UI to update

        _logger.LogInformation("STEP 1: Retrieving stored credentials for profile: {ProfileName}", 
            SelectedSourceProfile.Name);
        string? password = _credentialService.GetPassword(SelectedSourceProfile);
        string finalPassword;

        if (string.IsNullOrEmpty(password))
            {
            _logger.LogInformation("STEP 1: No stored credentials found - prompting user for password");
            SourceConnectionStatus = "🔑 Password required - please enter credentials";
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {SelectedSourceProfile.Username}@{SelectedSourceProfile.ServerAddress}:"
            );

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                _logger.LogWarning("STEP 1: User cancelled password prompt");
                SourceConnectionStatus = "❌ Connection cancelled by user";
                LogMessage("Source connection cancelled by user", "WARN");
                IsJobRunning = false;
                return;
                }

            _logger.LogInformation("STEP 1: User provided password successfully");
            finalPassword = promptedPassword;
            }
        else
            {
            _logger.LogInformation("STEP 1: Using stored credentials");
            finalPassword = password;
            }

        try
            {
            // Step 2: Establish PowerShell connection
            _logger.LogInformation("STEP 2: Starting persistent connection to {Server}", 
                SelectedSourceProfile.ServerAddress);
            SourceConnectionStatus = $"🔗 Connecting to {SelectedSourceProfile.ServerAddress}...";
            await Task.Delay(100); // Allow UI to update

            _logger.LogInformation("STEP 2: Using VSphereApiService for simple connection validation");
            
            // Create connection info for VSphere API
            var connectionInfo = new VCenterConnectionInfo
            {
                ServerAddress = SelectedSourceProfile.ServerAddress,
                Username = SelectedSourceProfile.Username
            };
            
            // Test connection using VSphere API first
            var (apiSuccess, sessionToken) = await _vSphereApiService.AuthenticateAsync(connectionInfo, finalPassword);
            string apiMessage = apiSuccess ? "Connection successful via VSphere API" : sessionToken; // Use actual error message from service
            string apiSessionId = apiSuccess ? sessionToken : null;
            
            _logger.LogInformation("STEP 2A: VSphereApiService returned - Success: {Success} | Message: {Message} | HasSessionToken: {HasToken}", 
                apiSuccess, apiMessage, !string.IsNullOrEmpty(sessionToken));
            
            // Show API connection result before proceeding to PowerCLI
            SourceConnectionStatus = $"API Connection: {(apiSuccess ? "✅ Success" : "❌ Failed")} - Now attempting PowerCLI...";
            await Task.Delay(1000); // Give user time to see API result

            // Always establish PowerCLI connection alongside API for admin operations
            // This ensures admin configuration functionality works regardless of API SSL issues
            bool powerCLISuccess = false;
            string powerCLISessionId = null;
            
            _logger.LogInformation("STEP 2B: Establishing PowerCLI connection for admin operations alongside API");
            SourceConnectionStatus = $"🔗 Attempting PowerCLI connection to {SelectedSourceProfile.ServerAddress}...";
            await Task.Delay(500); // Allow UI to update and be visible

            try
            {
                // Establish persistent PowerCLI connection using PersistentExternalConnectionService
                _logger.LogInformation("STEP 2B: Establishing persistent PowerCLI connection for admin operations");
                
                // First try with PowerCLI modules (bypassModuleCheck: false)
                var (pcliSuccess, pcliMessage, pcliSessionId) = await _persistentConnectionService.ConnectAsync(
                    SelectedSourceProfile, finalPassword, isSource: true, bypassModuleCheck: false);
                
                // If PowerCLI import fails, try bypass mode for basic PowerShell functionality
                if (!pcliSuccess && pcliMessage.Contains("Failed to load PowerCLI modules"))
                {
                    _logger.LogWarning("PowerCLI import failed, attempting bypass mode for basic PowerShell functionality");
                    var (bypassSuccess, bypassMessage, bypassSessionId) = await _persistentConnectionService.ConnectAsync(
                        SelectedSourceProfile, finalPassword, isSource: true, bypassModuleCheck: true);
                    
                    pcliSuccess = bypassSuccess;
                    pcliMessage = bypassSuccess ? "Connected in PowerShell bypass mode (limited functionality)" : bypassMessage;
                    pcliSessionId = bypassSessionId;
                }
                
                powerCLISuccess = pcliSuccess;
                powerCLISessionId = pcliSessionId;
                
                if (pcliSuccess)
                {
                    _logger.LogInformation("STEP 2B: Persistent PowerCLI connection established - SessionId: {SessionId}", pcliSessionId);
                    
                    // Allow PowerShell process to stabilize after connection before testing
                    _logger.LogDebug("STEP 2B: Allowing PowerShell process to stabilize...");
                    await Task.Delay(2000); // 2 second stabilization delay
                    
                    try
                    {
                        // Test the connection with a simple, safe command
                        var testResult = await _persistentConnectionService.ExecuteCommandAsync("source", 
                            "if ($global:DefaultVIServers -and $global:DefaultVIServers.Count -gt 0) { $global:DefaultVIServers[0] | Select-Object Name, IsConnected | ConvertTo-Json } else { 'No connections found' }");
                        
                        if (!string.IsNullOrEmpty(testResult) && !testResult.Contains("ERROR"))
                        {
                            _logger.LogInformation("STEP 2B: Connection test successful: {Result}", testResult.Trim());
                        }
                        else
                        {
                            _logger.LogWarning("STEP 2B: Connection test returned unexpected result: {Result}", testResult);
                        }
                    }
                    catch (Exception testEx)
                    {
                        _logger.LogWarning(testEx, "STEP 2B: Connection test failed, but connection may still be functional");
                        // Don't fail the entire process if just the test fails
                    }
                }
                else
                {
                    _logger.LogError("STEP 2B: Persistent PowerCLI connection failed: {Message}", pcliMessage);
                }
            }
            catch (Exception pcliEx)
            {
                _logger.LogError(pcliEx, "STEP 2B: Exception during PowerCLI connection");
            }

            // Update status to show PowerCLI result for source
            SourceConnectionStatus = $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success" : "❌ Failed")} - Finalizing setup...";
            await Task.Delay(500); // Allow user to see PowerCLI result

            // Evaluate overall connection success - need at least one working connection
            bool overallSuccess = apiSuccess || powerCLISuccess;
            
            if (overallSuccess)
                {
                _logger.LogInformation("STEP 3: Connection successful - setting up shared connection service (API: {ApiStatus}, PowerCLI: {PowerCLIStatus})", 
                    apiSuccess, powerCLISuccess);
                
                // Step 3: Set connection status for both API and PowerCLI
                _sharedConnectionService.SourceConnection = SelectedSourceProfile;
                _sharedConnectionService.SourceApiConnected = apiSuccess;
                _sharedConnectionService.SourceUsingPowerCLI = powerCLISuccess;
                _sharedConnectionService.SourcePowerCLISessionId = powerCLISuccess ? powerCLISessionId : null;

                string version = "Unknown";
                
                if (powerCLISuccess)
                {
                    // Get version from persistent connection
                    try 
                    {
                        var versionResult = await _persistentConnectionService.ExecuteCommandAsync("source", 
                            "$global:DefaultVIServers | Select-Object -First 1 | Select-Object -ExpandProperty Version");
                        if (!string.IsNullOrEmpty(versionResult))
                        {
                            version = versionResult.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not get vCenter version from PowerCLI connection");
                    }
                    
                    _logger.LogInformation("STEP 3: PowerCLI connection active - Version: {Version}", version);
                }
                else if (apiSuccess)
                {
                    // Get basic vCenter info using API
                    _logger.LogInformation("STEP 3: Getting vCenter version info via API");
                    var (isConnected, apiVersion, build) = await _vSphereApiService.GetConnectionStatusAsync(connectionInfo, finalPassword);
                    version = isConnected ? apiVersion : "Unknown";
                    _logger.LogInformation("STEP 3: vCenter info retrieved - Version: {Version}", version);
                }

                IsSourceConnected = true;
                
                // Create connection status that reflects both connections
                string connectionTypes = "";
                if (apiSuccess && powerCLISuccess) connectionTypes = "API+PowerCLI";
                else if (powerCLISuccess) connectionTypes = "PowerCLI";
                else if (apiSuccess) connectionTypes = "API";
                
                SourceConnectionStatus = $"✅ Connected - {SelectedSourceProfile.ServerAddress} (v{version})";
                SourceConnectionDetails = $"API Connection: {(apiSuccess ? "Active" : "Failed")}\n" +
                                        $"PowerCLI Connection: {(powerCLISuccess ? "Active" : "Failed")}\n" +
                                        $"Version: {version}";
                
                ScriptOutput = $"Dual connection established to {SelectedSourceProfile.ServerAddress}!\n\n" +
                              $"API Connection: {(apiSuccess ? "✅ Success" : "❌ Failed")}\n" +
                              $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success (SSL bypassed)" : "❌ Failed")}\n" +
                              $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                              $"Version: {version}\n\n" +
                              $"Connection capabilities:\n" +
                              $"• Standard operations: {(apiSuccess ? "API + PowerCLI fallback" : "PowerCLI only")}\n" +
                              $"• Admin configuration: {(powerCLISuccess ? "Available" : "Not available")}\n\n" +
                              $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Source vCenter dual connection established successfully - API: {ApiStatus}, PowerCLI: {PowerCLIStatus}, Version: {Version}", 
                    apiSuccess, powerCLISuccess, version);
                LogMessage($"✅ Source connection successful: {SelectedSourceProfile.ServerAddress} ({connectionTypes})", "INFO");

                // Update inventory summary
                _logger.LogInformation("STEP 3: Starting inventory summary update task");
                _ = Task.Run(UpdateInventorySummaries);

                // Notify property changes
                OnPropertyChanged(nameof(IsSourceDisconnected));
                OnPropertyChanged(nameof(CanConnectSource));
                }
            else
                {
                _logger.LogError("STEP 2: Both connections FAILED - API: {ApiSuccess}, PowerCLI: {PowerCLISuccess}", 
                    apiSuccess, powerCLISuccess);
                
                string failureMessage = $"API: {(apiSuccess ? "Success" : apiMessage)} | PowerCLI: {(powerCLISuccess ? "Success" : "Failed")}";
                
                LogMessage($"❌ Source connection failed: {failureMessage}", "ERROR");
                IsSourceConnected = false;
                SourceConnectionStatus = $"❌ Connection failed - both API and PowerCLI failed";
                SourceConnectionDetails = $"API Connection: {(apiSuccess ? "Success" : "Failed")}\n" +
                                        $"PowerCLI Connection: {(powerCLISuccess ? "Success" : "Failed")}\n" +
                                        $"Details: {failureMessage}";
                ScriptOutput = $"Connection failed to {SelectedSourceProfile.ServerAddress}:\n\n" +
                              $"API Connection: {(apiSuccess ? "✅ Success" : $"❌ Failed - {apiMessage}")}\n" +
                              $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success" : "❌ Failed")}\n\n" +
                              $"At least one connection method must succeed to proceed.";
                _sharedConnectionService.SourceConnection = null;

                _logger.LogError("❌ Failed to establish any persistent connection");
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "EXCEPTION: Error establishing persistent connection - Type: {ExceptionType} | Message: {Message}", 
                ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
                {
                _logger.LogError("EXCEPTION: Inner Exception - Type: {InnerType} | Message: {InnerMessage}", 
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
                }
            _logger.LogError("EXCEPTION: Stack Trace: {StackTrace}", ex.StackTrace);
            IsSourceConnected = false;
            SourceConnectionStatus = $"❌ Connection error: {ex.Message}";
            SourceConnectionDetails = "";
            ScriptOutput = $"Error: {ex.Message}";
            LogMessage($"❌ Source connection error: {ex.Message}", "ERROR");
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
        TargetConnectionStatus = "🔄 Initializing connection...";
        ScriptOutput = string.Empty;
        LogMessage($"Initiating target connection to {SelectedTargetProfile.ServerAddress}", "INFO");

        _logger.LogInformation("=== ESTABLISHING PERSISTENT TARGET CONNECTION ===");
        _logger.LogInformation("Selected Target Profile: {Server} | User: {Username}", 
            SelectedTargetProfile.ServerAddress, SelectedTargetProfile.Username);
        _logger.LogInformation("PowerCLI Bypass Status: {PowerCliStatus}", 
            HybridPowerShellService.PowerCliConfirmedInstalled);

        // Step 1: Get credentials
        TargetConnectionStatus = "🔐 Retrieving credentials...";
        await Task.Delay(100); // Allow UI to update

        _logger.LogInformation("STEP 1: Retrieving stored credentials for profile: {ProfileName}", 
            SelectedTargetProfile.Name);
        string? password = _credentialService.GetPassword(SelectedTargetProfile);
        string finalPassword;

        if (string.IsNullOrEmpty(password))
            {
            _logger.LogInformation("STEP 1: No stored credentials found - prompting user for password");
            TargetConnectionStatus = "🔑 Password required - please enter credentials";
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {SelectedTargetProfile.Username}@{SelectedTargetProfile.ServerAddress}:"
            );

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                {
                _logger.LogWarning("STEP 1: User cancelled password prompt");
                TargetConnectionStatus = "❌ Connection cancelled by user";
                IsJobRunning = false;
                return;
                }

            _logger.LogInformation("STEP 1: User provided password successfully");
            finalPassword = promptedPassword;
            }
        else
            {
            _logger.LogInformation("STEP 1: Using stored credentials");
            finalPassword = password;
            }

        try
            {
            // Step 2: Establish connection using VSphere API
            _logger.LogInformation("STEP 2: Starting target connection to {Server}", 
                SelectedTargetProfile.ServerAddress);
            TargetConnectionStatus = $"🔗 Connecting to {SelectedTargetProfile.ServerAddress}...";
            await Task.Delay(100); // Allow UI to update

            _logger.LogInformation("STEP 2: Using VSphereApiService for simple connection validation");
            
            // Create connection info for VSphere API
            var connectionInfo = new VCenterConnectionInfo
            {
                ServerAddress = SelectedTargetProfile.ServerAddress,
                Username = SelectedTargetProfile.Username
            };
            
            // Test connection using VSphere API first
            var (apiSuccess, sessionToken) = await _vSphereApiService.AuthenticateAsync(connectionInfo, finalPassword);
            string apiMessage = apiSuccess ? "Connection successful via VSphere API" : sessionToken; // Use actual error message from service
            string apiSessionId = apiSuccess ? sessionToken : null;
            
            _logger.LogInformation("STEP 2A: VSphereApiService returned - Success: {Success} | Message: {Message} | HasSessionToken: {HasToken}", 
                apiSuccess, apiMessage, !string.IsNullOrEmpty(sessionToken));
            
            // Show API connection result before proceeding to PowerCLI
            TargetConnectionStatus = $"API Connection: {(apiSuccess ? "✅ Success" : "❌ Failed")} - Now attempting PowerCLI...";
            await Task.Delay(1000); // Give user time to see API result

            // Always establish PowerCLI connection alongside API for admin operations
            // This ensures admin configuration functionality works regardless of API SSL issues
            bool powerCLISuccess = false;
            string powerCLISessionId = null;
            
            _logger.LogInformation("STEP 2B: Establishing PowerCLI connection for admin operations alongside API");
            TargetConnectionStatus = $"🔗 Attempting PowerCLI connection to {SelectedTargetProfile.ServerAddress}...";
            await Task.Delay(500); // Allow UI to update and be visible

            try
            {
                // Establish persistent PowerCLI connection using PersistentExternalConnectionService
                _logger.LogInformation("STEP 2B: Establishing persistent PowerCLI connection for admin operations");
                
                // First try with PowerCLI modules (bypassModuleCheck: false)
                var (pcliSuccess, pcliMessage, pcliSessionId) = await _persistentConnectionService.ConnectAsync(
                    SelectedTargetProfile, finalPassword, isSource: false, bypassModuleCheck: false);
                
                // If PowerCLI import fails, try bypass mode for basic PowerShell functionality
                if (!pcliSuccess && pcliMessage.Contains("Failed to load PowerCLI modules"))
                {
                    _logger.LogWarning("PowerCLI import failed for target, attempting bypass mode for basic PowerShell functionality");
                    var (bypassSuccess, bypassMessage, bypassSessionId) = await _persistentConnectionService.ConnectAsync(
                        SelectedTargetProfile, finalPassword, isSource: false, bypassModuleCheck: true);
                    
                    pcliSuccess = bypassSuccess;
                    pcliMessage = bypassSuccess ? "Connected in PowerShell bypass mode (limited functionality)" : bypassMessage;
                    pcliSessionId = bypassSessionId;
                }
                
                powerCLISuccess = pcliSuccess;
                powerCLISessionId = pcliSessionId;
                
                if (pcliSuccess)
                {
                    _logger.LogInformation("STEP 2B: Persistent PowerCLI connection established - SessionId: {SessionId}", pcliSessionId);
                    
                    // Allow PowerShell process to stabilize after connection before testing
                    _logger.LogDebug("STEP 2B: Allowing target PowerShell process to stabilize...");
                    await Task.Delay(2000); // 2 second stabilization delay
                    
                    try
                    {
                        // Test the connection with a simple, safe command
                        var testResult = await _persistentConnectionService.ExecuteCommandAsync("target", 
                            "if ($global:DefaultVIServers -and $global:DefaultVIServers.Count -gt 0) { $global:DefaultVIServers[0] | Select-Object Name, IsConnected | ConvertTo-Json } else { 'No connections found' }");
                        
                        if (!string.IsNullOrEmpty(testResult) && !testResult.Contains("ERROR"))
                        {
                            _logger.LogInformation("STEP 2B: Target connection test successful: {Result}", testResult.Trim());
                        }
                        else
                        {
                            _logger.LogWarning("STEP 2B: Target connection test returned unexpected result: {Result}", testResult);
                        }
                    }
                    catch (Exception testEx)
                    {
                        _logger.LogWarning(testEx, "STEP 2B: Target connection test failed, but connection may still be functional");
                        // Don't fail the entire process if just the test fails
                    }
                }
                else
                {
                    _logger.LogError("STEP 2B: Persistent PowerCLI connection failed: {Message}", pcliMessage);
                }
            }
            catch (Exception pcliEx)
            {
                _logger.LogError(pcliEx, "STEP 2B: Exception during PowerCLI connection");
            }

            // Update status to show PowerCLI result for target
            TargetConnectionStatus = $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success" : "❌ Failed")} - Finalizing setup...";
            await Task.Delay(500); // Allow user to see PowerCLI result

            // Evaluate overall connection success - need at least one working connection
            bool overallSuccess = apiSuccess || powerCLISuccess;
            
            if (overallSuccess)
                {
                _logger.LogInformation("STEP 3: Connection successful - setting up shared connection service (API: {ApiStatus}, PowerCLI: {PowerCLIStatus})", 
                    apiSuccess, powerCLISuccess);
                
                // Step 3: Set connection status for both API and PowerCLI
                _sharedConnectionService.TargetConnection = SelectedTargetProfile;
                _sharedConnectionService.TargetApiConnected = apiSuccess;
                _sharedConnectionService.TargetUsingPowerCLI = powerCLISuccess;
                _sharedConnectionService.TargetPowerCLISessionId = powerCLISuccess ? powerCLISessionId : null;

                string version = "Unknown";
                
                if (powerCLISuccess)
                {
                    // Get version from persistent connection
                    try 
                    {
                        var versionResult = await _persistentConnectionService.ExecuteCommandAsync("target", 
                            "$global:DefaultVIServers | Select-Object -First 1 | Select-Object -ExpandProperty Version");
                        if (!string.IsNullOrEmpty(versionResult))
                        {
                            version = versionResult.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not get vCenter version from PowerCLI connection");
                    }
                    
                    _logger.LogInformation("STEP 3: PowerCLI connection active - Version: {Version}", version);
                }
                else if (apiSuccess)
                {
                    // Get basic vCenter info using API
                    _logger.LogInformation("STEP 3: Getting vCenter version info via API");
                    var (isConnected, apiVersion, build) = await _vSphereApiService.GetConnectionStatusAsync(connectionInfo, finalPassword);
                    version = isConnected ? apiVersion : "Unknown";
                    _logger.LogInformation("STEP 3: vCenter info retrieved - Version: {Version}", version);
                }

                IsTargetConnected = true;
                
                // Create connection status that reflects both connections
                string connectionTypes = "";
                if (apiSuccess && powerCLISuccess) connectionTypes = "API+PowerCLI";
                else if (powerCLISuccess) connectionTypes = "PowerCLI";
                else if (apiSuccess) connectionTypes = "API";
                
                TargetConnectionStatus = $"✅ Connected - {SelectedTargetProfile.ServerAddress} (v{version})";
                TargetConnectionDetails = $"API Connection: {(apiSuccess ? "Active" : "Failed")}\n" +
                                        $"PowerCLI Connection: {(powerCLISuccess ? "Active" : "Failed")}\n" +
                                        $"Version: {version}";
                
                ScriptOutput = $"Dual connection established to {SelectedTargetProfile.ServerAddress}!\n\n" +
                              $"API Connection: {(apiSuccess ? "✅ Success" : "❌ Failed")}\n" +
                              $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success (SSL bypassed)" : "❌ Failed")}\n" +
                              $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                              $"Version: {version}\n\n" +
                              $"Connection capabilities:\n" +
                              $"• Standard operations: {(apiSuccess ? "API + PowerCLI fallback" : "PowerCLI only")}\n" +
                              $"• Admin configuration: {(powerCLISuccess ? "Available" : "Not available")}\n\n" +
                              $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Target vCenter dual connection established successfully - API: {ApiStatus}, PowerCLI: {PowerCLIStatus}, Version: {Version}", 
                    apiSuccess, powerCLISuccess, version);
                LogMessage($"✅ Target connection successful: {SelectedTargetProfile.ServerAddress} ({connectionTypes})", "INFO");

                // Update inventory summary
                _logger.LogInformation("STEP 3: Starting inventory summary update task");
                _ = Task.Run(UpdateInventorySummaries);

                // Notify property changes
                OnPropertyChanged(nameof(IsTargetDisconnected));
                OnPropertyChanged(nameof(CanConnectTarget));
                }
            else
                {
                _logger.LogError("STEP 2: Both connections FAILED - API: {ApiSuccess}, PowerCLI: {PowerCLISuccess}", 
                    apiSuccess, powerCLISuccess);
                
                string failureMessage = $"API: {(apiSuccess ? "Success" : apiMessage)} | PowerCLI: {(powerCLISuccess ? "Success" : "Failed")}";
                
                LogMessage($"❌ Target connection failed: {failureMessage}", "ERROR");
                IsTargetConnected = false;
                TargetConnectionStatus = $"❌ Connection failed - both API and PowerCLI failed";
                TargetConnectionDetails = $"API Connection: {(apiSuccess ? "Success" : "Failed")}\n" +
                                        $"PowerCLI Connection: {(powerCLISuccess ? "Success" : "Failed")}\n" +
                                        $"Details: {failureMessage}";
                ScriptOutput = $"Connection failed to {SelectedTargetProfile.ServerAddress}:\n\n" +
                              $"API Connection: {(apiSuccess ? "✅ Success" : $"❌ Failed - {apiMessage}")}\n" +
                              $"PowerCLI Connection: {(powerCLISuccess ? "✅ Success" : "❌ Failed")}\n\n" +
                              $"At least one connection method must succeed to proceed.";
                _sharedConnectionService.TargetConnection = null;

                _logger.LogError("❌ Failed to establish any target connection");
                }
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "EXCEPTION: Error establishing target connection - Type: {ExceptionType} | Message: {Message}", 
                ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
                {
                _logger.LogError("EXCEPTION: Inner Exception - Type: {InnerType} | Message: {InnerMessage}", 
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
                }
            _logger.LogError("EXCEPTION: Stack Trace: {StackTrace}", ex.StackTrace);
            IsTargetConnected = false;
            TargetConnectionStatus = $"❌ Connection error: {ex.Message}";
            TargetConnectionDetails = "";
            ScriptOutput = $"Error: {ex.Message}";
            LogMessage($"❌ Target connection error: {ex.Message}", "ERROR");
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
        SourceConnectionStatus = "🔄 Disconnecting...";
        ScriptOutput = string.Empty;

        try
            {
            _logger.LogInformation("Disconnecting source vCenter connection...");
            LogMessage("Disconnecting from source vCenter", "INFO");

            SourceConnectionStatus = "🔌 Closing PowerShell session...";
            await Task.Delay(100); // Allow UI to update

            await _persistentConnectionService.DisconnectAsync("source");

            SourceConnectionStatus = "🧹 Clearing cached inventory...";
            await Task.Delay(50); // Allow UI to update

            _sharedConnectionService.ClearSourceConnection();

            IsSourceConnected = false;
            SourceConnectionStatus = "⭕ Connection not active";
            SourceConnectionDetails = "";
            ScriptOutput = "Source vCenter connection has been closed.";

            _logger.LogInformation("✅ Source vCenter connection disconnected successfully");
            LogMessage("✅ Source vCenter disconnected successfully", "INFO");

            // Notify property changes
            OnPropertyChanged(nameof(IsSourceDisconnected));
            OnPropertyChanged(nameof(CanConnectSource));
            }
        catch (Exception ex)
            {
            _logger.LogError(ex, "Error disconnecting source vCenter");
            SourceConnectionStatus = $"❌ Disconnect error: {ex.Message}";
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
        TargetConnectionStatus = "🔄 Disconnecting...";
        ScriptOutput = string.Empty;

        try
            {
            _logger.LogInformation("Disconnecting target vCenter connection...");

            TargetConnectionStatus = "🔌 Closing PowerShell session...";
            await Task.Delay(100); // Allow UI to update

            await _persistentConnectionService.DisconnectAsync("target");

            TargetConnectionStatus = "🧹 Clearing cached inventory...";
            await Task.Delay(50); // Allow UI to update

            _sharedConnectionService.ClearTargetConnection();

            IsTargetConnected = false;
            TargetConnectionStatus = "⭕ Connection not active";
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
            TargetConnectionStatus = $"❌ Disconnect error: {ex.Message}";
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
                await Task.Run(UpdateInventorySummaries);
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

    [RelayCommand]
    private void ClearLog()
    {
        ActivityLog = "vCenter Migration Tool - Activity Log\n" +
                     "=====================================\n" +
                     "[INFO] Log cleared\n";
        LogMessage("Log cleared by user", "INFO");
    }

    [RelayCommand]
    private void ToggleAutoScroll()
    {
        IsAutoScrollEnabled = !IsAutoScrollEnabled;
        LogMessage($"Auto scroll {(IsAutoScrollEnabled ? "enabled" : "disabled")}", "INFO");
    }

    /// <summary>
    /// Add a message to the activity log with timestamp
    /// </summary>
    public void LogMessage(string message, string level = "INFO")
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] [{level}] {message}\n";
        ActivityLog += logEntry;
        
        // If we have many lines, trim to keep performance good
        var lines = ActivityLog.Split('\n');
        if (lines.Length > 1000)
        {
            var keepLines = lines.Skip(lines.Length - 800).ToArray();
            ActivityLog = string.Join("\n", keepLines);
        }
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