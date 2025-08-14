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

        public MainWindow (MainWindowViewModel viewModel, INavigationService navigationService, IPageService pageService)
        {
            ViewModel = viewModel;
            // FIX: DataContext is the ViewModel
            DataContext = viewModel;

            SystemThemeWatcher.Watch(this);
            InitializeComponent();

            navigationService.SetNavigationControl(RootNavigation);
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