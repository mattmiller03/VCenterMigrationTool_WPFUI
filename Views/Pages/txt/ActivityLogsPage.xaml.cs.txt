using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VCenterMigrationTool.ViewModels;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Views.Pages;

public partial class ActivityLogsPage : Page, INavigableView<ActivityLogsViewModel>
    {
    private bool _autoScrollEnabled = true;

    public ActivityLogsViewModel ViewModel { get; }

    public ActivityLogsPage (ActivityLogsViewModel viewModel)
        {
        ViewModel = viewModel;
        DataContext = ViewModel; // Fix: Set DataContext to ViewModel, not 'this'

        InitializeComponent();

        // Set up filtered collection view
        var view = CollectionViewSource.GetDefaultView(ViewModel.LogEntries);
        ViewModel.SetCollectionView(view);

        // Subscribe to collection changes for auto-scroll
        ViewModel.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

        // Subscribe to auto-scroll setting changes
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

    private void ViewModel_PropertyChanged (object? sender, PropertyChangedEventArgs e)
        {
        if (e.PropertyName == nameof(ActivityLogsViewModel.AutoScroll))
            {
            _autoScrollEnabled = ViewModel.AutoScroll;
            }
        }

    private void LogEntries_CollectionChanged (object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
        if (_autoScrollEnabled && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
            // Auto-scroll to bottom when new entries are added
            Dispatcher.BeginInvoke(() =>
            {
                if (LogsListView.Items.Count > 0)
                    {
                    LogsListView.ScrollIntoView(LogsListView.Items[LogsListView.Items.Count - 1]);
                    }
            });
            }
        }
    private void RefreshOptionsButton_Click (object sender, RoutedEventArgs e)
        {
        if (sender is Wpf.Ui.Controls.Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
    }
    private void CopyMessage_Click (object sender, RoutedEventArgs e)
        {
        if (ViewModel.SelectedLogEntry != null)
            {
            Clipboard.SetText(ViewModel.SelectedLogEntry.Message);
            }
        }

    private void CopyFullEntry_Click (object sender, RoutedEventArgs e)
        {
        if (ViewModel.SelectedLogEntry != null)
            {
            Clipboard.SetText(ViewModel.SelectedLogEntry.FullText);
            }
        }

    private void FilterBySession_Click (object sender, RoutedEventArgs e)
        {
        if (ViewModel.SelectedLogEntry != null)
            {
            ViewModel.FilterText = ViewModel.SelectedLogEntry.SessionId;
            }
        }

    private void FilterByScript_Click (object sender, RoutedEventArgs e)
        {
        if (ViewModel.SelectedLogEntry != null)
            {
            ViewModel.FilterText = ViewModel.SelectedLogEntry.ScriptName;
            }
        }
    }