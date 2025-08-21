using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class InfrastructureMigrationPage : Page
    {
        public InfrastructureMigrationViewModel ViewModel { get; }

        public InfrastructureMigrationPage(InfrastructureMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;

            InitializeComponent();
        }
    }
}