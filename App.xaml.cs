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
using System.Net.Http;
using System.Security.Authentication;
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
            services.AddSingleton<PowerShellPathService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();

            // Phase 3 Unified Services (Consolidation)
            services.AddSingleton<UnifiedPowerShellService>();
            services.AddSingleton<UnifiedConnectionService>();
            
            // Phase 2 Refactored Services (Legacy - to be deprecated)
            services.AddSingleton<PowerShellProcessManager>();
            services.AddSingleton<ConnectionStateManager>();
            services.AddSingleton<PowerCLIConfigurationService>();

            // HTTP Client for vSphere API
            services.AddHttpClient<VSphereApiService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() =>
            {
                // Most aggressive HttpClientHandler with client certificate support
                var handler = new HttpClientHandler()
                {
                    // Bypass ALL SSL certificate validation - MOST AGGRESSIVE SETTING
                    ServerCertificateCustomValidationCallback = (httpRequestMessage, certificate, chain, sslPolicyErrors) =>
                    {
                        // Log every SSL bypass attempt for debugging
                        System.Diagnostics.Debug.WriteLine($"ULTRA-AGGRESSIVE SSL BYPASS: {certificate?.Subject}, Errors: {sslPolicyErrors}");
                        Console.WriteLine($"ULTRA-AGGRESSIVE SSL BYPASS: {certificate?.Subject}, Errors: {sslPolicyErrors}");
                        
                        // ALWAYS return true regardless of any SSL errors including PartialChain
                        return true;
                    },
                    
                    // Additional aggressive SSL bypass settings for .NET 8
                    ClientCertificateOptions = ClientCertificateOption.Automatic,
                    
                    // Most permissive SSL protocols - prefer newer protocols but allow all
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                    
                    // Disable all certificate validation checks
                    CheckCertificateRevocationList = false,
                    UseCookies = false,
                    
                    // Additional .NET 8 specific settings for certificate bypass
                    MaxConnectionsPerServer = 10
                };

                // Try to get and add client certificates from the certificate store
                try
                {
                    using (var store = new System.Security.Cryptography.X509Certificates.X509Store(
                        System.Security.Cryptography.X509Certificates.StoreName.My, 
                        System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser))
                    {
                        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                        
                        // Look for certificates that might be suitable for client authentication
                        var certificates = store.Certificates.Find(
                            System.Security.Cryptography.X509Certificates.X509FindType.FindByKeyUsage,
                            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature,
                            false);
                        
                        if (certificates.Count > 0)
                        {
                            Console.WriteLine($"Found {certificates.Count} potential client certificates");
                            
                            // Add all available certificates - let the system choose the best one
                            foreach (var cert in certificates)
                            {
                                handler.ClientCertificates.Add(cert);
                                Console.WriteLine($"Added client certificate: {cert.Subject}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("No client certificates found in certificate store");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading client certificates: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Client certificate error: {ex.Message}");
                }

                return handler;
            });
            services.AddSingleton<VSphereApiService>();
            services.AddSingleton<SharedConnectionService>();

            // UPDATED: Use HybridPowerShellService instead of PowerShellService
            services.AddSingleton<HybridPowerShellService>();
            services.AddSingleton<VCenterInventoryService>();

            // Main Window and ViewModel
            services.AddSingleton<INavigationWindow, MainWindow>();
            services.AddSingleton<MainWindowViewModel>();

            // Standard Pages and ViewModels
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
            // ActivityLogsPage removed - each page now has its own dashboard-style activity log

            // New Migration Pages and ViewModels
            services.AddSingleton<InfrastructureMigrationPage>();
            services.AddSingleton<InfrastructureMigrationViewModel>();
            // VirtualMachinesMigrationPage removed - functionality merged into VmMigrationPage
            services.AddSingleton<AdminConfigMigrationPage>();
            services.AddSingleton<AdminConfigMigrationViewModel>();

            // Settings Page and its sub-ViewModels
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<AppearanceSettingsViewModel>();
            services.AddTransient<PowerShellSettingsViewModel>();
            services.AddTransient<FilePathsSettingsViewModel>();
            services.AddTransient<ViewProfilesViewModel>();
            services.AddTransient<ProfileEditorViewModel>();
            services.AddSingleton<PersistantVcenterConnectionService>();
            services.AddSingleton<SharedPowerShellSessionService>();
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
        // ULTRA-AGGRESSIVE SSL certificate bypass for vCenter API connections (.NET 8 compatible)
        System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
        {
            // Log certificate issues for debugging including PartialChain
            if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
            {
                System.Diagnostics.Debug.WriteLine($"ULTRA-AGGRESSIVE ServicePointManager SSL bypass for: {certificate?.Subject}, Errors: {sslPolicyErrors}");
                Console.WriteLine($"ULTRA-AGGRESSIVE ServicePointManager SSL bypass for: {certificate?.Subject}, Errors: {sslPolicyErrors}");
            }
            // ALWAYS accept ALL certificates regardless of any SSL errors including PartialChain
            return true;
        };
        
        // .NET 8 compatible SSL/TLS settings - most aggressive configuration
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;
        System.Net.ServicePointManager.CheckCertificateRevocationList = false;
        System.Net.ServicePointManager.Expect100Continue = false;
        System.Net.ServicePointManager.MaxServicePoints = 0;
        System.Net.ServicePointManager.DefaultConnectionLimit = 1000;
        
        // Runtime certificate bypass at AppDomain level
        AppDomain.CurrentDomain.SetData("DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER", "0");
        
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
            var persistentConnectionService = Host.Services.GetService<PersistantVcenterConnectionService>();
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
            // ActivityLogsViewModel removed - each page now manages its own activity log

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

                    // Identify our processes by timing and process characteristics
                    var isOurProcess = false;
                    try
                    {
                        // Check if it's a recent process that could be ours
                        var timeDiff = DateTime.Now - process.StartTime;
                        
                        // Only consider processes started after our app started or recently
                        if (timeDiff.TotalMinutes < 30)
                        {
                            // Additional checks to be more certain it's our process
                            try
                            {
                                // Check if the process was started from our application directory
                                var processPath = process.MainModule?.FileName ?? "";
                                var appPath = Environment.CurrentDirectory;
                                
                                // If PowerShell is running and started recently, and either:
                                // 1. Was started from our app directory, or
                                // 2. Started very recently (within 5 minutes - likely our scripts)
                                if ((processPath.Contains("pwsh") || processPath.Contains("powershell")) &&
                                    (processPath.StartsWith(appPath, StringComparison.OrdinalIgnoreCase) || 
                                     timeDiff.TotalMinutes < 5))
                                {
                                    isOurProcess = true;
                                }
                            }
                            catch
                            {
                                // If we can't get detailed info, assume recent PowerShell processes might be ours
                                if (timeDiff.TotalMinutes < 5) // Very recent - likely ours
                                {
                                    isOurProcess = true;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip this process if we can't analyze it
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