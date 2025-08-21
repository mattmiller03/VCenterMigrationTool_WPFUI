using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class AdminConfigMigrationPage : Page
    {
        public AdminConfigMigrationViewModel ViewModel { get; }

        public AdminConfigMigrationPage(AdminConfigMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;

            InitializeComponent();
        }
    }
}