using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using VCenterMigrationTool.Services;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class FilePathsSettingsViewModel : ObservableObject
    {
    private readonly ConfigurationService _configurationService;

    [ObservableProperty]
    private string _logPath = string.Empty;
    [ObservableProperty]
    private string _exportPath = string.Empty;
    [ObservableProperty]
    private bool _settingsSavedSuccessfully;
    [ObservableProperty]
    private string _saveStatusMessage = "Ready.";

    public FilePathsSettingsViewModel (ConfigurationService configurationService)
        {
        _configurationService = configurationService;
        var config = _configurationService.GetConfiguration();
        _logPath = config.LogPath ?? "Logs";
        _exportPath = config.ExportPath ?? "Exports";
        }

    [RelayCommand]
    private async Task OnSaveChanges ()
        {
        try
            {
            var config = _configurationService.GetConfiguration();
            config.LogPath = LogPath;
            config.ExportPath = ExportPath;
            await _configurationService.SaveConfigurationAsync(config);

            SettingsSavedSuccessfully = true;
            SaveStatusMessage = "Settings saved successfully!";
            await Task.Delay(3000);
            SettingsSavedSuccessfully = false;
            SaveStatusMessage = "Ready.";
            }
        catch (Exception ex)
            {
            SettingsSavedSuccessfully = false;
            SaveStatusMessage = $"Failed to save settings: {ex.Message}";
            }
        }

    [RelayCommand]
    private void OnBrowseLogPath ()
        {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Log File Location", InitialDirectory = LogPath };
        if (dialog.ShowDialog() == true)
            {
            LogPath = dialog.FolderName;
            }
        }

    [RelayCommand]
    private void OnBrowseExportPath ()
        {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Export/Backup Location", InitialDirectory = ExportPath };
        if (dialog.ShowDialog() == true)
            {
            ExportPath = dialog.FolderName;
            }
        }
    }