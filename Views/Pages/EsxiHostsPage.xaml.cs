using System.Threading.Tasks;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.Views.Pages;

public partial class EsxiHostsPage : INavigableView<EsxiHostsViewModel>, INavigationAware
{
    public EsxiHostsViewModel ViewModel { get; }

    public EsxiHostsPage (EsxiHostsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public async Task OnNavigatedToAsync ()
    {
        // Initialize the ViewModel when navigating to this page
        await ViewModel.InitializeAsync();
    }

    public async Task OnNavigatedFromAsync ()
    {
        // Clean up if needed
        await Task.CompletedTask;
    }
}