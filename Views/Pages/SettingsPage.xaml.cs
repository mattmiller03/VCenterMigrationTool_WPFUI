
// In Views/Pages/SettingsPage.xaml.cs
using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            // Change the DataContext to be the ViewModel directly
            DataContext = this;

            InitializeComponent();
        }
    }
}