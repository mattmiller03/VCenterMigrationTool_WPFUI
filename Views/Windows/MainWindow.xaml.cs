using System;
using VCenterMigrationTool.ViewModels;
using VCenterMigrationTool.Models;
using VCenterMigrationTool.Services;
using VCenterMigrationTool.Helpers;
using VCenterMigrationTool.Views.Pages;
using VCenterMigrationTool.Views.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Views.Windows;

public partial class MainWindow : INavigationWindow
{
    public ViewModels.MainWindowViewModel ViewModel { get; }

    public MainWindow(ViewModels.MainWindowViewModel viewModel, INavigationService navigationService)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        navigationService.SetNavigationControl(RootNavigation);
    }

    public INavigationView GetNavigation() => RootNavigation;
    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);
    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
    RootNavigation.SetPageProviderService(navigationViewPageProvider);
    public void ShowWindow() => Show();

    public void CloseWindow() => Close();


    // This is the new method you need to add
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        throw new NotImplementedException();
    }
    INavigationView INavigationWindow.GetNavigation()
    {
        throw new NotImplementedException();
    }
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Make sure that closing this window will begin the process of closing the application.
        Application.Current.Shutdown();
    }
}