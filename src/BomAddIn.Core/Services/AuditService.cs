using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using BomAddIn.Data.Repositories;

namespace BomAddIn.Core.Services
{
    /// <summary>审计日志服务 — 记录增删改操作到 AuditLogs 表</summary>
    public class AuditService : IAuditService
    {
        private readonly IAuditLogRepository _repo;

        public AuditService(IAuditLogRepository repo)
        {
            _repo = repo;
        }

        public void Log(
            string action,
            string tableName,
            long? recordId = null,
            string? oldValues = null,
            string? newValues = null,
            long? userId = null)
        {
            var log = BuildLog(action, tableName, recordId, oldValues, newValues, userId);
            _repo.Add(log);
        }

        public void Log(
            string action,
            string tableName,
            IDbConnection conn, IDbTransaction tx,
            long? recordId = null,
            string? oldValues = null,
            string? newValues = null,
            long? userId = null)
        {
            var log = BuildLog(action, tableName, recordId, oldValues, newValues, userId);
            _repo.Add(log, conn, tx);
        }

        private static Infrastructure.Models.AuditLog BuildLog(
            string action,
            string tableName,
            long? recordId,
            string? oldValues,
            string? newValues,
            long? userId)
        {
            return new Infrastructure.Models.AuditLog
            {
                UserId = userId,
                Action = action,
                TableName = tableName,
                RecordId = recordId,
                OldValues = oldValues,
                NewValues = newValues,
                Timestamp = DateTime.UtcNow
            };
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetTableHistory(string tableName, int limit = 50)
        {
            return _repo.GetByTable(tableName, DateTime.UtcNow.AddDays(-365), limit);
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetTableHistory(string tableName, long recordId)
        {
            return _repo.GetByTableAndRecordId(tableName, recordId);
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetUserHistory(long userId, int limit = 100)
        {
            return _repo.GetByUser(userId, limit);
        }

        /// <summary>
        /// 将对象序列化为简单 JSON 字符串（无外部依赖）。
        /// 支持匿名类型和 POCO 的属性序列化。
        /// </summary>
        public static string ToJson(object? obj)
        {
            if (obj == null) return "null";

            var type = obj.GetType();
            if (type.IsValueType || obj is string)
                return Convert.ToString(obj, CultureInfo.InvariantCulture) ?? "null";

            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var prop in type.GetProperties())
            {
                if (!prop.CanRead) continue;
                if (!first) sb.Append(',');
                var value = prop.GetValue(obj);
                sb.Append('"');
                sb.Append(prop.Name);
                sb.Append("\":");
                if (value == null)
                    sb.Append("null");
                else if (value is string s)
                    sb.Append('"').Append(
                        s.Replace("\\", "\\\\")
                         .Replace("\"", "\\\"")
                         .Replace("\n", "\\n")
                         .Replace("\r", "\\r")
                         .Replace("\t", "\\t"))
                        .Append('"');
                else if (value is DateTime dt)
                    sb.Append('"').Append(dt.ToString("o")).Append('"');
                else if (value.GetType().IsEnum)
                    sb.Append('"').Append(value.ToString()).Append('"');
                else
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null");
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
