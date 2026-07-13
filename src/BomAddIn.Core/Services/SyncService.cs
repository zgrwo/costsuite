using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Infrastructure.Network;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Data.Repositories;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Sync;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace BomAddIn.Core.Services
{
    /// <summary>同步服务 — Polly 重试 + 熔断 + 并行拉取</summary>
    public class SyncService : ISyncService
    {
        private readonly IErpAdapter _erpAdapter;
        private readonly INetworkMonitor _networkMonitor;
        private readonly IMaterialRepository _materialRepo;
        private readonly IPriceRecordRepository _priceRepo;
        private readonly IInventoryRecordRepository _inventoryRepo;
        private readonly IOrderRecordRepository _orderRepo;
        private readonly ICapacityRecordRepository _capacityRepo;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;
        private readonly IBomAnalysisProvider _analysisProvider;
        private readonly ISyncLogRepository _syncLogRepo;

        // Polly 重试策略：指数退避 (2s, 4s, 8s) + 抖动
        // 使用 ThreadLocal<Random> 确保多线程安全 (code-review TH-note)
        private static readonly ThreadLocal<Random> RetryRng = new(() => new Random());
        private static readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<Exception>(ex => !(ex is InvalidOperationException)) // 不重试业务逻辑错误
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    + TimeSpan.FromMilliseconds(RetryRng.Value!.Next(0, 1000)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    AppLogger.Warn($"Polly retry {retryCount} after {timeSpan.TotalSeconds:F1}s: {exception.Message}", typeof(SyncService));
                });

        // C-4: Polly 断路器 — 5 次异常后熔断 30 秒 (spec §9.2)
        private static readonly AsyncCircuitBreakerPolicy CircuitBreaker = Policy
            .Handle<Exception>(ex => !(ex is InvalidOperationException))
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, ts) =>
                {
                    AppLogger.Warn($"断路器熔断 — {ts.TotalSeconds:F0}s 内拒绝请求。原因: {ex.Message}", typeof(SyncService));
                },
                onReset: () =>
                {
                    AppLogger.Info("断路器已重置 — 恢复请求。", typeof(SyncService));
                });

        /// <summary>执行带断路器和重试保护的数据库写入事务</summary>
        private async Task ExecuteWrappedTransaction(Action transactionBody)
        {
            await CircuitBreaker.ExecuteAsync(
                () => RetryPolicy.ExecuteAsync(
                    () => Task.Run(transactionBody)));
        }

        public SyncService(
            IErpAdapter erpAdapter,
            INetworkMonitor networkMonitor,
            IMaterialRepository materialRepo,
            IPriceRecordRepository priceRepo,
            IInventoryRecordRepository inventoryRepo,
            IOrderRecordRepository orderRepo,
            ICapacityRecordRepository capacityRepo,
            IDbConnectionFactory connectionFactory,
            IAuthorizationService authz,
            IBomAnalysisProvider analysisProvider,
            ISyncLogRepository syncLogRepo)
        {
            _erpAdapter = erpAdapter;
            _networkMonitor = networkMonitor;
            _materialRepo = materialRepo;
            _priceRepo = priceRepo;
            _inventoryRepo = inventoryRepo;
            _orderRepo = orderRepo;
            _capacityRepo = capacityRepo;
            _connectionFactory = connectionFactory;
            _authz = authz;
            _analysisProvider = analysisProvider;
            _syncLogRepo = syncLogRepo;
        }

        // C-11: 移除无参重载 SyncAllAsync()，强制调用者显式提供 UserRole。
        // RBAC 检查在 SyncAllAsync(UserRole) 中通过 Demand 完成，确保校验的是当前用户而非硬编码 Admin。

        public async Task<SyncResult> SyncAllAsync(UserRole callerRole)
        {
            _authz.Demand(callerRole, BomOperation.SyncTrigger);

            var result = new SyncResult
            {
                StartedAt = DateTime.UtcNow
            };

            try
            {
                // 1. 连接检测
                var isOnline = await _networkMonitor.ProbeConnectionAsync();
                if (!isOnline)
                {
                    result.ErrorMessage = "网络不可达，同步跳过。";
                    return result;
                }

                // 2. 记录 SyncLog 开始
                var syncLogId = _syncLogRepo.WriteLog("Full", "Running", result.StartedAt.ToString("o"));

                // 3. 并行拉取（Polly 指数退避 + 抖动 + 3 次重试）
                //     每个任务独立 try/catch，一个表失败不影响其他表继续
                var since = GetLastSyncTime();
                var materialsTask = RetryPolicy.ExecuteAsync(() => _erpAdapter.PullMaterialsAsync(since));
                var pricesTask = RetryPolicy.ExecuteAsync(() => _erpAdapter.PullPricesAsync(since));
                var inventoriesTask = RetryPolicy.ExecuteAsync(() => _erpAdapter.PullInventoriesAsync(since));
                var ordersTask = RetryPolicy.ExecuteAsync(() => _erpAdapter.PullOrdersAsync(since));
                var capacitiesTask = RetryPolicy.ExecuteAsync(() => _erpAdapter.PullCapacitiesAsync(since));

                // B-3 fix: 用 try-catch 包裹 WhenAll，使 IsFaulted 检查可达
                try
                {
                    await Task.WhenAll(materialsTask, pricesTask, inventoriesTask, ordersTask, capacitiesTask);
                }
                catch (AggregateException)
                {
                    // 异常由下方 IsFaulted 检查处理——不在此处提前返回
                }

                // C-16: 检查是否有任何任务失败 — 如果有，全部回滚，不写入任何数据
                var errors = new List<string>();

                if (materialsTask.IsFaulted)
                    errors.Add($"Materials: {materialsTask.Exception?.InnerException?.Message}");
                if (pricesTask.IsFaulted)
                    errors.Add($"Prices: {pricesTask.Exception?.InnerException?.Message}");
                if (inventoriesTask.IsFaulted)
                    errors.Add($"Inventories: {inventoriesTask.Exception?.InnerException?.Message}");
                if (ordersTask.IsFaulted)
                    errors.Add($"Orders: {ordersTask.Exception?.InnerException?.Message}");
                if (capacitiesTask.IsFaulted)
                    errors.Add($"Capacities: {capacitiesTask.Exception?.InnerException?.Message}");

                if (errors.Count > 0)
                {
                    result.CompletedAt = DateTime.UtcNow;
                    result.ErrorMessage = $"同步失败 ({errors.Count}/5): " + string.Join("; ", errors);
                    _syncLogRepo.UpdateLog(syncLogId, "Failed", 0);
                    return result;
                }

                // 4. 事务批量写入 — 全部成功或全部回滚 (C-16)
                // D-3 fix: 重试+断路器包裹整个事务，而非内部单块写入，避免重试时重复插入
                var total = 0;
                var materialsList = materialsTask.Result.ToList();
                var pricesList = pricesTask.Result.ToList();
                var inventoriesList = inventoriesTask.Result.ToList();
                var ordersList = ordersTask.Result.ToList();
                var capacitiesList = capacitiesTask.Result.ToList();

                await ExecuteWrappedTransaction(() =>
                {
                    using var conn = _connectionFactory.CreateConnection();
                    conn.Open();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        foreach (var m in materialsList)
                            _materialRepo.Add(m, conn, tx);

                        _priceRepo.BulkUpsert(pricesList, conn, tx);
                        _inventoryRepo.BulkUpsert(inventoriesList, conn, tx);
                        _orderRepo.BulkUpsert(ordersList, conn, tx);
                        _capacityRepo.BulkUpsert(capacitiesList, conn, tx);

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                });

                total = materialsList.Count
                      + pricesList.Count
                      + inventoriesList.Count
                      + ordersList.Count
                      + capacitiesList.Count;

                // 5. 更新 SyncLog
                result.CompletedAt = DateTime.UtcNow;
                result.TotalRecords = total;
                result.Success = true;

                _syncLogRepo.UpdateLog(syncLogId, "Complete", total);

                // R2-14: 同步完成后重建 DuckDB 内存表，确保后续 BOM 展开使用最新数据
                try
                {
                    using var sqliteConn = _connectionFactory.CreateConnection();
                    sqliteConn.Open();
                    _analysisProvider.LoadFromSqlite(sqliteConn);
                }
                catch
                {
                    // DuckDB 刷新失败不阻碍同步结果
                    AppLogger.Warn("DuckDB 刷新失败，将在下次 BOM 查询时触发懒加载。", typeof(SyncService));
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                result.CompletedAt = DateTime.UtcNow;
            }

            return result;
        }

        public DateTime? GetLastSyncTime()
        {
            var lastSync = _syncLogRepo.GetLastSyncCompletedAt();
            return lastSync != null ? DateTime.Parse(lastSync) : (DateTime?)null;
        }

        // WriteSyncLog 和 UpdateSyncLog 已委托给 ISyncLogRepository (code-review C-4)

    }
}
