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
                    return "从未同步";

                var age = DateTime.UtcNow - lastSync.Value;
                if (age.TotalHours < 1)
                    return $"{age.TotalMinutes:F0} 分钟前同步";
                if (age.TotalHours < 24)
                    return $"{age.TotalHours:F1} 小时前同步";

                return $"上次同步: {lastSync:yyyy-MM-dd HH:mm}";
            }
            catch (Exception ex)
            {
                BomAddIn.Infrastructure.Logging.AppLogger.Warn($"SYNCSTATUS 查询失败: {ex.Message}", typeof(SystemFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
