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
        /// <summary>获取指定物料+仓库的最新库存记录（SQL 层过滤，消除全量拉取）</summary>
        InventoryRecord? GetLatestByMaterialAndWarehouse(long materialId, string warehouseId);
        void BulkUpsert(IEnumerable<InventoryRecord> records);
        void BulkUpsert(IEnumerable<InventoryRecord> records, IDbConnection conn, IDbTransaction tx);
    }
}
