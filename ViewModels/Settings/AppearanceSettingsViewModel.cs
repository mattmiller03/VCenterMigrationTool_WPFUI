using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using VCenterMigrationTool.Services;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace VCenterMigrationTool.ViewModels.Settings;

public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly ConfigurationService _configurationService;

    [ObservableProperty]
    private ApplicationTheme _currentTheme;

    [ObservableProperty]
    private string _currentAccentColor = "Blue";

    [ObservableProperty]
    private ObservableCollection<AccentColorOption> _availableAccentColors;

    public AppearanceSettingsViewModel(IThemeService themeService, ConfigurationService configurationService)
    {
        _themeService = themeService;
        _configurationService = configurationService;
        
        // Initialize available accent colors
        _availableAccentColors = new ObservableCollection<AccentColorOption>
        {
            new AccentColorOption("Blue", "#0078d4"),
            new AccentColorOption("Green", "#107c10"),
            new AccentColorOption("Red", "#d13438"),
            new AccentColorOption("Purple", "#881798"),
            new AccentColorOption("Orange", "#ff8c00"),
            new AccentColorOption("Teal", "#00bcf2"),
            new AccentColorOption("Pink", "#e3008c"),
            new AccentColorOption("Yellow", "#ffd700")
        };

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var config = _configurationService.GetConfiguration();
        
        // Load theme
        CurrentTheme = config.ApplicationTheme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark
        };
        
        // Load accent color
        CurrentAccentColor = config.AccentColor ?? "Blue";
        
        // Apply loaded settings
        _themeService.SetTheme(CurrentTheme);
        ApplyAccentColor(CurrentAccentColor);
    }

    [RelayCommand]
    private async Task OnChangeTheme(string parameter)
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
            
            // Save to configuration
            await SaveThemeSettings();
        }
    }

    [RelayCommand]
    private async Task OnChangeAccentColor(string colorName)
    {
        if (CurrentAccentColor != colorName)
        {
            CurrentAccentColor = colorName;
            ApplyAccentColor(colorName);
            
            // Save to configuration
            await SaveThemeSettings();
        }
    }

    private void ApplyAccentColor(string colorName)
    {
        var colorHex = AvailableAccentColors.FirstOrDefault(c => c.Name == colorName)?.HexValue ?? "#0078d4";
        
        try
        {
            // TODO: Apply accent color when WPF-UI API is available
            // Note: WPF-UI 4.0.3 doesn't have Wpf.Ui.Appearance.Accent class
            System.Diagnostics.Debug.WriteLine($"Accent color would be applied: {colorHex}");
        }
        catch (System.Exception)
        {
            // Fallback if accent color application fails
        }
    }

    private async Task SaveThemeSettings()
    {
        var config = _configurationService.GetConfiguration();
        config.ApplicationTheme = CurrentTheme.ToString();
        config.AccentColor = CurrentAccentColor;
        
        await _configurationService.SaveConfigurationAsync(config);
    }
}

public class AccentColorOption
{
    public string Name { get; set; }
    public string HexValue { get; set; }

    public AccentColorOption(string name, string hexValue)
    {
        Name = name;
        HexValue = hexValue;
    }
}