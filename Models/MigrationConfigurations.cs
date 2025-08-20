using System.Collections.Generic;

namespace VCenterMigrationTool.Services
{
    public class VmBackupConfiguration
    {
        public List<string> VmNames { get; set; } = new();
        public string BackupPath { get; set; } = "";
        public bool IncludeSnapshots { get; set; }
        public bool CompressBackup { get; set; }
    }

    public class NetworkMigrationConfiguration
    {
        public string SourceHost { get; set; } = "";
        public string TargetHost { get; set; } = "";
        public Dictionary<string, string> NetworkMappings { get; set; } = new();
        public bool MigrateVSwitches { get; set; }
        public bool MigratePortGroups { get; set; }
    }

    public class HostMigrationConfiguration
    {
        public string HostName { get; set; } = "";
        public string SourceCluster { get; set; } = "";
        public string TargetCluster { get; set; } = "";
        public bool EnterMaintenanceMode { get; set; }
        public bool MigrateVMs { get; set; }
    }
}