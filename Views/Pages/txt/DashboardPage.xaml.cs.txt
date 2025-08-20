using System.Threading.Tasks;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.Views.Pages;

public partial class DashboardPage : INavigableView<DashboardViewModel>, INavigationAware
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage (DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
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
}