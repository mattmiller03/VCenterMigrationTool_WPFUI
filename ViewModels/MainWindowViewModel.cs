// In ViewModels/MainWindowViewModel.cs

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Configuration;
using VCenterMigrationTool.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels;

public partial class MainWindowViewModel : ViewModel
{
    private bool _isInitialized = false;
    [ObservableProperty]
    private string _applicationTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<object> _navigationItems = [];

    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter = [];

    [ObservableProperty]
    private ObservableCollection<MenuItem> _trayMenuItems = [];

    [ObservableProperty]
    private ObservableCollection<object> _menuItems = new()
    {
        // The items for your main navigation menu.
        // Each NavigationViewItem links a name and an icon to a specific page.
        new NavigationViewItem("Home", SymbolRegular.Home24, typeof(ConnectionPage)),
        new NavigationViewItem("1. Export / Import", SymbolRegular.ArrowUpload24, typeof(DashboardPage)), // Placeholder for now
        new NavigationViewItem("2. Host Migration", SymbolRegular.Server24, typeof(DashboardPage)),       // Placeholder for now
        new NavigationViewItem("3. VM Migration", SymbolRegular.Desktop24, typeof(DashboardPage)),      // Placeholder for now
        new NavigationViewItem("4. Validation", SymbolRegular.CheckmarkStarburst24, typeof(DashboardPage))// Placeholder for now
    };


    public MainWindowViewModel(INavigationService navigationService)
    {
        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }
    private void InitializeViewModel()
    {
        ApplicationTitle = "vCenter Migration Tool";

        NavigationItems =
        [
        new NavigationViewItem("Home", SymbolRegular.Home24, typeof(ConnectionPage)),
        new NavigationViewItem("1. Export / Import", SymbolRegular.ArrowUpload24, typeof(DashboardPage)), // Placeholder for now
        new NavigationViewItem("2. Host Migration", SymbolRegular.Server24, typeof(DashboardPage)),       // Placeholder for now
        new NavigationViewItem("3. VM Migration", SymbolRegular.Desktop24, typeof(DashboardPage)),      // Placeholder for now
        new NavigationViewItem("4. Validation", SymbolRegular.CheckmarkStarburst24, typeof(DashboardPage))// Placeholder for now
        ];

        NavigationFooter =
        [
            new NavigationViewItem()
            {
                Content = "Settings",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(Views.Pages.SettingsPage),
            },
        ];

        TrayMenuItems = [new() { Header = "Home", Tag = "tray_home" }];

        _isInitialized = true;
    }

}