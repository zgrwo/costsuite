using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    /// <summary>同步日志仓库 — 管理 SyncLogs 表的读写</summary>
    public interface ISyncLogRepository
    {
        /// <summary>写入同步日志并返回 ID</summary>
        long WriteLog(string syncType, string status, string startedAt);

        /// <summary>更新同步日志状态</summary>
        void UpdateLog(long id, string status, int recordsProcessed, string? completedAt = null);

        /// <summary>获取最近一次成功同步的时间</summary>
        DateTime? GetLastSyncCompletedAt();

        /// <summary>获取最近的同步日志</summary>
        IEnumerable<SyncLog> GetRecent(int limit = 10);
    }
}
