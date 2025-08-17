using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class VCenterMigrationPage : Page, INavigableView<VCenterMigrationViewModel>
    {
        public VCenterMigrationViewModel ViewModel { get; }

        public VCenterMigrationPage (VCenterMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this; // Key: DataContext is the Page, not the ViewModel

            InitializeComponent();
        }
    }
}