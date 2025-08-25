using System;
using System.Windows;
using VCenterMigrationTool.Services;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Views.Windows
{
    /// <summary>
    /// Interaction logic for the main application window.
    /// </summary>
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow (MainWindowViewModel viewModel, INavigationService navigationService, IPageService pageService, ConfigurationService configurationService, IThemeService themeService)
        {
            ViewModel = viewModel;
            // FIX: DataContext is the ViewModel
            DataContext = viewModel;

            // Initialize theme settings before anything else
            InitializeThemeSettings(configurationService, themeService);

            SystemThemeWatcher.Watch(this);
            InitializeComponent();

            navigationService.SetNavigationControl(RootNavigation);
        }

        private void InitializeThemeSettings(ConfigurationService configurationService, IThemeService themeService)
        {
            try
            {
                var config = configurationService.GetConfiguration();
                
                // Apply saved theme
                var savedTheme = config.ApplicationTheme switch
                {
                    "Light" => ApplicationTheme.Light,
                    "Dark" => ApplicationTheme.Dark,
                    _ => ApplicationTheme.Dark
                };
                
                themeService.SetTheme(savedTheme);
                
                // TODO: Apply saved accent color when WPF-UI API is available
                // Note: Accent color application will be implemented when the correct API is found
            }
            catch (Exception ex)
            {
                // Fallback to default theme if loading fails
                System.Diagnostics.Debug.WriteLine($"Failed to load theme settings: {ex.Message}");
                themeService.SetTheme(ApplicationTheme.Dark);
                // TODO: Apply default accent color when WPF-UI API is available
            }
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider pageProvider) => RootNavigation.SetPageProviderService(pageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        public void SetServiceProvider(IServiceProvider serviceProvider) { }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            Application.Current.Shutdown();
        }
    }
}