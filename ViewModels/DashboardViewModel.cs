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
            var (success, sessionToken) = await _vSphereApiService.AuthenticateAsync(connectionInfo, finalPassword);
            string message = success ? "Connection successful via VSphere API" : "VSphere API authentication failed";
            string sessionId = success ? sessionToken : null;
            bool usedPowerCLI = false;
            string result = "";

            _logger.LogInformation("STEP 2: VSphereApiService returned - Success: {Success} | Message: {Message} | HasSessionToken: {HasToken}", 
                success, message, !string.IsNullOrEmpty(sessionToken));

            // If VSphere API fails with SSL issues, fall back to PowerCLI
            if (!success && (message.Contains("SSL") || message.Contains("certificate") || message.Contains("PartialChain") || message.Contains("AuthenticationException")))
            {
                _logger.LogWarning("STEP 2: VSphere API failed with SSL/certificate issue, attempting PowerCLI fallback");
                SourceConnectionStatus = $"🔄 SSL issue detected, trying PowerCLI fallback...";
                await Task.Delay(100); // Allow UI to update

                try
                {
                    // Use HybridPowerShellService for PowerCLI connection (bypasses SSL issues)
                    _logger.LogInformation("STEP 2: Attempting PowerCLI connection via HybridPowerShellService");
                    
                    var testScript = $@"
                        # PowerCLI connection test with SSL bypass
                        try {{
                            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                            Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                            
                            $credential = New-Object System.Management.Automation.PSCredential('{SelectedSourceProfile.Username}', (ConvertTo-SecureString '{finalPassword.Replace("'", "''")}' -AsPlainText -Force))
                            $connection = Connect-VIServer -Server '{SelectedSourceProfile.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                            
                            if ($connection -and $connection.IsConnected) {{
                                Write-Output 'POWERCLI_SUCCESS'
                                Write-Output ""SESSION_ID:$($connection.SessionId)""
                                Write-Output ""VERSION:$($connection.Version)""
                                Write-Output ""BUILD:$($connection.Build)""
                                
                                # Quick inventory test
                                $vmCount = (Get-VM -Server $connection -ErrorAction SilentlyContinue).Count
                                Write-Output ""VM_COUNT:$vmCount""
                                
                                # Keep connection for future use
                                $global:SourceVIConnection = $connection
                                Write-Output 'CONNECTION_ESTABLISHED'
                            }} else {{
                                Write-Output 'POWERCLI_FAILED: Connection object invalid'
                            }}
                        }} catch {{
                            Write-Output ""POWERCLI_FAILED: $($_.Exception.Message)""
                        }}
                    ";

                    result = await _powerShellService.RunCommandAsync(testScript);
                    
                    if (result.Contains("POWERCLI_SUCCESS") && result.Contains("CONNECTION_ESTABLISHED"))
                    {
                        // Parse PowerCLI connection details
                        var lines = result.Split('\n');
                        string version = "Unknown", build = "", vmCount = "0";
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("VERSION:")) version = line.Substring(8).Trim();
                            if (line.StartsWith("BUILD:")) build = line.Substring(6).Trim();
                            if (line.StartsWith("VM_COUNT:")) vmCount = line.Substring(9).Trim();
                        }
                        
                        success = true;
                        message = "Connection successful via PowerCLI";
                        sessionId = $"PowerCLI-{DateTime.Now:yyyyMMdd-HHmmss}";
                        usedPowerCLI = true;
                        
                        _logger.LogInformation("STEP 2: PowerCLI fallback successful - Version: {Version}, VM Count: {VmCount}", version, vmCount);
                    }
                    else
                    {
                        _logger.LogError("STEP 2: PowerCLI fallback also failed. Result: {Result}", result.Trim());
                        message = $"Both VSphere API and PowerCLI failed. PowerCLI error: {result}";
                    }
                }
                catch (Exception pcliEx)
                {
                    _logger.LogError(pcliEx, "STEP 2: Exception during PowerCLI fallback");
                    message = $"VSphere API SSL error, PowerCLI fallback failed: {pcliEx.Message}";
                }
            }

            if (success)
                {
                _logger.LogInformation("STEP 3: Connection successful - setting up shared connection service");
                // Step 3: Set connection
                _sharedConnectionService.SourceConnection = SelectedSourceProfile;

                string version = "Unknown";
                
                if (usedPowerCLI)
                {
                    // Version already parsed from PowerCLI output
                    var lines = result.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("VERSION:")) version = line.Substring(8).Trim();
                    }
                    
                    _logger.LogInformation("STEP 3: PowerCLI connection active - Version: {Version}", version);
                }
                else
                {
                    // Get basic vCenter info using API
                    _logger.LogInformation("STEP 3: Getting vCenter version info via API");
                    var (isConnected, apiVersion, build) = await _vSphereApiService.GetConnectionStatusAsync(connectionInfo, finalPassword);
                    version = isConnected ? apiVersion : "Unknown";
                    _logger.LogInformation("STEP 3: vCenter info retrieved - Version: {Version}", version);
                }

                IsSourceConnected = true;
                SourceConnectionStatus = $"✅ Connected - {SelectedSourceProfile.ServerAddress} (v{version})";
                SourceConnectionDetails = usedPowerCLI ? 
                    $"PowerCLI Session: Active\nVersion: {version}\nSSL: Bypassed" :
                    $"API Session: Active\nVersion: {version}";
                
                ScriptOutput = usedPowerCLI ?
                    $"PowerCLI connection established! (SSL certificates bypassed)\n" +
                    $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                    $"Version: {version}\n" +
                    $"Authentication: PowerCLI (used due to SSL certificate issues)\n\n" +
                    $"Connection is ready for all operations.\n" +
                    $"All operations will use PowerCLI commands.\n" +
                    $"Use 'Disconnect' button to close the connection." :
                    $"vCenter API connection established!\n" +
                    $"Server: {SelectedSourceProfile.ServerAddress}\n" +
                    $"Version: {version}\n" +
                    $"Authentication: VSphere API\n\n" +
                    $"Connection is ready for all operations.\n" +
                    $"PowerCLI operations will use the HybridPowerShellService as needed.\n" +
                    $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Source vCenter API connection established successfully (Version: {Version})", version);

                // Update inventory summary
                _logger.LogInformation("STEP 3: Starting inventory summary update task");
                _ = Task.Run(UpdateInventorySummaries);

                // Notify property changes
                OnPropertyChanged(nameof(IsSourceDisconnected));
                OnPropertyChanged(nameof(CanConnectSource));
                }
            else
                {
                _logger.LogError("STEP 2: Connection FAILED - Success={Success} | Message={Message}", success, message);
                IsSourceConnected = false;
                SourceConnectionStatus = $"❌ Connection failed: {message}";
                SourceConnectionDetails = "";
                ScriptOutput = $"Connection failed: {message}";
                _sharedConnectionService.SourceConnection = null;

                _logger.LogError("❌ Failed to establish persistent connection: {Message}", message);
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
            var (success, sessionToken) = await _vSphereApiService.AuthenticateAsync(connectionInfo, finalPassword);
            string message = success ? "Connection successful via VSphere API" : "VSphere API authentication failed";
            string sessionId = success ? sessionToken : null;
            bool usedPowerCLI = false;
            string result = "";

            _logger.LogInformation("STEP 2: VSphereApiService returned - Success: {Success} | Message: {Message} | HasSessionToken: {HasToken}", 
                success, message, !string.IsNullOrEmpty(sessionToken));

            // If VSphere API fails with SSL issues, fall back to PowerCLI
            if (!success && (message.Contains("SSL") || message.Contains("certificate") || message.Contains("PartialChain") || message.Contains("AuthenticationException")))
            {
                _logger.LogWarning("STEP 2: VSphere API failed with SSL/certificate issue, attempting PowerCLI fallback");
                TargetConnectionStatus = $"🔄 SSL issue detected, trying PowerCLI fallback...";
                await Task.Delay(100); // Allow UI to update

                try
                {
                    // Use HybridPowerShellService for PowerCLI connection (bypasses SSL issues)
                    _logger.LogInformation("STEP 2: Attempting PowerCLI connection via HybridPowerShellService");
                    
                    var testScript = $@"
                        # PowerCLI connection test with SSL bypass
                        try {{
                            Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false -Scope Session | Out-Null
                            Set-PowerCLIConfiguration -ParticipateInCEIP $false -Confirm:$false -Scope Session -ErrorAction SilentlyContinue | Out-Null
                            
                            $credential = New-Object System.Management.Automation.PSCredential('{SelectedTargetProfile.Username}', (ConvertTo-SecureString '{finalPassword.Replace("'", "''")}' -AsPlainText -Force))
                            $connection = Connect-VIServer -Server '{SelectedTargetProfile.ServerAddress}' -Credential $credential -Force -ErrorAction Stop
                            
                            if ($connection -and $connection.IsConnected) {{
                                Write-Output 'POWERCLI_SUCCESS'
                                Write-Output ""SESSION_ID:$($connection.SessionId)""
                                Write-Output ""VERSION:$($connection.Version)""
                                Write-Output ""BUILD:$($connection.Build)""
                                
                                # Quick inventory test
                                $vmCount = (Get-VM -Server $connection -ErrorAction SilentlyContinue).Count
                                Write-Output ""VM_COUNT:$vmCount""
                                
                                # Keep connection for future use
                                $global:TargetVIConnection = $connection
                                Write-Output 'CONNECTION_ESTABLISHED'
                            }} else {{
                                Write-Output 'POWERCLI_FAILED: Connection object invalid'
                            }}
                        }} catch {{
                            Write-Output ""POWERCLI_FAILED: $($_.Exception.Message)""
                        }}
                    ";

                    result = await _powerShellService.RunCommandAsync(testScript);
                    
                    if (result.Contains("POWERCLI_SUCCESS") && result.Contains("CONNECTION_ESTABLISHED"))
                    {
                        // Parse PowerCLI connection details
                        var lines = result.Split('\n');
                        string version = "Unknown", build = "", vmCount = "0";
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("VERSION:")) version = line.Substring(8).Trim();
                            if (line.StartsWith("BUILD:")) build = line.Substring(6).Trim();
                            if (line.StartsWith("VM_COUNT:")) vmCount = line.Substring(9).Trim();
                        }
                        
                        success = true;
                        message = "Connection successful via PowerCLI";
                        sessionId = $"PowerCLI-{DateTime.Now:yyyyMMdd-HHmmss}";
                        usedPowerCLI = true;
                        
                        _logger.LogInformation("STEP 2: PowerCLI fallback successful - Version: {Version}, VM Count: {VmCount}", version, vmCount);
                    }
                    else
                    {
                        _logger.LogError("STEP 2: PowerCLI fallback also failed. Result: {Result}", result.Trim());
                        message = $"Both VSphere API and PowerCLI failed. PowerCLI error: {result}";
                    }
                }
                catch (Exception pcliEx)
                {
                    _logger.LogError(pcliEx, "STEP 2: Exception during PowerCLI fallback");
                    message = $"VSphere API SSL error, PowerCLI fallback failed: {pcliEx.Message}";
                }
            }

            if (success)
                {
                _logger.LogInformation("STEP 3: Connection successful - setting up shared connection service");
                // Step 3: Set connection
                _sharedConnectionService.TargetConnection = SelectedTargetProfile;

                string version = "Unknown";
                
                if (usedPowerCLI)
                {
                    // Version already parsed from PowerCLI output
                    var lines = result.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("VERSION:")) version = line.Substring(8).Trim();
                    }
                    
                    _logger.LogInformation("STEP 3: PowerCLI connection active - Version: {Version}", version);
                }
                else
                {
                    // Get basic vCenter info using API
                    _logger.LogInformation("STEP 3: Getting vCenter version info via API");
                    var (isConnected, apiVersion, build) = await _vSphereApiService.GetConnectionStatusAsync(connectionInfo, finalPassword);
                    version = isConnected ? apiVersion : "Unknown";
                    _logger.LogInformation("STEP 3: vCenter info retrieved - Version: {Version}", version);
                }

                IsTargetConnected = true;
                TargetConnectionStatus = $"✅ Connected - {SelectedTargetProfile.ServerAddress} (v{version})";
                TargetConnectionDetails = usedPowerCLI ? 
                    $"PowerCLI Session: Active\nVersion: {version}\nSSL: Bypassed" :
                    $"API Session: Active\nVersion: {version}";
                
                ScriptOutput = usedPowerCLI ?
                    $"PowerCLI connection established! (SSL certificates bypassed)\n" +
                    $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                    $"Version: {version}\n" +
                    $"Authentication: PowerCLI (used due to SSL certificate issues)\n\n" +
                    $"Connection is ready for all operations.\n" +
                    $"All operations will use PowerCLI commands.\n" +
                    $"Use 'Disconnect' button to close the connection." :
                    $"vCenter API connection established!\n" +
                    $"Server: {SelectedTargetProfile.ServerAddress}\n" +
                    $"Version: {version}\n" +
                    $"Authentication: VSphere API\n\n" +
                    $"Connection is ready for all operations.\n" +
                    $"PowerCLI operations will use the HybridPowerShellService as needed.\n" +
                    $"Use 'Disconnect' button to close the connection.";

                _logger.LogInformation("✅ Target vCenter API connection established successfully (Version: {Version})", version);

                // Update inventory summary
                _logger.LogInformation("STEP 3: Starting inventory summary update task");
                _ = Task.Run(UpdateInventorySummaries);

                // Notify property changes
                OnPropertyChanged(nameof(IsTargetDisconnected));
                OnPropertyChanged(nameof(CanConnectTarget));
                }
            else
                {
                _logger.LogError("STEP 2: Connection FAILED - Success={Success} | Message={Message}", success, message);
                IsTargetConnected = false;
                TargetConnectionStatus = $"❌ Connection failed: {message}";
                TargetConnectionDetails = "";
                ScriptOutput = $"Connection failed: {message}";
                _sharedConnectionService.TargetConnection = null;

                _logger.LogError("❌ Failed to establish target connection: {Message}", message);
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