using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using VCenterMigrationTool.Messages;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class ViewProfilesViewModel : ObservableObject
    {
    private readonly ConnectionProfileService _profileService;
    private readonly CredentialService _credentialService;
    private readonly HybridPowerShellService _powerShellService;
    private readonly ConfigurationService _configurationService;
    private readonly IMessenger _messenger;
    private readonly ILogger<ViewProfilesViewModel> _logger;

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
        IMessenger messenger,
        ILogger<ViewProfilesViewModel> logger)
        {
        _profileService = profileService;
        _credentialService = credentialService;
        _powerShellService = powerShellService;
        _configurationService = configurationService;
        _messenger = messenger;
        _logger = logger;
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

        // FIXED: Add BypassModuleCheck manually when PowerCLI is confirmed installed
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            _logger.LogInformation("DEBUG: [Settings Test] Added BypassModuleCheck=true for profile test");
            }
        else
            {
            _logger.LogInformation("DEBUG: [Settings Test] PowerCLI not confirmed installed, not adding bypass");
            }

        // Enhanced debug logging for settings page testing (SECURE - no passwords)
        _logger.LogInformation("DEBUG: [Settings Test] PowerCliConfirmedInstalled = {PowerCliConfirmed}",
            HybridPowerShellService.PowerCliConfirmedInstalled);

        // SECURE: Log parameters excluding sensitive data
        var safeParams = scriptParams
            .Where(p => !p.Key.ToLower().Contains("password"))
            .Select(p => $"{p.Key}={p.Value}");
        _logger.LogInformation("DEBUG: [Settings Test] Safe parameters: {Parameters}", string.Join(", ", safeParams));

        string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
        string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Test-vCenterConnection.ps1", scriptParams, logPath);

        ConnectionStatus = result.Trim() == "Success" ? "Connection successful!" : $"Failed: {result.Replace("Failure:", "").Trim()}";
        IsBusy = false;
        }
    }