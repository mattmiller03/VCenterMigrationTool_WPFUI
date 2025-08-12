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
using Meziantou.Framework.Win32;

namespace VCenterMigrationTool.ViewModels
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private readonly IThemeService _themeService;
        private readonly ConnectionProfileService _profileService;
        private readonly PowerShellService _powerShellService;
        private readonly CredentialService _credentialService;
        private readonly IOptions<AppConfig> _appConfig;

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
        private bool _isBusy;
        [ObservableProperty]
        private bool _shouldSavePassword;
        [ObservableProperty]
        private bool _isEditing;
        [ObservableProperty]
        private string _connectionStatus = "Ready to test connection.";
        [ObservableProperty]
        private string _powerShellVersion = "Checking...";
        [ObservableProperty]
        private bool _isPowerCliInstalled;
        [ObservableProperty]
        private string _logPath = string.Empty;
        [ObservableProperty]
        private string _exportPath = string.Empty;

        public SettingsViewModel(
            IThemeService themeService,
            ConnectionProfileService profileService,
            PowerShellService powerShellService,
            CredentialService credentialService,
            IOptions<AppConfig> appConfig
        )
        {
            _themeService = themeService;
            _profileService = profileService;
            _powerShellService = powerShellService;
            _credentialService = credentialService;
            _appConfig = appConfig;

            _currentTheme = _themeService.GetTheme();
            _profiles = _profileService.Profiles;
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
            ConnectionStatus = "Installing VMware.PowerCLI module... This may take a few minutes.";
            IsBusy = true;

            string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Install-PowerCli.ps1", new Dictionary<string, object>());

            ConnectionStatus = result; // Display the success or failure message from the script
            IsBusy = false;

            // After attempting installation, re-run the prerequisite check to update the status icon
            await OnCheckPrerequisites();
        }

        [RelayCommand]
        private void OnSaveChanges()
        {
            // This will save the new path settings
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
            // 1. Validate that essential fields are not empty
            if (string.IsNullOrWhiteSpace(NewProfileName) || string.IsNullOrWhiteSpace(NewProfileServer))
                return;

            // 2. Create a new profile object from the UI fields
            var newProfile = new VCenterConnection
            {
                Name = NewProfileName,
                ServerAddress = NewProfileServer,
                Username = NewProfileUsername,
                ShouldSavePassword = ShouldSavePassword
            };

            // 3. Use the CredentialService to securely save the password to the Windows Credential Manager
            _credentialService.SavePassword(newProfile, passwordBox?.Password ?? string.Empty);

            // 4. Add the profile to the main collection (this also saves the profiles.json file)
            _profileService.AddProfile(newProfile);

            // 5. Clear the UI fields, ready for the next entry
            NewProfileName = string.Empty;
            NewProfileServer = string.Empty;
            NewProfileUsername = string.Empty;
            ShouldSavePassword = false;

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
            // 1. Check if a profile is actually selected in the list
            if (SelectedProfileForEditing is null)
            {
                ConnectionStatus = "Please select a profile from the list to test.";
                return;
            }
            IsBusy = true; // <-- Set IsBusy
            // 2. Use the CredentialService to get the saved password for the selected profile
            string? password = _credentialService.GetPassword(SelectedProfileForEditing);
            if (string.IsNullOrEmpty(password))
            {
                ConnectionStatus = "Password not saved for this profile. Cannot test.";
                return;
            }

            // 3. Update the UI to show that a test is in progress
            ConnectionStatus = $"Testing connection to {SelectedProfileForEditing.ServerAddress}...";
            IsBusy = true;

            // 4. Prepare the parameters for the PowerShell script
            var scriptParams = new Dictionary<string, object>
            {
                { "VCenterServer", SelectedProfileForEditing.ServerAddress },
                { "Username", SelectedProfileForEditing.Username },
                { "Password", password }
            };

            // 5. Execute the script and get the result
            string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams);

            // 6. Update the UI with the final result
            if (result.Trim() == "Success")
            {
                ConnectionStatus = "Connection successful!";
            }
            else
            {
                ConnectionStatus = $"Failed: {result.Replace("Failure:", "").Trim()}";
            }

            IsBusy = false;
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

        public async Task OnNavigatedToAsync()
        {
            // Automatically check prerequisites when the page is loaded
            await OnCheckPrerequisites();
        }
        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
    }
}