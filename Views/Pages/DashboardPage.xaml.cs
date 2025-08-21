using System.Threading.Tasks;
using VCenterMigrationTool.ViewModels;
using VCenterMigrationTool.Views.Pages;
using Wpf.Ui.Controls;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui;

namespace VCenterMigrationTool.Views.Pages;

public partial class DashboardPage : INavigableView<DashboardViewModel>, INavigationAware
{
    public DashboardViewModel ViewModel { get; }
    private readonly INavigationService _navigationService;

    public DashboardPage (DashboardViewModel viewModel, INavigationService navigationService)
    {
        ViewModel = viewModel;
        _navigationService = navigationService;
        DataContext = this;  // This is KEY - DataContext is the Page, not the ViewModel

        InitializeComponent();
    }

    public async Task OnNavigatedToAsync ()
    {
        await ViewModel.OnNavigatedToAsync();
    }

    public async Task OnNavigatedFromAsync ()
    {
        await ViewModel.OnNavigatedFromAsync();
    }

    private void NavigateToObjects_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.Navigate(typeof(EsxiHostsPage));
    }

    private void NavigateToMigration_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.Navigate(typeof(VCenterMigrationPage));
    }

    private void NavigateToLogs_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.Navigate(typeof(ActivityLogsPage));
    }
}