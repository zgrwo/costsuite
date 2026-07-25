using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IAuditLogRepository
    {
        void Add(AuditLog log);
        void Add(AuditLog log, IDbConnection conn, IDbTransaction? tx);
        IEnumerable<AuditLog> GetByTable(string tableName, DateTime since, int limit = 50);
        IEnumerable<AuditLog> GetByTableAndRecordId(string tableName, long recordId, int limit = 50);
        IEnumerable<AuditLog> GetByUser(long userId, int limit = 100);
        IEnumerable<AuditLog> GetRecent(int limit = 50);
    }
}
