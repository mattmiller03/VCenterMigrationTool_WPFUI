using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using System.IO;
using System.Windows.Threading; // Required for the exception handler
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using VCenterMigrationTool.ViewModels;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.Views.Windows;
using VCenterMigrationTool.Helpers;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection; // The fix for the first error


namespace VCenterMigrationTool;

public partial class App : Application
{
    private static readonly IHost _host = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            var basePath =
                Path.GetDirectoryName(AppContext.BaseDirectory)
                ?? throw new DirectoryNotFoundException(
                    "Unable to find the base directory of the application."
                );
            _ = c.SetBasePath(basePath);
        })
        .ConfigureServices((context, services) =>
        {
            _ = services.AddNavigationViewPageProvider();

            // App Host
            _ = services.AddHostedService<ApplicationHostService>();
            // Theme manipulation
            _ = services.AddSingleton<IThemeService, ThemeService>();
            // TaskBar manipulation
            _ = services.AddSingleton<ITaskBarService, TaskBarService>();

            // Service containing navigation, same as INavigationWindow... but without window
            _ = services.AddSingleton<INavigationService, NavigationService>();

            // Main window with navigation
            _ = services.AddSingleton<INavigationWindow, Views.Windows.MainWindow>();
            _ = services.AddSingleton<ViewModels.MainWindowViewModel>();
            // Views and ViewModels
            _ = services.AddSingleton<Views.Pages.DashboardPage>();
            _ = services.AddSingleton<ViewModels.DashboardViewModel>();
            _ = services.AddSingleton<Views.Pages.ConnectionPage>();
            _ = services.AddSingleton<ViewModels.ConnectionViewModel>();
            _ = services.AddSingleton<Views.Pages.SettingsPage>();
            _ = services.AddSingleton<ViewModels.SettingsViewModel>();

            // Configuration
            _ = services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));

        }).Build();

    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services
    {
        get { return _host.Services; }
    }

    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        await _host.StartAsync();
    }

    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();

        _host.Dispose();
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // For more info see https://docs.microsoft.com/en-us/dotnet/api/system.windows.application.dispatcherunhandledexception?view=windowsdesktop-6.0
    }
}