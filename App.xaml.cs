// In App.xaml.cs
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using VCenterMigrationTool.ViewModels;
using VCenterMigrationTool.ViewModels.Settings;
using VCenterMigrationTool.Views.Dialogs;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.Views.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.DependencyInjection;

namespace VCenterMigrationTool;

public partial class App
    {
    private static readonly IHost Host = Microsoft.Extensions.Hosting.Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(c => c.SetBasePath(AppContext.BaseDirectory))
        .UseSerilog((context, services, configuration) =>
        {
            // Get the configuration service to determine log path
            var tempServiceProvider = new ServiceCollection()
                .AddSingleton<ConfigurationService>()
                .AddLogging()
                .BuildServiceProvider();

            var configService = tempServiceProvider.GetRequiredService<ConfigurationService>();
            var appConfig = configService.GetConfiguration();

            // Use the configured log path or fallback to default
            var logPath = !string.IsNullOrEmpty(appConfig.LogPath)
                ? appConfig.LogPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCenterMigrationTool", "Logs");

            // Ensure log directory exists
            Directory.CreateDirectory(logPath);

            var logFilePath = Path.Combine(logPath, "log-.txt");

            configuration
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Debug()
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                );
        })
        .ConfigureServices((context, services) =>
        {
            // App Host
            services.AddHostedService<ApplicationHostService>();

            // Messaging Service
            services.AddSingleton<IMessenger, WeakReferenceMessenger>();

            // --- FIX: Explicitly register the page provider ---
            services.AddSingleton<INavigationViewPageProvider, DependencyInjectionNavigationViewPageProvider>();

            // Wpf.Ui Services
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ITaskBarService, TaskBarService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Custom Application Services
            services.AddSingleton<ConnectionProfileService>();
            services.AddSingleton<CredentialService>();
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton<SharedConnectionService>();
            services.AddSingleton<IDialogService, DialogService>();

            // UPDATED: Use HybridPowerShellService instead of PowerShellService
            services.AddSingleton<HybridPowerShellService>();

            // Main Window and ViewModel
            services.AddSingleton<INavigationWindow, MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            // Standard Pages and ViewModels
            services.AddSingleton<DashboardPage>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<VCenterMigrationPage>();
            services.AddSingleton<VCenterMigrationViewModel>();
            services.AddTransient<EsxiHostsViewModel>();
            services.AddTransient<EsxiHostsPage>();
            services.AddSingleton<VmMigrationPage>();
            services.AddSingleton<VmMigrationViewModel>();
            services.AddSingleton<NetworkMigrationPage>();
            services.AddSingleton<NetworkMigrationViewModel>();
            services.AddSingleton<ResourcePoolMigrationPage>();
            services.AddSingleton<ResourcePoolMigrationViewModel>();

            // Settings Page and its sub-ViewModels
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<AppearanceSettingsViewModel>();
            services.AddTransient<PowerShellSettingsViewModel>();
            services.AddTransient<FilePathsSettingsViewModel>();
            services.AddTransient<ViewProfilesViewModel>();
            services.AddTransient<ProfileEditorViewModel>();
            services.AddSingleton<PersistentExternalConnectionService>();

            // Dialogs
            services.AddTransient<PasswordPromptDialog>();
            services.AddTransient<PasswordPromptViewModel>();

            // Configuration
            services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));
        })
        .Build();

    public static T? GetService<T> () where T : class
        {
        return Host.Services.GetService(typeof(T)) as T;
        }

    private async void OnStartup (object sender, StartupEventArgs e)
        {
        await Host.StartAsync();
        }

    private async void OnExit (object sender, ExitEventArgs e)
        {
        // Get logger from the host services
        var logger = Host.Services.GetService<ILogger<App>>();
        logger?.LogInformation("Application shutting down - cleaning up services");

        try
            {
            // Get the PowerShell service and clean up processes
            var powerShellService = Host.Services.GetService<HybridPowerShellService>();
            if (powerShellService != null)
                {
                logger?.LogInformation("Cleaning up PowerShell processes before shutdown");
                powerShellService.CleanupAllProcesses();
                }
            }
        catch (Exception ex)
            {
            logger?.LogError(ex, "Error during PowerShell service cleanup");
            }

        try
            {
            await Host.StopAsync();
            Host.Dispose();
            }
        catch (Exception ex)
            {
            logger?.LogError(ex, "Error during host shutdown");
            }
        }

    private void OnDispatcherUnhandledException (object sender, DispatcherUnhandledExceptionEventArgs e)
        {
        // Log the unhandled exception using Serilog
        Log.Error(e.Exception, "An unhandled exception occurred: {Message}", e.Exception.Message);

        // Show a user-friendly message
        MessageBox.Show("An unexpected error occurred. Please check the logs for more details.", "Application Error",
            MessageBoxButton.OK, MessageBoxImage.Error);

        // Prevent the application from crashing
        e.Handled = true;
        }
    }