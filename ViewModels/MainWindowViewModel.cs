using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Views.Pages;
using Wpf.Ui;
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "IDE0060:Remove unused parameter",
        Justification = "Demo"
    )]
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
            
            new NavigationViewItem()
            {
                Content = "Dashboard",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 }, 
                TargetPageType = typeof(Views.Pages.DashboardPage),
            },
            new NavigationViewItem()
            {
                Content = "vCenter Migration",
                Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowSwap24 },
                TargetPageType = typeof(Views.Pages.VCenterMigrationPage),
            },
            new NavigationViewItem()
            {
                Content = "ESXi Host Migration",
                Icon = new SymbolIcon { Symbol = SymbolRegular.Server24 },
                TargetPageType = typeof(Views.Pages.HostMigrationPage),
                
            },
            new NavigationViewItem()
            {
                Content = "Network Migration",
                Icon = new SymbolIcon { Symbol = SymbolRegular.NetworkCheck24 },
                TargetPageType = typeof(Views.Pages.NetworkMigrationPage),
            },            
            new NavigationViewItem()
                {
                    Content = "VM Migration",
                    Icon = new SymbolIcon {Symbol = SymbolRegular.Desktop24 },
                    TargetPageType = typeof(Views.Pages.VmMigrationPage),
                    
                },
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

        TrayMenuItems = [new() { Header = "Dashboard", Tag = "tray_home" }];

        _isInitialized = true;
    }
}