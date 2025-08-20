using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services
{
    public interface IConnectionHealthService
    {
        event EventHandler<ConnectionHealthChangedEventArgs> HealthChanged;
        Task StartMonitoringAsync();
        Task<bool> AttemptAutoReconnectionAsync(string connectionKey);
    }
}
