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
            ConnectionStatus = "Please select a profile to test.";
            return;
            }

        IsBusy = true;
        string? password = _credentialService.GetPassword(SelectedProfile);
        if (string.IsNullOrEmpty(password))
            {
            ConnectionStatus = "No saved password for this profile.";
            _logger.LogWarning("Connection test failed for {ServerAddress}: No password saved",
                SelectedProfile.ServerAddress);
            IsBusy = false;
            return;
            }

        ConnectionStatus = $"Testing {SelectedProfile.ServerAddress}...";
        _logger.LogInformation("Starting connection test for {ServerAddress} with user {Username}",
            SelectedProfile.ServerAddress, SelectedProfile.Username);

        var scriptParams = new System.Collections.Generic.Dictionary<string, object>
        {
            { "VCenterServer", SelectedProfile.ServerAddress },
            { "Username", SelectedProfile.Username },
            { "Password", password }
        };

        // Add BypassModuleCheck when PowerCLI is confirmed installed
        if (HybridPowerShellService.PowerCliConfirmedInstalled)
            {
            scriptParams["BypassModuleCheck"] = true;
            _logger.LogInformation("Added BypassModuleCheck=true for connection test (PowerCLI confirmed installed)");
            }
        else
            {
            _logger.LogInformation("PowerCLI not confirmed installed, performing full module check");
            }

        // Log test parameters (excluding password)
        var safeParams = scriptParams
            .Where(p => !p.Key.ToLower().Contains("password"))
            .Select(p => $"{p.Key}={p.Value}");
        _logger.LogDebug("Connection test parameters: {Parameters}", string.Join(", ", safeParams));

        try
            {
            string logPath = _configurationService.GetConfiguration().LogPath ?? "Logs";
            string result = await _powerShellService.RunScriptAsync(".\\Scripts\\Active\\Test-VCenterConnection.ps1", scriptParams, logPath);

            // Parse the result and set simple status message
            if (result.Contains("Success"))
                {
                ConnectionStatus = "✅ Connection successful";
                _logger.LogInformation("Connection test PASSED for {ServerAddress}", SelectedProfile.ServerAddress);
                }
            else
                {
                ConnectionStatus = "❌ Connection failed";

                // Log the detailed error to console/logs
                var errorDetails = result.Replace("Failure:", "").Trim();
                _logger.LogError("Connection test FAILED for {ServerAddress}: {ErrorDetails}",
                    SelectedProfile.ServerAddress, errorDetails);

                // Also log the full result for debugging
                _logger.LogDebug("Full PowerShell result: {FullResult}", result);
                }
            }
        catch (System.Exception ex)
            {
            ConnectionStatus = "❌ Test error";
            _logger.LogError(ex, "Exception during connection test for {ServerAddress}", SelectedProfile.ServerAddress);
            }
        finally
            {
            IsBusy = false;
            }
        }
    }