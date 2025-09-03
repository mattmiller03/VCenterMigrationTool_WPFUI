using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Media;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        // Services for vCenter connection functionality
        private readonly ConnectionProfileService _profileService;
        private readonly CredentialService _credentialService;
        private readonly HybridPowerShellService _powerShellService;
        private readonly PersistantVcenterConnectionService _persistantConnectionService;
        private readonly IDialogService _dialogService;
        private readonly ILogger<MainWindowViewModel> _logger;

        // FIX: Removed redundant '= null;' initializer
        [ObservableProperty]
        private string _applicationTitle;

        [ObservableProperty]
        private ObservableCollection<object> _navigationItems = new();

        [ObservableProperty]
        private ObservableCollection<object> _navigationFooter = new();

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new();

        // vCenter Connection Properties
        public ObservableCollection<VCenterConnection> SourceProfiles { get; }
        public ObservableCollection<VCenterConnection> TargetProfiles { get; }

        [ObservableProperty]
        private VCenterConnection? _selectedSourceProfile;

        [ObservableProperty]
        private VCenterConnection? _selectedTargetProfile;

        [ObservableProperty]
        private string _sourceConnectionStatus = "⭕ Not connected";

        [ObservableProperty]
        private string _targetConnectionStatus = "⭕ Not connected";

        [ObservableProperty]
        private bool _isSourceConnected;

        [ObservableProperty]
        private bool _isTargetConnected;

        [ObservableProperty]
        private bool _isJobRunning;

        // Connection state computed properties
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

        public MainWindowViewModel (
            INavigationService navigationService,
            ConnectionProfileService profileService,
            CredentialService credentialService,
            HybridPowerShellService powerShellService,
            PersistantVcenterConnectionService persistantConnectionService,
            IDialogService dialogService,
            ILogger<MainWindowViewModel> logger)
        {
            // Inject services
            _profileService = profileService;
            _credentialService = credentialService;
            _powerShellService = powerShellService;
            _persistantConnectionService = persistantConnectionService;
            _dialogService = dialogService;
            _logger = logger;

            _applicationTitle = "VCenter Migration Tool";

            // Initialize connection profiles
            SourceProfiles = new ObservableCollection<VCenterConnection>(_profileService.Profiles);
            TargetProfiles = new ObservableCollection<VCenterConnection>(_profileService.Profiles);

            // Create nested navigation structure - Remove Dashboard
            var vCenterObjectsChildItems = new List<object>
            {
                new NavigationViewItem("Infrastructure Migration", SymbolRegular.Server24, typeof(Views.Pages.InfrastructureMigrationPage)),
                new NavigationViewItem("Admin Config Migration", SymbolRegular.Settings24, typeof(Views.Pages.AdminConfigMigrationPage)),
                new NavigationViewItem("Resource Pools", SymbolRegular.DatabaseStack16, typeof(Views.Pages.ResourcePoolMigrationPage)),
                new NavigationViewItem("Network", SymbolRegular.NetworkCheck24, typeof(Views.Pages.NetworkMigrationPage))
            };
            var vCenterObjectsItem = new NavigationViewItem("vCenter Objects", SymbolRegular.Box24, typeof(Views.Pages.VCenterMigrationPage), vCenterObjectsChildItems);
            vCenterObjectsItem.IsExpanded = true; // Expand the vCenter Objects submenu by default

            NavigationItems = new ObservableCollection<object>
            {
                vCenterObjectsItem, // Make vCenter Objects the first/default item
                new NavigationViewItem("ESXi Hosts", SymbolRegular.Server24, typeof(Views.Pages.EsxiHostsPage)),
                new NavigationViewItem("Virtual Machines", SymbolRegular.Desktop24, typeof(Views.Pages.VmMigrationPage))
            };

            NavigationFooter = new ObservableCollection<object>
            {
                new NavigationViewItem("Settings", SymbolRegular.Settings24, typeof(Views.Pages.SettingsPage))
            };

            TrayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem { Header = "Home", Tag = "tray_home" }
            };
        }

        // Connection Commands
        [RelayCommand]
        private async Task OnConnectSource()
        {
            if (SelectedSourceProfile is null) return;

            IsJobRunning = true;
            SourceConnectionStatus = "🔄 Connecting...";
            NotifyConnectionPropertiesChanged();

            try
            {
                // Get credentials
                string? password = _credentialService.GetPassword(SelectedSourceProfile);
                
                if (string.IsNullOrEmpty(password))
                {
                    var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                        "Password Required", 
                        $"Enter password for {SelectedSourceProfile.Username}@{SelectedSourceProfile.ServerAddress}:");
                    
                    if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                        SourceConnectionStatus = "❌ Connection cancelled";
                        return;
                    }
                    password = promptedPassword;
                }

                // Establish persistent connection
                var (success, message, sessionId) = await _persistantConnectionService.ConnectAsync(
                    SelectedSourceProfile, password, isSource: true);
                
                if (success)
                {
                    IsSourceConnected = true;
                    SourceConnectionStatus = $"✅ Connected to {SelectedSourceProfile.ServerAddress}";
                    _logger.LogInformation("Source vCenter connected successfully. Session ID: {SessionId}", sessionId);
                }
                else
                {
                    IsSourceConnected = false;
                    SourceConnectionStatus = $"❌ Connection failed: {message}";
                    _logger.LogWarning("Source connection failed: {Message}", message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Source connection failed");
                IsSourceConnected = false;
                SourceConnectionStatus = "❌ Connection error";
            }
            finally
            {
                IsJobRunning = false;
                NotifyConnectionPropertiesChanged();
            }
        }

        [RelayCommand]
        private async Task OnConnectTarget()
        {
            if (SelectedTargetProfile is null) return;

            IsJobRunning = true;
            TargetConnectionStatus = "🔄 Connecting...";
            NotifyConnectionPropertiesChanged();

            try
            {
                // Get credentials
                string? password = _credentialService.GetPassword(SelectedTargetProfile);
                
                if (string.IsNullOrEmpty(password))
                {
                    var (dialogResult, promptedPassword) = _dialogService.ShowPasswordDialog(
                        "Password Required", 
                        $"Enter password for {SelectedTargetProfile.Username}@{SelectedTargetProfile.ServerAddress}:");
                    
                    if (dialogResult != true || string.IsNullOrEmpty(promptedPassword))
                    {
                        TargetConnectionStatus = "❌ Connection cancelled";
                        return;
                    }
                    password = promptedPassword;
                }

                // Establish persistent connection
                var (success, message, sessionId) = await _persistantConnectionService.ConnectAsync(
                    SelectedTargetProfile, password, isSource: false);
                
                if (success)
                {
                    IsTargetConnected = true;
                    TargetConnectionStatus = $"✅ Connected to {SelectedTargetProfile.ServerAddress}";
                    _logger.LogInformation("Target vCenter connected successfully. Session ID: {SessionId}", sessionId);
                }
                else
                {
                    IsTargetConnected = false;
                    TargetConnectionStatus = $"❌ Connection failed: {message}";
                    _logger.LogWarning("Target connection failed: {Message}", message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Target connection failed");
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Connection error";
            }
            finally
            {
                IsJobRunning = false;
                NotifyConnectionPropertiesChanged();
            }
        }

        [RelayCommand]
        private async Task OnDisconnectSource()
        {
            try
            {
                // Disconnect using persistent connection service
                await _persistantConnectionService.DisconnectAsync("source");
                
                IsSourceConnected = false;
                SourceConnectionStatus = "⭕ Disconnected";
                NotifyConnectionPropertiesChanged();
                
                _logger.LogInformation("Source vCenter disconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting source");
                // Update UI even if disconnect fails
                IsSourceConnected = false;
                SourceConnectionStatus = "❌ Disconnect error";
                NotifyConnectionPropertiesChanged();
            }
        }

        [RelayCommand]
        private async Task OnDisconnectTarget()
        {
            try
            {
                // Disconnect using persistent connection service
                await _persistantConnectionService.DisconnectAsync("target");
                
                IsTargetConnected = false;
                TargetConnectionStatus = "⭕ Disconnected";
                NotifyConnectionPropertiesChanged();
                
                _logger.LogInformation("Target vCenter disconnected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting target");
                // Update UI even if disconnect fails
                IsTargetConnected = false;
                TargetConnectionStatus = "❌ Disconnect error";
                NotifyConnectionPropertiesChanged();
            }
        }

        // Helper method to notify all connection-related properties
        private void NotifyConnectionPropertiesChanged()
        {
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanConnectTarget));
            OnPropertyChanged(nameof(CanDisconnectSource));
            OnPropertyChanged(nameof(CanDisconnectTarget));
            OnPropertyChanged(nameof(SourceConnectionBackgroundBrush));
            OnPropertyChanged(nameof(SourceConnectionBorderBrush));
            OnPropertyChanged(nameof(SourceConnectionTextBrush));
            OnPropertyChanged(nameof(TargetConnectionBackgroundBrush));
            OnPropertyChanged(nameof(TargetConnectionBorderBrush));
            OnPropertyChanged(nameof(TargetConnectionTextBrush));
        }

        // Property change handlers for CommunityToolkit.Mvvm - these are called automatically when ObservableProperty values change
        partial void OnSelectedSourceProfileChanged(VCenterConnection? value)
        {
            OnPropertyChanged(nameof(CanConnectSource));
            OnPropertyChanged(nameof(CanDisconnectSource));
        }

        partial void OnSelectedTargetProfileChanged(VCenterConnection? value)
        {
            OnPropertyChanged(nameof(CanConnectTarget));
            OnPropertyChanged(nameof(CanDisconnectTarget));
        }

        partial void OnIsJobRunningChanged(bool value)
        {
            NotifyConnectionPropertiesChanged();
        }

        partial void OnIsSourceConnectedChanged(bool value)
        {
            NotifyConnectionPropertiesChanged();
        }

        partial void OnIsTargetConnectedChanged(bool value)
        {
            NotifyConnectionPropertiesChanged();
        }
    }
}