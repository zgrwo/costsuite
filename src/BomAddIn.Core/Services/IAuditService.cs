using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>审计日志服务 — 记录所有增删改操作</summary>
    public interface IAuditService
    {
        /// <summary>记录一条操作审计</summary>
        void Log(
            AuditAction action,
            string tableName,
            long? recordId = null,
            string? oldValues = null,
            string? newValues = null,
            long? userId = null);

        /// <summary>记录一条操作审计（使用共享连接+事务保证原子性）</summary>
        void Log(
            AuditAction action,
            string tableName,
            IDbConnection conn, IDbTransaction tx,
            long? recordId = null,
            string? oldValues = null,
            string? newValues = null,
            long? userId = null);

        /// <summary>获取某个表的变更历史</summary>
        IEnumerable<Infrastructure.Models.AuditLog> GetTableHistory(string tableName, int limit = 50);

        /// <summary>获取某个表特定记录的变更历史</summary>
        IEnumerable<Infrastructure.Models.AuditLog> GetTableHistory(string tableName, long recordId);

        /// <summary>获取某个用户的操作记录</summary>
        IEnumerable<Infrastructure.Models.AuditLog> GetUserHistory(long userId, int limit = 100);
    }
}
