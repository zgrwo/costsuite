using System;
using BomAddIn.Bridge;
using BomAddIn.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn
{
    /// <summary>
    /// 启动自检 — 在 AutoOpen 中执行，快速失败。
    /// 检查项: Excel 主线程确认、版本适配器可用性。
    /// Sprint 1+ 补充: 数据库连接、SQLite 缓存权限、网络连通性。
    /// </summary>
    public static class StartupValidator
    {
        public static void Validate(IServiceProvider services)
        {
            // 探针 P-0.1: 确认 Excel 主线程已正确捕获
            var dispatcher = services.GetRequiredService<IExcelThreadDispatcher>();
            if (!dispatcher.IsExcelMainThread)
            {
                throw new InvalidOperationException(
                    "ExcelThreadDispatcher: 当前线程不是 Excel 主线程。" +
                    "请确认 ExcelThreadDispatcher.Initialize() 已在 AutoOpen 中调用。");
            }

            // 探针 P-0.2: 确认版本适配器可用
            var versionAdapter = services.GetRequiredService<IVersionAdapter>();
            var behavior = versionAdapter.GetArrayFormulaBehavior();
            // 仅记录，不阻塞启动
            AppLogger.Info($"Excel 数组公式行为: {behavior}", typeof(StartupValidator));
        }
    }
}
