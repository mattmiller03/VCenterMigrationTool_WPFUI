using Microsoft.Extensions.DependencyInjection;
using System;
using VCenterMigrationTool.ViewModels;
using VCenterMigrationTool.Views.Dialogs;

namespace VCenterMigrationTool.Services;

public class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public DialogService (IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // --- FIX: Change return type from SecureString to string ---
    public (bool?, string?) ShowPasswordDialog (string title, string message)
    {
        var viewModel = _serviceProvider.GetRequiredService<PasswordPromptViewModel>();
        var dialog = _serviceProvider.GetRequiredService<PasswordPromptDialog>();

        viewModel.Title = title;
        viewModel.Message = message;

        dialog.Owner = App.Current.MainWindow;
        dialog.DataContext = viewModel;

        bool? result = dialog.ShowDialog();

        return (result, result == true ? viewModel.Password : null);
    }
}