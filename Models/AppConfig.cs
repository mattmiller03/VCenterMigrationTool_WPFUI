using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Models
{
    public class AppConfig
    {
        public string? ApplicationTitle { get; set; }
        public string? ConfigurationsFolder { get; set; }

        public string? AppPropertiesFileName { get; set; }

        public string? AppVersion { get; set; }
        public string? AppName { get; set; }
        public string? AppDescription { get; set; }
        public string? AppAuthor { get; set; }
        public string? AppLicense { get; set; }
        public string? AppLicenseUrl { get; set; }
        public string? AppRepositoryUrl { get; set; }
        public string? AppWebsiteUrl { get; set; }
        public string? AppSupportUrl { get; set; }

    }
}
