using System;
using BomAddIn.Core.Services;
using ExcelDna.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UDF.Functions
{
    public static class SystemFunctions
    {
        /// <summary>
        /// =SYNCSTATUS()
        /// 获取当前数据同步状态。
        /// 易变函数: 每次 Excel 重算都会执行。
        /// </summary>
        [ExcelFunction(Name = "SYNCSTATUS", Description = "获取当前数据同步状态",
            IsThreadSafe = false, IsVolatile = true)]
        public static object SyncStatus()
        {
            try
            {
                using var scope = Container.BeginScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
                var lastSync = syncService.GetLastSyncTime();

                if (lastSync == null)
                    return "Never synced";

                var age = DateTime.UtcNow - lastSync.Value;
                if (age.TotalHours < 1)
                    return $"Synced {age.TotalMinutes:F0}m ago";
                if (age.TotalHours < 24)
                    return $"Synced {age.TotalHours:F1}h ago";

                return $"Synced {lastSync:yyyy-MM-dd HH:mm}";
            }
            catch (Exception)
            {
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
