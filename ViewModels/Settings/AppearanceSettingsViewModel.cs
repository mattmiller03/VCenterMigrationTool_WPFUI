using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private ApplicationTheme _currentTheme;

    public AppearanceSettingsViewModel (IThemeService themeService)
    {
        _themeService = themeService;
        _currentTheme = _themeService.GetTheme();
    }

    [RelayCommand]
    private void OnChangeTheme (string parameter)
    {
        var newTheme = parameter switch
        {
            "theme_light" => ApplicationTheme.Light,
            _ => ApplicationTheme.Dark
        };

        if (_themeService.GetTheme() != newTheme)
        {
            _themeService.SetTheme(newTheme);
            CurrentTheme = newTheme;
        }
    }
}