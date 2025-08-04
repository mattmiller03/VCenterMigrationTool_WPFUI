// In Services/ApplicationHostService.cs
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.Views.Windows;
using VCenterMigrationTool.Helpers;
using Wpf.Ui;
using Wpf.Ui.Controls; // <-- CHANGE/ADD this namespace for INavigationWindow

namespace VCenterMigrationTool.Services;

public class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
{
    private INavigationWindow? _navigationWindow; // Use INavigationWindow from Wpf.Ui.Controls


    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Triggered when the application host starts.
    /// </summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }
    private async Task HandleActivationAsync()
    {
        await Task.CompletedTask;

        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            _navigationWindow = (serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;
            _navigationWindow!.ShowWindow();
            _ = _navigationWindow.Navigate(typeof(Views.Pages.ConnectionPage));
        }
        await Task.CompletedTask;
    }
}