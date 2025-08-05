using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;


// In Views/Pages/SettingsPage.xaml.cs

using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

// In Views/Pages/SettingsPage.xaml.cs

namespace VCenterMigrationTool.Views.Pages
{
    public partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
