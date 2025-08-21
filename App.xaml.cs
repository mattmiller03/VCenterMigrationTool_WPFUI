// In App.xaml.cs
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
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
            services.AddSingleton<PowerShellLoggingService>();

            // Custom Application Services
            services.AddSingleton<ConnectionProfileService>();
            services.AddSingleton<CredentialService>();
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton<SharedConnectionService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();



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
            services.AddSingleton<ActivityLogsViewModel>();
            services.AddSingleton<ActivityLogsPage>();

            // Settings Page and its sub-ViewModels
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<AppearanceSettingsViewModel>();
            services.AddTransient<PowerShellSettingsViewModel>();
            services.AddTransient<FilePathsSettingsViewModel>();
            services.AddTransient<ViewProfilesViewModel>();
            services.AddTransient<ProfileEditorViewModel>();
            services.AddSingleton<PersistentExternalConnectionService>();
            services.AddTransient<ViewModels.Dialogs.ErrorDialogViewModel>();

            // Dialogs
            services.AddTransient<PasswordPromptDialog>();
            services.AddTransient<PasswordPromptViewModel>();
            services.AddTransient<ErrorDialog>();

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
                var activeProcessCount = powerShellService.GetActiveProcessCount();
                logger?.LogInformation("Cleaning up {ProcessCount} active PowerShell processes before shutdown", activeProcessCount);
                powerShellService.CleanupAllProcesses();
                logger?.LogInformation("PowerShell process cleanup completed");
                }

            // Clean up persistent connection service processes
            var persistentConnectionService = Host.Services.GetService<PersistentExternalConnectionService>();
            if (persistentConnectionService != null)
                {
                logger?.LogInformation("Cleaning up persistent vCenter connection processes");
                persistentConnectionService.Dispose(); // This should clean up persistent PowerShell processes
                logger?.LogInformation("Persistent connection service cleanup completed");
                }

            // Clean up ViewModels with timers/resources
            var powerShellSettingsVM = Host.Services.GetService<PowerShellSettingsViewModel>();
            if (powerShellSettingsVM != null)
                {
                logger?.LogInformation("Stopping PowerShell monitoring timer");
                powerShellSettingsVM.StopProcessMonitoring();
                }

            // Clean up other ViewModels if needed
            var activityLogsVM = Host.Services.GetService<ActivityLogsViewModel>();
            activityLogsVM?.Dispose();

            // Final cleanup: Kill any remaining PowerShell processes created by this app
            await PerformFinalProcessCleanup(logger);
            }
        catch (Exception ex)
            {
            logger?.LogError(ex, "Error during service cleanup");
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

    /// <summary>
    /// Final cleanup to kill any remaining PowerShell processes that might have been left behind
    /// </summary>
    private async Task PerformFinalProcessCleanup(ILogger<App>? logger)
    {
        try
        {
            // Get all PowerShell processes running on the system
            var allPowerShellProcesses = Process.GetProcessesByName("pwsh")
                .Concat(Process.GetProcessesByName("powershell"))
                .ToArray();

            if (allPowerShellProcesses.Length == 0)
            {
                logger?.LogInformation("No PowerShell processes found during final cleanup");
                return;
            }

            logger?.LogInformation("Found {Count} PowerShell processes during final cleanup scan", allPowerShellProcesses.Length);

            // Kill PowerShell processes that were likely created by this application
            // We'll identify them by command line arguments or creation time near our app's startup
            var killed = 0;
            var appStartTime = Process.GetCurrentProcess().StartTime;

            foreach (var process in allPowerShellProcesses)
            {
                try
                {
                    // Skip processes that started before our app (likely system or user processes)
                    if (process.StartTime < appStartTime.AddSeconds(-30))
                        continue;

                    // Try to get command line to identify our processes
                    var isOurProcess = false;
                    try
                    {
                        // Check if it's running our scripts or has our signature parameters
                        var commandLine = GetCommandLine(process);
                        if (!string.IsNullOrEmpty(commandLine) && 
                            (commandLine.Contains("VCenterMigrationTool") || 
                             commandLine.Contains("Get-Clusters") ||
                             commandLine.Contains("Get-ClusterItems") ||
                             commandLine.Contains("BypassModuleCheck") ||
                             commandLine.Contains("SuppressConsoleOutput")))
                        {
                            isOurProcess = true;
                        }
                    }
                    catch
                    {
                        // If we can't read command line, check if it's a recent process
                        // and assume it might be ours if it started recently
                        var timeDiff = DateTime.Now - process.StartTime;
                        if (timeDiff.TotalMinutes < 30) // Started within last 30 minutes
                        {
                            isOurProcess = true;
                        }
                    }

                    if (isOurProcess)
                    {
                        logger?.LogInformation("Killing potentially orphaned PowerShell process PID {PID} (started: {StartTime})", 
                            process.Id, process.StartTime);
                        
                        process.Kill(entireProcessTree: true);
                        await Task.Delay(100); // Small delay to allow process to terminate
                        killed++;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Error cleaning up PowerShell process PID {PID}", process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (killed > 0)
            {
                logger?.LogWarning("Killed {Count} potentially orphaned PowerShell processes during final cleanup", killed);
            }
            else
            {
                logger?.LogInformation("No orphaned PowerShell processes found during final cleanup");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error during final PowerShell process cleanup");
        }
    }

    /// <summary>
    /// Get command line arguments for a process (Windows-specific)
    /// </summary>
    private string GetCommandLine(Process process)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
            using var objects = searcher.Get();
            
            foreach (System.Management.ManagementObject obj in objects)
            {
                return obj["CommandLine"]?.ToString() ?? "";
            }
        }
        catch
        {
            // Fallback - unable to read command line
        }
        return "";
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