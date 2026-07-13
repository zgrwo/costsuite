using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IInventoryRecordRepository
    {
        IEnumerable<InventoryRecord> GetByMaterialWarehouse(long materialId, string warehouseId, long dataVersion);
        IEnumerable<InventoryRecord> GetSnapshot(long materialId, DateTime date);
        void BulkUpsert(IEnumerable<InventoryRecord> records);
        void BulkUpsert(IEnumerable<InventoryRecord> records, IDbConnection conn, IDbTransaction tx);
    }
}
