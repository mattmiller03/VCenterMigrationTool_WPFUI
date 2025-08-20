using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VCenterMigrationTool.Services
{
    public interface IProgressReportingService
    {
        Task<T> ExecuteWithProgressAsync<T>(string operationName, Func<IProgress<ProgressInfo>, Task<T>> operation);
        void ShowToastNotification(string title, string message, NotificationType type);
    }
}
