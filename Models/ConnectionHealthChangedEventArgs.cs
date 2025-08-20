using System;

namespace VCenterMigrationTool.Services
{
    public class ConnectionHealthChangedEventArgs : EventArgs
    {
        public string ConnectionKey { get; set; } = "";
        public bool IsHealthy { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}