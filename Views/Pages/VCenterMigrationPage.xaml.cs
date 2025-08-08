using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    /// <summary>
    /// Interaction logic for VCenterMigrationPage.xaml
    /// </summary>
    public partial class VCenterMigrationPage : Page
    {
        public VCenterMigrationViewModel ViewModel { get; }

        public VCenterMigrationPage(VCenterMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}