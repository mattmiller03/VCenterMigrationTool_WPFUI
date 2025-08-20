namespace VCenterMigrationTool.Services
{
    public class ProgressInfo
    {
        public int Percentage { get; set; }
        public string Message { get; set; } = "";
        public string CurrentOperation { get; set; } = "";
        public int CurrentItem { get; set; }
        public int TotalItems { get; set; }
    }

    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }
}