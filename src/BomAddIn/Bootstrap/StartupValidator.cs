using System;
using BomAddIn.Bridge;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn
{
    /// <summary>
    /// 启动自检 — 在 AutoOpen 中执行，快速失败。
    /// 检查项: Excel 主线程确认、版本适配器可用性、预警阈值配置。
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
            AppLogger.Info($"Excel 数组公式行为: {behavior}", typeof(StartupValidator));

            // G-3 fix: 启动时验证 AlertEvaluator 阈值配置，配置错误快速失败
            // AlertEvaluator 构造函数验证阈值单调性 (Critical > Severe > Warning > 0)
            // 在此处强制解析可确保配置错误在插件加载时暴露，而非用户首次触发分析时
            try
            {
                services.GetRequiredService<IAlertEvaluator>();
                AppLogger.Info("AlertEvaluator 阈值配置验证通过。", typeof(StartupValidator));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("阈值"))
            {
                AppLogger.Error($"AlertEvaluator 阈值配置无效，预警功能将不可用: {ex.Message}",
                    ex, typeof(StartupValidator));
                // 不阻塞启动——预警功能降级，其他功能正常
            }
        }
    }
}
