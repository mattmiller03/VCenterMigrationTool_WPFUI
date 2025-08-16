using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class ResourcePoolMigrationPage : Page
    {
        public ResourcePoolMigrationViewModel ViewModel { get; }

        public ResourcePoolMigrationPage (ResourcePoolMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}