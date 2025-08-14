using System.Windows;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Views.Dialogs;

public partial class PasswordPromptDialog : FluentWindow
{
    public PasswordPromptViewModel ViewModel { get; }

    public PasswordPromptDialog (PasswordPromptViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void OkButton_Click (object sender, RoutedEventArgs e)
    {
        // --- FIX: Change from SecurePassword to Password ---
        ViewModel.Password = PasswordBox.Password;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click (object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}