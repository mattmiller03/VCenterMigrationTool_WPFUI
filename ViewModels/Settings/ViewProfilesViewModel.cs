using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VCenterMigrationTool.Messages;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class ViewProfilesViewModel : ObservableObject
    {
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly HybridPowerShellService _powerShellService;
    private readonly ConfigurationService _configurationService;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private ObservableCollection<VCenterConnection> _profiles;

    [ObservableProperty]
    private VCenterConnection? _selectedProfile;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _connectionStatus = "Ready to test connection.";

    public ViewProfilesViewModel (
        ConnectionProfileService profileService,
        CredentialService credentialService,
        HybridPowerShellService powerShellService,
        ConfigurationService configurationService,
        IMessenger messenger)
        {
        _profileService = profileService;
        _credentialService = credentialService;
        _powerShellService = powerShellService;
        _configurationService = configurationService;
        _messenger = messenger;
        _profiles = _profileService.Profiles;
        }

    [RelayCommand]
    private void OnEditProfile (VCenterConnection? profile)
    {
        if (profile is null) return;

        // Send a message containing the profile to be edited.
        _messenger.Send(new EditProfileMessage(profile));
    }

    [RelayCommand]
    private void OnRemoveProfile (VCenterConnection? profile)
        {
        if (profile is null) return;
        _credentialService.DeletePassword(profile);
        _profileService.RemoveProfile(profile);
        }

    [RelayCommand]
    private async Task OnTestSelectedProfile ()
        {
        if (SelectedProfile is null)
            {
            ConnectionStatus = "Please select a profile from the list to test.";
            return;
            }
        IsBusy = true;
        string? password = _credentialService.GetPassword(SelectedProfile);
        if (string.IsNullOrEmpty(password))
            {
            ConnectionStatus = "Password not saved for this profile. Cannot test.";
            IsBusy = false;
            return;
            }
        ConnectionStatus = $"Testing connection to {SelectedProfile.ServerAddress}...";
        var scriptParams = new System.Collections.Generic.Dictionary<string, object>
        {
            { "VCenterServer", SelectedProfile.ServerAddress },
            { "Username", SelectedProfile.Username },
            { "Password", password }
        };
        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams, logPath);
        ConnectionStatus = result.Trim() == "Success" ? "Connection successful!" : $"Failed: {result.Replace("Failure:", "").Trim()}";
        IsBusy = false;
        }
    }