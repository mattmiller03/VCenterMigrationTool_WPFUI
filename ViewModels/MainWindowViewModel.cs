// In ViewModels/MainWindowViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Views.Pages;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<object> _menuItems;

    [ObservableProperty]
    private ObservableCollection<object> _footerMenuItems;

    [ObservableProperty]
    private ObservableCollection<MenuItem> _trayMenuItems;

    public MainWindowViewModel(IOptions<AppConfig> appConfig)
    {
        // Set the title from the configuration file
        ApplicationTitle = appConfig.Value.ApplicationTitle ?? "vCenter Migration Tool";

        _menuItems = new()
        {
            new NavigationViewItem("Dashboard", SymbolRegular.Home24, typeof(DashboardPage)),
            new NavigationViewItem("vCenter Migration", SymbolRegular.ArrowSwap24, typeof(VCenterMigrationPage)),
            new NavigationViewItem("ESXi Host Migration", SymbolRegular.Server24, typeof(DashboardPage)),
            new NavigationViewItem("VM Migration", SymbolRegular.Desktop24, typeof(DashboardPage))
        };

        _footerMenuItems = new()
        {
            new NavigationViewItem("Settings", SymbolRegular.Settings24, typeof(SettingsPage))
        };

        _trayMenuItems = new()
        {
            new MenuItem { Header = "Home", Tag = "tray_home" }
        };
    }
}