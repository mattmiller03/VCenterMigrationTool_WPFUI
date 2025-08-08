using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private readonly IThemeService _themeService;
        private readonly ConnectionProfileService _profileService;
        private readonly PowerShellService _powerShellService;

        [ObservableProperty]
        private ApplicationTheme _currentTheme;

        [ObservableProperty]
        private ObservableCollection<VCenterConnection> _profiles;

        [ObservableProperty]
        private VCenterConnection? _selectedProfileForEditing;

        [ObservableProperty]
        private string _newProfileName = string.Empty;

        [ObservableProperty]
        private string _newProfileServer = string.Empty;

        [ObservableProperty]
        private string _newProfileUsername = string.Empty;

        [ObservableProperty]
        private bool _shouldSavePassword;

        [ObservableProperty]
        private bool _isEditing;

        private readonly IOptions<AppConfig> _appConfig;

        // --- New Properties for General Settings ---
        [ObservableProperty]
        private string _powerShellVersion = "Checking...";

        [ObservableProperty]
        private bool _isPowerCliInstalled;

        [ObservableProperty]
        private string _logPath = string.Empty;

        [ObservableProperty]
        private string _exportPath = string.Empty;

        [ObservableProperty]
        private string _connectionStatus = "Ready to test connection.";

        public SettingsViewModel(
            IThemeService themeService,
            ConnectionProfileService profileService,
            PowerShellService powerShellService, 
            IOptions<AppConfig> appConfig
        )
        {
            _themeService = themeService;
            _profileService = profileService;
            _powerShellService = powerShellService;
            _currentTheme = _themeService.GetTheme();
            _profiles = _profileService.Profiles;
            _appConfig = appConfig;

            // Load settings from the config file
            _logPath = _appConfig.Value.LogPath ?? "Logs";
            _exportPath = _appConfig.Value.ExportPath ?? "Exports";
        }

        [RelayCommand]
        private async Task OnCheckPrerequisites()
        {
            PowerShellVersion = "Checking...";
            IsPowerCliInstalled = false;

            // Run our new script and expect a PrerequisitesResult object back
            var results = await _powerShellService.RunScriptAndGetObjectsAsync<PrerequisitesResult>(
                ".\\Scripts\\Get-Prerequisites.ps1",
                new Dictionary<string, object>() // No parameters needed for this script
            );

            var result = results.FirstOrDefault();
            if (result != null)
            {
                PowerShellVersion = result.PowerShellVersion;
                IsPowerCliInstalled = result.IsPowerCliInstalled;
            }
            else
            {
                PowerShellVersion = "Failed to get version";
            }
        }
        [RelayCommand]
        private async Task OnInstallPowerCli()
        {
            // This will call a script to install the module
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void OnSaveChanges()
        {
            // This will save the new path settings
            // In a real app, you'd write these values back to appsettings.json
            _appConfig.Value.LogPath = LogPath;
            _appConfig.Value.ExportPath = ExportPath;
        }
        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            var newTheme = parameter switch
            {
                "theme_light" => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark
            };
            if (_themeService.GetTheme() == newTheme)
                return;
            _themeService.SetTheme(newTheme);
            CurrentTheme = newTheme;
        }

        [RelayCommand]
        private void OnAddProfile(PasswordBox? passwordBox)
        {
            if (string.IsNullOrWhiteSpace(NewProfileName) || string.IsNullOrWhiteSpace(NewProfileServer))
                return;
            var newProfile = new VCenterConnection
            {
                Name = NewProfileName,
                ServerAddress = NewProfileServer,
                Username = NewProfileUsername,
                ShouldSavePassword = ShouldSavePassword
            };
            _profileService.ProtectPassword(newProfile, passwordBox?.Password);
            _profileService.AddProfile(newProfile);
            OnCancelEdit(); // Clear fields after adding
            passwordBox?.Clear();
        }

        [RelayCommand]
        private void OnRemoveProfile(VCenterConnection? profile)
        {
            if (profile is null) return;
            _profileService.RemoveProfile(profile);
        }



        [RelayCommand]
        private void OnStartEdit(VCenterConnection? profile)
        {
            if (profile is null) return;
            SelectedProfileForEditing = profile;
            NewProfileName = profile.Name;
            NewProfileServer = profile.ServerAddress;
            NewProfileUsername = profile.Username;
            IsEditing = true;
        }

        [RelayCommand]
        private async Task OnTestSelectedProfile()
        {
            if (SelectedProfileForEditing is null)
            {
                ConnectionStatus = "Please select a profile to test.";
                return;
            }

            // Decrypt the password for the selected profile
            string? password = _profileService.UnprotectPassword(SelectedProfileForEditing);
            if (string.IsNullOrEmpty(password))
            {
                ConnectionStatus = "Password not saved for this profile.";
                return;
            }

            ConnectionStatus = $"Testing connection to {SelectedProfileForEditing.ServerAddress}...";

            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", SelectedProfileForEditing.ServerAddress },
                { "Username", SelectedProfileForEditing.Username },
                { "Password", password }
            };

            string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

            if (result.Trim() == "Success")
            {
                ConnectionStatus = "Connection successful!";
            }
            else
            {
                ConnectionStatus = $"Failed: {result.Replace("Failure:", "").Trim()}";
            }
        }
        [RelayCommand]
        private void OnUpdateProfile()
        {
            if (SelectedProfileForEditing is null) return;
            SelectedProfileForEditing.Name = NewProfileName;
            SelectedProfileForEditing.ServerAddress = NewProfileServer;
            SelectedProfileForEditing.Username = NewProfileUsername;
            _profileService.UpdateProfile();
            OnCancelEdit();
        }

        [RelayCommand]
        private void OnCancelEdit()
        {
            NewProfileName = string.Empty;
            NewProfileServer = string.Empty;
            NewProfileUsername = string.Empty;
            ShouldSavePassword = false;
            IsEditing = false;
            SelectedProfileForEditing = null;
        }

        [RelayCommand]
        private void OnPrepareNewProfile()
        {
            OnCancelEdit();
        }

        public async Task OnNavigatedToAsync() => await Task.CompletedTask;
        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
    }
}