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

namespace BomAddIn.Core.Services
{
    /// <summary>同步服务 — 简单重试 + 并行拉取</summary>
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
        private readonly IBomClosureRepository _closureRepo;

        // 简单重试：指数退避 (2s, 4s, 8s) + 抖动，最多 3 次
        private const int MaxRetries = 3;
        private static readonly ThreadLocal<Random> RetryRng = new(() => new Random());

        private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { return await action().ConfigureAwait(false); }
                catch (InvalidOperationException) { throw; } // 不重试业务逻辑错误
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                              + TimeSpan.FromMilliseconds(RetryRng.Value!.Next(0, 1000));
                    AppLogger.Warn($"重试 {attempt}/{MaxRetries}，等待 {delay.TotalSeconds:F1}s: {ex.Message}", typeof(SyncService));
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        private static async Task ExecuteWithRetryAsync(Func<Task> action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { await action().ConfigureAwait(false); return; }
                catch (InvalidOperationException) { throw; }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                              + TimeSpan.FromMilliseconds(RetryRng.Value!.Next(0, 1000));
                    AppLogger.Warn($"重试 {attempt}/{MaxRetries}，等待 {delay.TotalSeconds:F1}s: {ex.Message}", typeof(SyncService));
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        /// <summary>执行带重试保护的数据库写入事务</summary>
        private Task ExecuteWrappedTransaction(Action transactionBody)
        {
            return ExecuteWithRetryAsync(() => { transactionBody(); return Task.CompletedTask; });
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
            ISyncLogRepository syncLogRepo,
            IBomClosureRepository closureRepo)
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
            _closureRepo = closureRepo;
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
                var isOnline = await _networkMonitor.ProbeConnectionAsync().ConfigureAwait(false);
                if (!isOnline)
                {
                    result.ErrorMessage = "网络不可达，同步跳过。";
                    return result;
                }

                // 2. 记录 SyncLog 开始
                var syncLogId = _syncLogRepo.WriteLog("Full", SyncStatus.Running.ToString(), result.StartedAt.ToString("o"));

                // 3. 并行拉取（指数退避 + 抖动 + 3 次重试）
                //     每个任务独立 try/catch，一个表失败不影响其他表继续
                var since = GetLastSyncTime();
                var materialsTask = ExecuteWithRetryAsync(() => _erpAdapter.PullMaterialsAsync(since));
                var pricesTask = ExecuteWithRetryAsync(() => _erpAdapter.PullPricesAsync(since));
                var inventoriesTask = ExecuteWithRetryAsync(() => _erpAdapter.PullInventoriesAsync(since));
                var ordersTask = ExecuteWithRetryAsync(() => _erpAdapter.PullOrdersAsync(since));
                var capacitiesTask = ExecuteWithRetryAsync(() => _erpAdapter.PullCapacitiesAsync(since));

                // 等待所有并行任务完成。Task.WhenAll 遇首个故障即抛异常，
                // 需用 try-catch 包裹以确保后续 IsFaulted 错误聚合代码可达。
                try { await Task.WhenAll(materialsTask, pricesTask, inventoriesTask, ordersTask, capacitiesTask).ConfigureAwait(false); }
                catch { /* 吞掉异常 — 下方 IsFaulted 检查统一聚合所有错误 */ }

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
                    _syncLogRepo.UpdateLog(syncLogId, SyncStatus.Failed.ToString(), 0);
                    return result;
                }

                // 4. 事务批量写入 — 全部成功或全部回滚 (C-16)
                // D-3 fix: 重试+断路器包裹整个事务，而非内部单块写入，避免重试时重复插入
                var total = 0;
                var materialsList = (await materialsTask).ToList();
                var pricesList = (await pricesTask).ToList();
                var inventoriesList = (await inventoriesTask).ToList();
                var ordersList = (await ordersTask).ToList();
                var capacitiesList = (await capacitiesTask).ToList();

                await ExecuteWrappedTransaction(() =>
                {
                    using var conn = _connectionFactory.CreateConnection();
                    conn.Open();
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        foreach (var m in materialsList)
                        {
                            var existing = _materialRepo.GetByCode(m.OrgId, m.Code, conn, tx);
                            if (existing == null)
                                _materialRepo.Add(m, conn, tx);
                            else
                            {
                                m.Id = existing.Id;
                                _materialRepo.Update(m, conn, tx);
                            }
                        }

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
                }).ConfigureAwait(false);

                total = materialsList.Count
                      + pricesList.Count
                      + inventoriesList.Count
                      + ordersList.Count
                      + capacitiesList.Count;

                // 5. 更新 SyncLog
                result.CompletedAt = DateTime.UtcNow;
                result.TotalRecords = total;
                result.Success = true;

                _syncLogRepo.UpdateLog(syncLogId, SyncStatus.Complete.ToString(), total);

                // R2-14: 同步完成后重建 DuckDB 内存表，确保后续 BOM 展开使用最新数据
                // 先重建 Closure Table（同步可能新增/修改了 BOM 边）
                try { _closureRepo.Rebuild(); }
                catch (Exception ex) { AppLogger.Warn($"Closure Table 重建失败: {ex.Message}", typeof(SyncService)); }

                // 加入重试循环（3 次，间隔 100ms），降低瞬时失败对同步的影响
                const int duckDbMaxRetries = 3;
                const int duckDbRetryDelayMs = 100;
                bool duckDbLoaded = false;

                for (int retry = 0; retry < duckDbMaxRetries; retry++)
                {
                    try
                    {
                        using var sqliteConn = _connectionFactory.CreateConnection();
                        sqliteConn.Open();
                        _analysisProvider.LoadFromSqlite(sqliteConn);
                        duckDbLoaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retry < duckDbMaxRetries - 1)
                        {
                            AppLogger.Warn($"DuckDB 刷新重试 ({retry + 1}/{duckDbMaxRetries}): {ex.Message}", typeof(SyncService));
                            await Task.Delay(duckDbRetryDelayMs).ConfigureAwait(false);
                        }
                        else
                        {
                            AppLogger.Warn($"DuckDB 刷新失败（{duckDbMaxRetries} 次重试后）: {ex.Message}", typeof(SyncService));
                        }
                    }
                }

                if (!duckDbLoaded)
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
            return _syncLogRepo.GetLastSyncCompletedAt();
        }

        // WriteSyncLog 和 UpdateSyncLog 已委托给 ISyncLogRepository (code-review C-4)

    }
}
