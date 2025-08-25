using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;

namespace VCenterMigrationTool.ViewModels.Base
{
    /// <summary>
    /// Base class providing dashboard-style activity log functionality
    /// </summary>
    public abstract partial class ActivityLogViewModelBase : ObservableObject
    {
        // Activity log properties - dashboard style
        [ObservableProperty]
        private string _activityLog = string.Empty;

        [ObservableProperty]
        private bool _isAutoScrollEnabled = true;

        /// <summary>
        /// Initialize activity log with module-specific header
        /// </summary>
        protected void InitializeActivityLog(string moduleName)
        {
            ActivityLog = $"vCenter Migration Tool - {moduleName}\n" +
                         new string('=', Math.Max(40, moduleName.Length + 30)) + "\n" +
                         "[INFO] Module loaded\n" +
                         "[INFO] Ready for operations...\n";
        }

        /// <summary>
        /// Clear Log Command - Dashboard Style
        /// </summary>
        [RelayCommand]
        private void ClearLog()
        {
            var headerLines = ActivityLog.Split('\n').Take(4).ToArray();
            ActivityLog = string.Join("\n", headerLines) + 
                         "[INFO] Log cleared\n";
            LogMessage("Log cleared by user", "INFO");
        }

        /// <summary>
        /// Toggle Auto Scroll Command - Dashboard Style
        /// </summary>
        [RelayCommand]
        private void ToggleAutoScroll()
        {
            IsAutoScrollEnabled = !IsAutoScrollEnabled;
            LogMessage($"Auto scroll {(IsAutoScrollEnabled ? "enabled" : "disabled")}", "INFO");
        }

        /// <summary>
        /// Add a timestamped message to the activity log - Dashboard Style
        /// </summary>
        protected void LogMessage(string message, string level = "INFO")
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] [{level}] {message}\n";
            ActivityLog += logEntry;
            
            // If we have many lines, trim to keep performance good
            var lines = ActivityLog.Split('\n');
            if (lines.Length > 1000)
            {
                var keepLines = lines.Skip(lines.Length - 800).ToArray();
                ActivityLog = string.Join("\n", keepLines);
            }
        }
    }
}