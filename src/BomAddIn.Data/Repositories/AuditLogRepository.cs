using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AuditLogRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Add(AuditLog log)
        {
            using var conn = _connectionFactory.CreateConnection();
            Add(log, conn, null);
        }

        public void Add(AuditLog log, System.Data.IDbConnection conn, System.Data.IDbTransaction? tx)
        {
            log.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO AuditLogs (UserId, Action, TableName, RecordId, OldValues, NewValues, Timestamp)
                  VALUES (@UserId, @Action, @TableName, @RecordId, @OldValues, @NewValues, @Timestamp);
                  SELECT last_insert_rowid();",
                new
                {
                    log.UserId,
                    Action = log.Action.ToString(),
                    log.TableName,
                    log.RecordId,
                    log.OldValues,
                    log.NewValues,
                    Timestamp = log.Timestamp.ToString("o")
                }, tx);
        }

        public IEnumerable<AuditLog> GetByTable(string tableName, DateTime since, int limit = 50)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<AuditLog>(
                @"SELECT * FROM AuditLogs
                  WHERE TableName = @TableName AND Timestamp >= @Since
                  ORDER BY Timestamp DESC
                  LIMIT @Limit",
                new { TableName = tableName, Since = since.ToString("o"), Limit = limit }).ToList();
        }

        public IEnumerable<AuditLog> GetByTableAndRecordId(string tableName, long recordId, int limit = 50)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<AuditLog>(
                @"SELECT * FROM AuditLogs
                  WHERE TableName = @TableName AND RecordId = @RecordId
                  ORDER BY Timestamp DESC
                  LIMIT @Limit",
                new { TableName = tableName, RecordId = recordId, Limit = limit }).ToList();
        }

        public IEnumerable<AuditLog> GetByUser(long userId, int limit = 100)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<AuditLog>(
                @"SELECT * FROM AuditLogs
                  WHERE UserId = @UserId
                  ORDER BY Timestamp DESC
                  LIMIT @Limit",
                new { UserId = userId, Limit = limit }).ToList();
        }

        public IEnumerable<AuditLog> GetRecent(int limit = 50)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<AuditLog>(
                "SELECT * FROM AuditLogs ORDER BY Timestamp DESC LIMIT @Limit",
                new { Limit = limit }).ToList();
        }
    }
}
