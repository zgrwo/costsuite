using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class OrderRecordRepository : IOrderRecordRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public OrderRecordRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IEnumerable<OrderRecord> GetByMaterialDue(long materialId, DateTime? dueBefore = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Orders WHERE MaterialId = @MaterialId";
            var parameters = new DynamicParameters();
            parameters.Add("MaterialId", materialId);

            if (dueBefore.HasValue)
            {
                sql += " AND DueDate <= @DueBefore";
                parameters.Add("DueBefore", dueBefore.Value.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            sql += " ORDER BY DueDate";
            return conn.Query<OrderRecord>(sql, parameters).ToList();
        }

        public void BulkUpsert(IEnumerable<OrderRecord> records)
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

        public void BulkUpsert(IEnumerable<OrderRecord> records, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
        {
            if (records == null) return;

            const int batchSize = 200;
            var batch = new List<OrderRecord>(batchSize);
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

        private static void ExecuteBatch(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, List<OrderRecord> batch)
        {
            const string prefix = @"INSERT OR REPLACE INTO Orders
                          (Id, OrgId, MaterialId, OrderQty, DueDate, DataVersion, CreatedAt)
                          VALUES ";
            var values = new List<string>(batch.Count);
            var parameters = new DynamicParameters();
            for (int i = 0; i < batch.Count; i++)
            {
                var r = batch[i];
                var idx = i.ToString();
                values.Add($"(@Id{idx}, @OrgId{idx}, @MaterialId{idx}, @OrderQty{idx}, @DueDate{idx}, @DataVersion{idx}, @CreatedAt{idx})");
                parameters.Add($"Id{idx}", r.Id);
                parameters.Add($"OrgId{idx}", r.OrgId);
                parameters.Add($"MaterialId{idx}", r.MaterialId);
                parameters.Add($"OrderQty{idx}", r.OrderQty);
                parameters.Add($"DueDate{idx}", r.DueDate.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                parameters.Add($"DataVersion{idx}", r.DataVersion);
                parameters.Add($"CreatedAt{idx}", r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            conn.Execute(prefix + string.Join(", ", values), parameters, tx);
        }
    }
}
