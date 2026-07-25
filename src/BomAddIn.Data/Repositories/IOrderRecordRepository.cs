using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IOrderRecordRepository
    {
        IEnumerable<OrderRecord> GetByMaterialDue(long materialId, DateTime? dueBefore = null);
        void BulkUpsert(IEnumerable<OrderRecord> records);
        void BulkUpsert(IEnumerable<OrderRecord> records, IDbConnection conn, IDbTransaction tx);
    }
}
