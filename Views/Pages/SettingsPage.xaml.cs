using System.Windows;
using System.Windows.Controls;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage (SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
        }

        private void TreeViewItem_Selected (object sender, RoutedEventArgs e)
        {
            // This event handler ensures the ViewModel's SelectedCategory is always in sync
            // with what the user clicks in the TreeView.
            if (e.OriginalSource is TreeViewItem { DataContext: SettingsCategory selectedCategory })
            {
                ViewModel.SelectedCategory = selectedCategory;
            }
            e.Handled = true; // Prevents the event from bubbling up and causing side effects
        }
    }
}