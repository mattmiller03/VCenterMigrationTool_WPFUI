using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.Generic;
using System.Linq;
using System.Windows;

using VCenterMigrationTool.Services;

using Wpf.Ui.Controls;

namespace VCenterMigrationTool.ViewModels.Dialogs
{
    public partial class ErrorDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private string _message = "";

        [ObservableProperty]
        private string _details = "";

        [ObservableProperty]
        private List<string> _suggestedActions = new();

        [ObservableProperty]
        private bool _canRetry;

        [ObservableProperty]
        private ErrorSeverity _severity;

        public bool HasSuggestedActions => SuggestedActions?.Any() == true;

        public SymbolRegular SeverityIcon => Severity switch
        {
            ErrorSeverity.Critical => SymbolRegular.ErrorCircle24,
            ErrorSeverity.Error => SymbolRegular.Warning24,
            ErrorSeverity.Warning => SymbolRegular.Info24,
            _ => SymbolRegular.Info24
        };

        public string SeverityColor => Severity switch
        {
            ErrorSeverity.Critical => "#FF3838",
            ErrorSeverity.Error => "#FF6B6B",
            ErrorSeverity.Warning => "#FFB366",
            _ => "#339AF0"
        };

        public bool DialogResult { get; private set; }

        [RelayCommand]
        private void Close()
        {
            DialogResult = false;
            if (Application.Current.MainWindow is Window window)
            {
                var dialog = window.OwnedWindows.OfType<Views.Dialogs.ErrorDialog>().FirstOrDefault();
                dialog?.Close();
            }
        }

        [RelayCommand]
        private void Retry()
        {
            DialogResult = true;
            if (Application.Current.MainWindow is Window window)
            {
                var dialog = window.OwnedWindows.OfType<Views.Dialogs.ErrorDialog>().FirstOrDefault();
                dialog?.Close();
            }
        }
    }
}