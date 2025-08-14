using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class VCenterMigrationPage : Page
    {
        public VCenterMigrationViewModel ViewModel { get; }

        public VCenterMigrationPage (VCenterMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}