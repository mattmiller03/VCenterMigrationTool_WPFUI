using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services
{
    public interface IValidationService
    {
        Task<ValidationResult> ValidateVMBackupAsync(VmBackupConfiguration config);
        Task<ValidationResult> ValidateNetworkMigrationAsync(NetworkMigrationConfiguration config);
        Task<ValidationResult> ValidateHostMigrationAsync(HostMigrationConfiguration config);
    }
}
