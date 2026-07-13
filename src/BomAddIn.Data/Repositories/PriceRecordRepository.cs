using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class PriceRecordRepository : IPriceRecordRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public PriceRecordRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public PriceRecord? GetLatestByMaterialId(long materialId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<PriceRecord>(
                "SELECT * FROM Prices WHERE MaterialId = @MaterialId ORDER BY EffectiveDate DESC LIMIT 1",
                new { MaterialId = materialId });
        }

        /// <summary>批量获取多个物料的最新单价 — 一次 SQL 替代 N 次查询 (v2 C-18)</summary>
        public Dictionary<long, PriceRecord> GetLatestByMaterialIds(IEnumerable<long> materialIds)
        {
            using var conn = _connectionFactory.CreateConnection();
            // 使用 GROUP BY + MAX 获取每个物料的最新 EffectiveDate，再 JOIN 回 Prices
            var results = conn.Query<PriceRecord>(
                @"SELECT p.* FROM Prices p
                  INNER JOIN (
                      SELECT MaterialId, MAX(EffectiveDate) AS MaxDate
                      FROM Prices
                      WHERE MaterialId IN @Ids
                      GROUP BY MaterialId
                  ) latest ON p.MaterialId = latest.MaterialId AND p.EffectiveDate = latest.MaxDate",
                new { Ids = materialIds });
            // M-8 fix: 同一物料同日期多条记录（多供应商）时避免重复键异常，取第一条
            return results.GroupBy(r => r.MaterialId)
                          .ToDictionary(g => g.Key, g => g.First());
        }

        public PriceRecord? GetByMaterialIdAndDate(long materialId, DateTime asOfDate)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<PriceRecord>(
                @"SELECT * FROM Prices
                  WHERE MaterialId = @MaterialId AND EffectiveDate <= @AsOfDate
                  ORDER BY EffectiveDate DESC LIMIT 1",
                new { MaterialId = materialId, AsOfDate = asOfDate.ToString("yyyy-MM-dd") });
        }

        /// <summary>批量获取多个物料在指定日期的价格 — 一次 SQL 替代 N 次查询 (C-2 fix)</summary>
        public Dictionary<long, PriceRecord> GetByMaterialIdsAndDate(IEnumerable<long> materialIds, DateTime asOfDate)
        {
            using var conn = _connectionFactory.CreateConnection();
            var results = conn.Query<PriceRecord>(
                @"SELECT p.* FROM Prices p
                  INNER JOIN (
                      SELECT MaterialId, MAX(EffectiveDate) AS MaxDate
                      FROM Prices
                      WHERE MaterialId IN @Ids AND EffectiveDate <= @AsOfDate
                      GROUP BY MaterialId
                  ) latest ON p.MaterialId = latest.MaterialId AND p.EffectiveDate = latest.MaxDate",
                new { Ids = materialIds, AsOfDate = asOfDate.ToString("yyyy-MM-dd") });
            // M-8 fix: 同一物料同日期多条记录时避免重复键异常，取第一条
            return results.GroupBy(r => r.MaterialId)
                          .ToDictionary(g => g.Key, g => g.First());
        }

        public IEnumerable<PriceRecord> GetByMaterialVersion(long materialId, long dataVersion)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<PriceRecord>(
                "SELECT * FROM Prices WHERE MaterialId = @MaterialId AND DataVersion = @Version",
                new { MaterialId = materialId, Version = dataVersion });
        }

        public IEnumerable<PriceRecord> GetHistory(long materialId, DateTime from, DateTime to)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<PriceRecord>(
                @"SELECT * FROM Prices
                  WHERE MaterialId = @MaterialId
                    AND EffectiveDate >= @From
                    AND EffectiveDate <= @To
                  ORDER BY EffectiveDate",
                new { MaterialId = materialId, From = from.ToString("o"), To = to.ToString("o") });
        }

        /// <summary>批量获取多个物料的价格历史 — 一次 SQL 替代 N 次查询 (U-2 fix)</summary>
        public IEnumerable<PriceRecord> GetHistoryBatch(IEnumerable<long> materialIds, DateTime from, DateTime to)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<PriceRecord>(
                @"SELECT * FROM Prices
                  WHERE MaterialId IN @Ids
                    AND EffectiveDate >= @From
                    AND EffectiveDate <= @To
                  ORDER BY MaterialId, EffectiveDate",
                new { Ids = materialIds, From = from.ToString("o"), To = to.ToString("o") });
        }

        public void BulkUpsert(IEnumerable<PriceRecord> records)
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

        public void BulkUpsert(IEnumerable<PriceRecord> records, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
        {
            if (records == null) return;

            const int batchSize = 200;
            var batch = new List<PriceRecord>(batchSize);
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

        private static void ExecuteBatch(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, List<PriceRecord> batch)
        {
            const string prefix = @"INSERT OR REPLACE INTO Prices
                          (Id, OrgId, MaterialId, SupplierId, UnitPrice, Currency, DataVersion, EffectiveDate, CreatedAt)
                          VALUES ";
            var values = new List<string>(batch.Count);
            var parameters = new DynamicParameters();
            for (int i = 0; i < batch.Count; i++)
            {
                var r = batch[i];
                var idx = i.ToString();
                values.Add($"(@Id{idx}, @OrgId{idx}, @MaterialId{idx}, @SupplierId{idx}, @UnitPrice{idx}, @Currency{idx}, @DataVersion{idx}, @EffectiveDate{idx}, @CreatedAt{idx})");
                parameters.Add($"Id{idx}", r.Id);
                parameters.Add($"OrgId{idx}", r.OrgId);
                parameters.Add($"MaterialId{idx}", r.MaterialId);
                parameters.Add($"SupplierId{idx}", r.SupplierId);
                parameters.Add($"UnitPrice{idx}", r.UnitPrice);
                parameters.Add($"Currency{idx}", r.Currency);
                parameters.Add($"DataVersion{idx}", r.DataVersion);
                parameters.Add($"EffectiveDate{idx}", r.EffectiveDate.ToString("o"));
                parameters.Add($"CreatedAt{idx}", r.CreatedAt.ToString("o"));
            }
            conn.Execute(prefix + string.Join(", ", values), parameters, tx);
        }
    }
}
