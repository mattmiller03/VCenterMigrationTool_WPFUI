using System.Windows.Controls;
using VCenterMigrationTool.ViewModels;

namespace VCenterMigrationTool.Views.Pages
{
    public partial class VirtualMachinesMigrationPage : Page
    {
        public VirtualMachinesMigrationViewModel ViewModel { get; }

        public VirtualMachinesMigrationPage(VirtualMachinesMigrationViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = ViewModel;

            InitializeComponent();
        }
    }
}