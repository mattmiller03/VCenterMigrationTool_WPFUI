using System.Windows;
using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class VCenterMigrationPage : Page, INavigableView<VCenterMigrationViewModel>
    {
        public VCenterMigrationViewModel ViewModel { get; }
        private readonly INavigationService _navigationService;

        public VCenterMigrationPage (VCenterMigrationViewModel viewModel, INavigationService navigationService)
        {
            ViewModel = viewModel;
            DataContext = ViewModel; // Fixed: Set DataContext to the ViewModel
            _navigationService = navigationService;

            InitializeComponent();
        }

        private void NavigateToResourcePools(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(ResourcePoolMigrationPage));
        }

        private void NavigateToNetwork(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(NetworkMigrationPage));
        }

        private void NavigateToInfrastructureMigration(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(InfrastructureMigrationPage));
        }

        private void NavigateToVirtualMachinesMigration(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(VirtualMachinesMigrationPage));
        }

        private void NavigateToAdminConfigMigration(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(AdminConfigMigrationPage));
        }
    }
}