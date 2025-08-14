using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    /// <summary>
    /// Interaction logic for HostMigrationPage.xaml
    /// </summary>
    public partial class HostMigrationPage : Page
    {
        public HostMigrationViewModel ViewModel { get; }

        public HostMigrationPage(HostMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            // FIX: DataContext is the ViewModel
            DataContext = viewModel;
            InitializeComponent();
        }
        }
}