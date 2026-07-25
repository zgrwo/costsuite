using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models.Enums;

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
            AuditAction action,
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
            AuditAction action,
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
            AuditAction action,
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
            if (tableName == null) throw new ArgumentNullException(nameof(tableName));
            return _repo.GetByTable(tableName, DateTime.UtcNow.AddDays(-365), limit);
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetTableHistory(string tableName, long recordId)
        {
            if (tableName == null) throw new ArgumentNullException(nameof(tableName));
            return _repo.GetByTableAndRecordId(tableName, recordId);
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetUserHistory(long userId, int limit = 100)
        {
            return _repo.GetByUser(userId, limit);
        }

        /// <summary>
        /// 将对象序列化为简单 JSON 字符串（无外部依赖）。
        /// 支持匿名类型和 POCO 的属性序列化。
        /// 最大递归深度 5 层，防止循环引用导致栈溢出。
        /// </summary>
        public static string ToJson(object? obj)
        {
            return ToJsonInternal(obj, depth: 0, visited: null);
        }

        private static string ToJsonInternal(object? obj, int depth, HashSet<object>? visited)
        {
            // C-6 fix: 递归深度守卫，防止深层嵌套/循环引用导致 StackOverflow
            const int maxDepth = 5;
            if (depth > maxDepth)
                return "\"<max depth exceeded>\"";

            if (obj == null) return "null";

            var type = obj.GetType();
            if (type.IsValueType || obj is string)
                return Convert.ToString(obj, CultureInfo.InvariantCulture) ?? "null";

            // C-6 fix: 循环引用检测 — 已访问对象返回占位符
            visited ??= new HashSet<object>();
            if (!visited.Add(obj))
                return "\"<circular reference>\"";

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
                    sb.Append('"').Append(EscapeJsonString(s)).Append('"');
                else if (value is DateTime dt)
                    sb.Append('"').Append(dt.ToString("o")).Append('"');
                else if (value.GetType().IsEnum)
                    sb.Append('"').Append(value.ToString()).Append('"');
                else if (value is bool b)
                    sb.Append(b ? "true" : "false");
                else if (value is decimal or double or float or int or long or short or byte)
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null");
                else
                    // 复杂对象：递归序列化（带深度守卫）
                    sb.Append(ToJsonInternal(value, depth + 1, visited));
                first = false;
            }
            sb.Append('}');

            // 离开当前对象时从 visited 中移除（允许同一对象在不同路径中出现）
            visited.Remove(obj);
            return sb.ToString();
        }

        /// <summary>
        /// JSON 字符串转义 — 处理控制字符和特殊字符。
        /// D-1 fix: 统一 JSON 转义逻辑（之前与 SnapshotService.Escape 重复实现且功能不一致）。
        /// </summary>
        internal static string EscapeJsonString(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
