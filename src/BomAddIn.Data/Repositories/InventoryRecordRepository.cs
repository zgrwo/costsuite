using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class InventoryRecordRepository : IInventoryRecordRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public InventoryRecordRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IEnumerable<InventoryRecord> GetByMaterialWarehouse(long materialId, string warehouseId, long dataVersion)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<InventoryRecord>(
                @"SELECT * FROM Inventories
                  WHERE MaterialId = @MaterialId AND WarehouseId = @WarehouseId AND DataVersion = @Version",
                new { MaterialId = materialId, WarehouseId = warehouseId, Version = dataVersion }).ToList();
        }

        public IEnumerable<InventoryRecord> GetSnapshot(long materialId, DateTime date)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<InventoryRecord>(
                @"SELECT * FROM Inventories
                  WHERE MaterialId = @MaterialId AND SnapshotDate <= @Date
                  ORDER BY SnapshotDate DESC",
                new { MaterialId = materialId, Date = date.ToString("o") });
        }

        public InventoryRecord? GetLatestByMaterialAndWarehouse(long materialId, string warehouseId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<InventoryRecord>(
                @"SELECT * FROM Inventories
                  WHERE MaterialId = @MaterialId AND WarehouseId = @WarehouseId
                  ORDER BY SnapshotDate DESC
                  LIMIT 1",
                new { MaterialId = materialId, WarehouseId = warehouseId });
        }

        public void BulkUpsert(IEnumerable<InventoryRecord> records)
        {
            using var conn = _connectionFactory.CreateConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                BulkUpsert(records, conn, tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void BulkUpsert(IEnumerable<InventoryRecord> records, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
        {
            if (records == null) return;

            const int batchSize = 200;
            var batch = new List<InventoryRecord>(batchSize);
            foreach (var r in records)
            {
                batch.Add(r);
                if (batch.Count >= batchSize)
                {
                    ExecuteBatch(conn, tx, batch);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                ExecuteBatch(conn, tx, batch);
        }

        private static void ExecuteBatch(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, List<InventoryRecord> batch)
        {
            const string prefix = @"INSERT OR REPLACE INTO Inventories
                          (Id, OrgId, MaterialId, WarehouseId, Quantity, DataVersion, SnapshotDate, CreatedAt)
                          VALUES ";
            var values = new List<string>(batch.Count);
            var parameters = new DynamicParameters();
            for (int i = 0; i < batch.Count; i++)
            {
                var r = batch[i];
                var idx = i.ToString();
                values.Add($"(@Id{idx}, @OrgId{idx}, @MaterialId{idx}, @WarehouseId{idx}, @Quantity{idx}, @DataVersion{idx}, @SnapshotDate{idx}, @CreatedAt{idx})");
                parameters.Add($"Id{idx}", r.Id);
                parameters.Add($"OrgId{idx}", r.OrgId);
                parameters.Add($"MaterialId{idx}", r.MaterialId);
                parameters.Add($"WarehouseId{idx}", r.WarehouseId);
                parameters.Add($"Quantity{idx}", r.Quantity);
                parameters.Add($"DataVersion{idx}", r.DataVersion);
                parameters.Add($"SnapshotDate{idx}", r.SnapshotDate.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                parameters.Add($"CreatedAt{idx}", r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            conn.Execute(prefix + string.Join(", ", values), parameters, tx);
        }
    }
}
