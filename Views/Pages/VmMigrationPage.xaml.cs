using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    /// <summary>
    /// Interaction logic for VmMigrationPage.xaml
    /// </summary>
    public partial class VmMigrationPage : Page
    {
        public VmMigrationViewModel ViewModel { get; }

        public VmMigrationPage(VmMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}