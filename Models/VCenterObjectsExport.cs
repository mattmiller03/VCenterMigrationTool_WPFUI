using System;
using System.Collections.Generic;

namespace VCenterMigrationTool.Models
{
    public class VCenterObjectsExport
    {
        public DateTime ExportDate { get; set; }
        public string SourceCluster { get; set; } = "";
        public string TargetCluster { get; set; } = "";
        public MigrationOptionsData MigrationOptions { get; set; } = new();
        public List<ClusterItem> ClusterItems { get; set; } = new();
    }

    public class MigrationOptionsData
    {
        public bool MigrateRoles { get; set; }
        public bool MigrateFolders { get; set; }
        public bool MigrateTags { get; set; }
        public bool MigratePermissions { get; set; }
        public bool MigrateResourcePools { get; set; }
        public bool MigrateCustomAttributes { get; set; }
    }
}