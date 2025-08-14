using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class NetworkMigrationPage : Page
    {
        public NetworkMigrationViewModel ViewModel { get; }

        public NetworkMigrationPage (NetworkMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            // This line sets the DataContext directly to the ViewModel,
            // which is the correct pattern for this application.
            DataContext = viewModel;

            InitializeComponent();
        }
    }
}