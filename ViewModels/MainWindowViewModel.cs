using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        // FIX: Removed redundant '= null;' initializer
        [ObservableProperty]
        private string _applicationTitle;

        [ObservableProperty]
        private ObservableCollection<object> _navigationItems = new();

        [ObservableProperty]
        private ObservableCollection<object> _navigationFooter = new();

        [ObservableProperty]
        private ObservableCollection<MenuItem> _trayMenuItems = new();

        public MainWindowViewModel (INavigationService navigationService)
        {
            _applicationTitle = "VCenter Migration Tool";

            // Create nested navigation structure
            var vCenterObjectsChildItems = new List<object>
            {
                new NavigationViewItem("Resource Pools", SymbolRegular.DatabaseStack16, typeof(Views.Pages.ResourcePoolMigrationPage)),
                new NavigationViewItem("Network", SymbolRegular.NetworkCheck24, typeof(Views.Pages.NetworkMigrationPage))
            };
            var vCenterObjectsItem = new NavigationViewItem("vCenter Objects", SymbolRegular.Box24, typeof(Views.Pages.VCenterMigrationPage), vCenterObjectsChildItems);

            NavigationItems = new ObservableCollection<object>
            {
                new NavigationViewItem("Dashboard", SymbolRegular.Home24, typeof(Views.Pages.DashboardPage)),
                vCenterObjectsItem,
                new NavigationViewItem("ESXi Hosts", SymbolRegular.Server24, typeof(Views.Pages.EsxiHostsPage)),
                new NavigationViewItem("Virtual Machines", SymbolRegular.Desktop24, typeof(Views.Pages.VmMigrationPage)),
                new NavigationViewItem("Activity Logs", SymbolRegular.List24, typeof(Views.Pages.ActivityLogsPage))
            };

            NavigationFooter = new ObservableCollection<object>
            {
                new NavigationViewItem("Settings", SymbolRegular.Settings24, typeof(Views.Pages.SettingsPage))
            };

            TrayMenuItems = new ObservableCollection<MenuItem>
            {
                new MenuItem { Header = "Home", Tag = "tray_home" }
            };
        }
    }
}