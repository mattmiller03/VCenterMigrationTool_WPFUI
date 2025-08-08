using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    /// <summary>
    /// Interaction logic for NetworkMigrationPage.xaml
    /// </summary>
    public partial class NetworkMigrationPage : Page
    {
        public NetworkMigrationViewModel ViewModel { get; }

        public NetworkMigrationPage(NetworkMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}