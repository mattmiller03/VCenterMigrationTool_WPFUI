using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class VmMigrationPage : Page
    {
        public VmMigrationViewModel ViewModel { get; }

        public VmMigrationPage (VmMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            // FIX: Set the DataContext directly to the ViewModel
            DataContext = viewModel;

            InitializeComponent();
        }
    }
}