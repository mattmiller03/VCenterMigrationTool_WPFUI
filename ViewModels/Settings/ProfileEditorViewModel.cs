using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class ProfileEditorViewModel : ObservableObject
{
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private VCenterConnection? _profileBeingEdited; // Keep track of the original object

    [ObservableProperty]
    private string _profileName = string.Empty;
    [ObservableProperty]
    private string _serverAddress = string.Empty;
    [ObservableProperty]
    private string _username = string.Empty;
    [ObservableProperty]
    private string _passwordInput = string.Empty;
    [ObservableProperty]
    private bool _shouldSavePassword;
    [ObservableProperty]
    private string _formTitle = "Add New Profile";
    [ObservableProperty]
    private bool _isEditing;

    public ProfileEditorViewModel (ConnectionProfileService profileService, CredentialService credentialService)
        {
        _profileService = profileService;
        _credentialService = credentialService;
        }

        [RelayCommand]
        private void OnSaveProfile ()
        {
            if (string.IsNullOrWhiteSpace(ProfileName) || string.IsNullOrWhiteSpace(ServerAddress))
                return;

            if (IsEditing && _profileBeingEdited is not null)
            {
                // Update the existing profile object
                _profileBeingEdited.Name = ProfileName;
                _profileBeingEdited.ServerAddress = ServerAddress;
                _profileBeingEdited.Username = Username;
                _profileBeingEdited.ShouldSavePassword = ShouldSavePassword;

                _credentialService.SavePassword(_profileBeingEdited, PasswordInput);
                _profileService.UpdateProfile(_profileBeingEdited);
            }
            else
            {
                // Add a new profile
                var newProfile = new VCenterConnection
                {
                    Name = ProfileName,
                    ServerAddress = ServerAddress,
                    Username = Username,
                    ShouldSavePassword = ShouldSavePassword
                };
                _credentialService.SavePassword(newProfile, PasswordInput);
                _profileService.AddProfile(newProfile);
            }

            OnCancel(); // Reset the form
        }
    public void LoadProfileForEditing (VCenterConnection profile)
        {
            _profileBeingEdited = profile;
            IsEditing = true;
            FormTitle = "Edit Profile";
            ProfileName = profile.Name;
            ServerAddress = profile.ServerAddress;
            Username = profile.Username;
            ShouldSavePassword = profile.ShouldSavePassword;
            PasswordInput = string.Empty; // Always clear password field for security
        }

    [RelayCommand]
        private void OnCancel ()
        {
            IsEditing = false;
            _profileBeingEdited = null;
            FormTitle = "Add New Profile";
            ProfileName = string.Empty;
            ServerAddress = string.Empty;
            Username = string.Empty;
            PasswordInput = string.Empty;
            ShouldSavePassword = false;
        }
    }