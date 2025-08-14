using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class HostMigrationPage : Page
    {
        public HostMigrationViewModel ViewModel { get; }

        public HostMigrationPage (HostMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            // FIX: Set the DataContext directly to the ViewModel
            DataContext = viewModel;

            InitializeComponent();
        }
    }
}