// In Services/ApplicationHostService.cs
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.Views.Windows;
using Wpf.Ui;

namespace VCenterMigrationTool.Services;

/// <summary>
/// Manages the application lifecycle, including showing the main window.
/// </summary>
public class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private INavigationWindow? _navigationWindow;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Triggered when the application host is ready to start the service.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleActivationAsync();
    }

    /// <summary>
    /// Triggered when the application host is performing a graceful shutdown.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Creates and shows the main window, then navigates to the initial page.
    /// </summary>
    private async Task HandleActivationAsync()
    {
        await Task.CompletedTask;

        if (!Application.Current.Windows.OfType<MainWindow>().Any())
        {
            // Get the main window from the service provider
            _navigationWindow = (_serviceProvider.GetService(typeof(INavigationWindow)) as INavigationWindow)!;

            // Show the window
            _navigationWindow!.ShowWindow();

            // Navigate to the initial page - changed from Dashboard to VCenter Migration
            _navigationWindow.Navigate(typeof(VCenterMigrationPage));
        }
    }
}