using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Data.Connection;
using BomAddIn.Infrastructure.Models;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    /// <summary>同步日志仓库 — 管理 SyncLogs 表</summary>
    public class SyncLogRepository : ISyncLogRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SyncLogRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public long WriteLog(string syncType, string status, string startedAt)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<long>(
                @"INSERT INTO SyncLogs (SyncType, Status, StartedAt, RecordsProcessed)
                  VALUES (@Type, @Status, @StartedAt, 0);
                  SELECT last_insert_rowid();",
                new { Type = syncType, Status = status, StartedAt = startedAt });
        }

        public void UpdateLog(long id, string status, int recordsProcessed, string? completedAt = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE SyncLogs SET Status=@Status, RecordsProcessed=@Processed, CompletedAt=@CompletedAt
                  WHERE Id=@Id",
                new
                {
                    Id = id,
                    Status = status,
                    Processed = recordsProcessed,
                    CompletedAt = completedAt ?? DateTime.UtcNow.ToString("o")
                });
        }

        public DateTime? GetLastSyncCompletedAt()
        {
            using var conn = _connectionFactory.CreateConnection();
            var value = conn.QueryFirstOrDefault<string>(
                "SELECT CompletedAt FROM SyncLogs WHERE Status = 'Complete' ORDER BY CompletedAt DESC LIMIT 1");
            return value != null ? DateTime.Parse(value) : (DateTime?)null;
        }

        public IEnumerable<SyncLog> GetRecent(int limit = 10)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<SyncLog>(
                "SELECT * FROM SyncLogs ORDER BY StartedAt DESC LIMIT @Limit",
                new { Limit = limit });
        }
    }
}
