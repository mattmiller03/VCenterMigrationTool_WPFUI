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
    // Phase 3: Unified Services (New Architecture)
    private readonly UnifiedPowerShellService _unifiedPowerShellService;
    private readonly UnifiedConnectionService _unifiedConnectionService;
    
    // Legacy services (for backward compatibility during migration)
    private readonly HybridPowerShellService _powerShellService;
    private readonly PersistantVcenterConnectionService _persistentConnectionService;
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
        // Phase 3: Unified Services (New Architecture)
        UnifiedPowerShellService unifiedPowerShellService,
        UnifiedConnectionService unifiedConnectionService,
        
        // Legacy services (for backward compatibility during migration)
        HybridPowerShellService powerShellService,
        PersistantVcenterConnectionService persistentConnectionService,
        VSphereApiService vSphereApiService,
        ConnectionProfileService profileService,
        CredentialService credentialService,
        SharedConnectionService sharedConnectionService,
        VCenterInventoryService inventoryService,
        ConfigurationService configurationService,
        IDialogService dialogService,
        ILogger<DashboardViewModel> logger)
        {
        // Initialize unified services first
        _unifiedPowerShellService = unifiedPowerShellService;
        _unifiedConnectionService = unifiedConnectionService;
        
        // Initialize legacy services for backward compatibility
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
        
        _logger.LogInformation("✅ DashboardViewModel initialized with unified services architecture");
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
            _logger.LogDebug("🔍 Checking connection status using unified services...");
            
            // Check source connection using unified service
            var sourceConnected = await _unifiedConnectionService.IsConnectedAsync("source");
            var sourceContext = _unifiedConnectionService.GetConnectionContext("source");
            
            if (sourceConnected && sourceContext != null)
                {
                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ Connected - {sourceContext.ConnectionInfo.ServerAddress} (v{sourceContext.VCenterVersion})";
                SourceConnectionDetails = $"Session: {sourceContext.SessionId}\nVersion: {sourceContext.VCenterVersion}\n" +
                                        $"Build: {sourceContext.VCenterBuild}\nProduct: {sourceContext.ProductLine}\n" +
                                        $"Connected: {sourceContext.ConnectedAt:yyyy-MM-dd HH:mm:ss}\n" +
                                        $"Last Activity: {sourceContext.LastActivityAt:yyyy-MM-dd HH:mm:ss}";
                
                // Update shared connection service for backward compatibility
                _sharedConnectionService.SourceConnection = sourceContext.ConnectionInfo;
                }
            else
                {
                IsSourceConnected = false;
                SourceConnectionStatus = sourceContext?.Status.ToString() switch
                {
                    "Connecting" => "🔄 Connecting...",
                    "Reconnecting" => "🔄 Reconnecting...", 
                    "Failed" => $"❌ Connection failed: {sourceContext.LastError}",
                    "Timeout" => "⏰ Connection timeout",
                    _ => "⭕ Connection not active"
                };
                SourceConnectionDetails = "";
                _sharedConnectionService.SourceConnection = null;
                }

            // Check target connection using unified service  
            var targetConnected = await _unifiedConnectionService.IsConnectedAsync("target");
            var targetContext = _unifiedConnectionService.GetConnectionContext("target");
            
            if (targetConnected && targetContext != null)
                {
                IsTargetConnected = true;
                TargetConnectionStatus = $"✅ Connected - {targetContext.ConnectionInfo.ServerAddress} (v{targetContext.VCenterVersion})";
                TargetConnectionDetails = $"Session: {targetContext.SessionId}\nVersion: {targetContext.VCenterVersion}\n" +
                                        $"Build: {targetContext.VCenterBuild}\nProduct: {targetContext.ProductLine}\n" +
                                        $"Connected: {targetContext.ConnectedAt:yyyy-MM-dd HH:mm:ss}\n" +
                                        $"Last Activity: {targetContext.LastActivityAt:yyyy-MM-dd HH:mm:ss}";
                
                // Update shared connection service for backward compatibility
                _sharedConnectionService.TargetConnection = targetContext.ConnectionInfo;
                }
            else
                {
                IsTargetConnected = false;
                TargetConnectionStatus = targetContext?.Status.ToString() switch
                {
                    "Connecting" => "🔄 Connecting...",
                    "Reconnecting" => "🔄 Reconnecting...",
                    "Failed" => $"❌ Connection failed: {targetContext.LastError}",
                    "Timeout" => "⏰ Connection timeout", 
                    _ => "⭕ Connection not active"
                };
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

        _logger.LogInformation("=== ESTABLISHING SOURCE CONNECTION (UNIFIED SERVICES) ===");
        _logger.LogInformation("Selected Source Profile: {Server} | User: {Username}", 
            SelectedSourceProfile.ServerAddress, SelectedSourceProfile.Username);

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
            // Step 2: Establish unified connection 
            _logger.LogInformation("STEP 2: Establishing unified connection to {Server}", 
                SelectedSourceProfile.ServerAddress);
            SourceConnectionStatus = $"🔗 Connecting to {SelectedSourceProfile.ServerAddress}...";
            await Task.Delay(100); // Allow UI to update

            // Use unified connection service for streamlined connection
            var (success, message, sessionId) = await _unifiedConnectionService.ConnectAsync(
                SelectedSourceProfile, finalPassword, isSource: true, bypassModuleCheck: false);

            if (success)
                {
                _logger.LogInformation("✅ Source connection established successfully: {Message}", message);
                
                // Update connection state
                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ Connected to {SelectedSourceProfile.ServerAddress}";
                
                // Update shared connection service for backward compatibility
                _sharedConnectionService.SourceConnection = SelectedSourceProfile;
                
                // Log success and update UI
                LogMessage($"✅ Source connection established - {SelectedSourceProfile.ServerAddress} (Session: {sessionId})", "SUCCESS");
                ScriptOutput = $"✅ Connection Success!\n" +
                              $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                              $"Session ID: {sessionId}\n" +
                              $"Message: {message}\n" +
                              $"Connected at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                // Load initial inventory summary
                await Task.Run(() => UpdateInventorySummaries());
                }
            else
                {
                _logger.LogError("❌ Source connection failed: {Message}", message);
                
                // Update connection state
                IsSourceConnected = false;
                SourceConnectionStatus = $"❌ Connection failed: {message}";
                
                // Log failure and update UI
                LogMessage($"❌ Source connection failed: {message}", "ERROR");
                ScriptOutput = $"❌ Connection Failed!\n" +
                              $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                              $"Error: {message}\n" +
                              $"Failed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
                
                if (success)
                {
                    _logger.LogInformation("Source connection established successfully using UnifiedConnectionService - SessionId: {SessionId}", sessionId);
                
                    IsSourceConnected = true;
                    
                    // Get version information from the connection context
                    var context = _unifiedConnectionService.GetConnectionContext(sessionId);
                    string version = context?.CachedInventory?.VCenterVersion ?? "Unknown";
                    
                    SourceConnectionStatus = $"✅ Connected - {SelectedSourceProfile.ServerAddress}";
                    SourceConnectionDetails = $"Connection: Active (Session: {sessionId})\n" +
                                            $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                                            $"Version: {version}";
                    
                    ScriptOutput = $"✅ Connection established to {SelectedSourceProfile.ServerAddress}!\n\n" +
                                  $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                                  $"Session ID: {sessionId}\n" +
                                  $"Version: {version}\n\n" +
                                  $"Connection is ready for operations.\n" +
                                  $"Use 'Disconnect' button to close the connection.";

                    _logger.LogInformation("✅ Source vCenter connection established successfully - Version: {Version}", version);
                    LogMessage($"✅ Source connection successful: {SelectedSourceProfile.ServerAddress}", "INFO");

                    // Update inventory summary in background
                    _ = Task.Run(UpdateInventorySummaries);

                    // Notify property changes
                    OnPropertyChanged(nameof(IsSourceDisconnected));
                    OnPropertyChanged(nameof(CanConnectSource));
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
    private async Task OnConnectTarget()
    {
        if (SelectedTargetProfile is null) return;

        IsJobRunning = true;
        TargetConnectionStatus = "🔄 Initializing connection...";
        ScriptOutput = string.Empty;
        LogMessage($"Initiating target connection to {SelectedTargetProfile.ServerAddress}", "INFO");

        _logger.LogInformation("=== ESTABLISHING TARGET CONNECTION ===");
        _logger.LogInformation("Selected Target Profile: {Server} | User: {Username}", 
            SelectedTargetProfile.ServerAddress, SelectedTargetProfile.Username);

        // Get credentials
        TargetConnectionStatus = "🔐 Retrieving credentials...";
        await Task.Delay(100);

        _logger.LogInformation("Retrieving stored credentials for profile: {ProfileName}", SelectedTargetProfile.Name);
        string? password = _credentialService.GetPassword(SelectedTargetProfile);
        string finalPassword;

        if (string.IsNullOrEmpty(password))
        {
            _logger.LogInformation("No stored credentials found - prompting user for password");
            TargetConnectionStatus = "🔑 Password required - please enter credentials";
            var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                "Password Required",
                $"Enter password for {SelectedTargetProfile.Username}@{SelectedTargetProfile.ServerAddress}:"
            );

            if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
            {
                _logger.LogWarning("User cancelled password prompt");
                TargetConnectionStatus = "❌ Connection cancelled by user";
                IsJobRunning = false;
                return;
            }

            _logger.LogInformation("User provided password successfully");
            finalPassword = promptedPassword;
        }
        else
        {
            _logger.LogInformation("Using stored credentials");
            finalPassword = password;
        }

        try
        {
            // Use unified connection service for target connection
            TargetConnectionStatus = $"🔗 Connecting to {SelectedTargetProfile.ServerAddress}...";
            await Task.Delay(100);
            
            _logger.LogInformation("Establishing target connection using UnifiedConnectionService");
            
            var (success, message, sessionId) = await _unifiedConnectionService.ConnectAsync(
                SelectedTargetProfile, finalPassword, isSource: false, bypassModuleCheck: false);
            
            if (!success)
            {
                _logger.LogError("Target connection failed: {Message}", message);
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Connection failed";
                TargetConnectionDetails = "";
                
                ScriptOutput = $"❌ Connection Failed!\n" +
                              $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                              $"Error: {message}\n" +
                              $"Failed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            }
            
            if (success)
            {
                _logger.LogInformation("Target connection established successfully using UnifiedConnectionService - SessionId: {SessionId}", sessionId);
            
                IsTargetConnected = true;
                
                // Get version information from the connection context
                var context = _unifiedConnectionService.GetConnectionContext(sessionId);
                string version = context?.CachedInventory?.VCenterVersion ?? "Unknown";
                
                TargetConnectionStatus = $"✅ Connected - {SelectedTargetProfile.ServerAddress}";
                TargetConnectionDetails = $"Connection: Active (Session: {sessionId})\n" +
                                        $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                                        $"Version: {version}";
                
                ScriptOutput = $"✅ Connection established to {SelectedTargetProfile.ServerAddress}!\n\n" +
                              $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                              $"Session ID: {sessionId}\n" +
                              $"Version: {version}\n\n" +
                              $"Connection is ready for operations.\n" +
                              $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Target vCenter connection established successfully - Version: {Version}", version);
                LogMessage($"✅ Target connection successful: {SelectedTargetProfile.ServerAddress}", "INFO");

                // Update inventory summary in background
                _ = Task.Run(UpdateInventorySummaries);

                // Notify property changes
                OnPropertyChanged(nameof(IsTargetDisconnected));
                OnPropertyChanged(nameof(CanConnectTarget));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error establishing target connection: {Message}", ex.Message);
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