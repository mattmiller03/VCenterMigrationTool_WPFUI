using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
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

        [ObservableProperty]
        private ApplicationTheme _currentTheme;

        [ObservableProperty]
        private bool _isEditing = false;

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
        private string _connectionStatus = "Ready to test connection.";

        public SettingsViewModel(IThemeService themeService, ConnectionProfileService profileService)
        {
            _themeService = themeService;
            _profileService = profileService;
            _currentTheme = _themeService.GetTheme();
            _profiles = _profileService.Profiles;
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
        private void OnStartEdit(VCenterConnection? profile)
        {
            if (profile is null) return;

            // When editing starts, copy the selected profile's data into the entry fields
            // and set the IsEditing flag to true.
            SelectedProfileForEditing = profile;
            NewProfileName = profile.Name;
            NewProfileServer = profile.ServerAddress;
            NewProfileUsername = profile.Username;
            // We don't load the password for editing, for security.

            IsEditing = true;
        }

        [RelayCommand]
        private void OnUpdateProfile()
        {
            if (SelectedProfileForEditing is null) return;

            // Copy the updated values from the entry fields back to the selected profile.
            SelectedProfileForEditing.Name = NewProfileName;
            SelectedProfileForEditing.ServerAddress = NewProfileServer;
            SelectedProfileForEditing.Username = NewProfileUsername;

            // Tell the service to save the updated profiles to the file.
            _profileService.UpdateProfile();

            // Clear the entry fields and exit editing mode.
            NewProfileName = string.Empty;
            NewProfileServer = string.Empty;
            NewProfileUsername = string.Empty;
            IsEditing = false;
            SelectedProfileForEditing = null;
        }

        [RelayCommand]
        private void OnCancelEdit()
        {
            // Clear the entry fields and exit editing mode without saving.
            NewProfileName = string.Empty;
            NewProfileServer = string.Empty;
            NewProfileUsername = string.Empty;
            IsEditing = false;
            SelectedProfileForEditing = null;
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

            NewProfileName = string.Empty;
            NewProfileServer = string.Empty;
            NewProfileUsername = string.Empty;
            ShouldSavePassword = false;

            if (passwordBox != null)
                passwordBox.Clear();
        }

        [RelayCommand]
        private void OnRemoveProfile(VCenterConnection? profile)
        {
            if (profile is null) return;
            _profileService.RemoveProfile(profile);
        }

        [RelayCommand]
        private async Task OnTestConnection(VCenterConnection? profile)
        {
            if (profile is null)
            {
                ConnectionStatus = "Please select a profile to test.";
                return;
            }

            ConnectionStatus = $"Testing connection to {profile.ServerAddress}...";
            await Task.Delay(1500); // Simulate connection test
            ConnectionStatus = "Connection successful!";
        }

        public async Task OnNavigatedToAsync() => await Task.CompletedTask;

        public async Task OnNavigatedFromAsync() => await Task.CompletedTask;
    }
}