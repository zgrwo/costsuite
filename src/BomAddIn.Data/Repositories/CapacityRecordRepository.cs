using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class CapacityRecordRepository : ICapacityRecordRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public CapacityRecordRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IEnumerable<CapacityRecord> GetByWorkCenter(string workCenterId, long dataVersion)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<CapacityRecord>(
                "SELECT * FROM Capacities WHERE WorkCenterId = @WorkCenterId AND DataVersion = @Version",
                new { WorkCenterId = workCenterId, Version = dataVersion });
        }

        public void BulkUpsert(IEnumerable<CapacityRecord> records)
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

        public void BulkUpsert(IEnumerable<CapacityRecord> records, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
        {
            if (records == null) return;

            const int batchSize = 200;
            var batch = new List<CapacityRecord>(batchSize);
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

        private static void ExecuteBatch(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, List<CapacityRecord> batch)
        {
            const string prefix = @"INSERT OR REPLACE INTO Capacities
                          (Id, OrgId, WorkCenterId, CapacityHours, DataVersion, CreatedAt)
                          VALUES ";
            var values = new List<string>(batch.Count);
            var parameters = new DynamicParameters();
            for (int i = 0; i < batch.Count; i++)
            {
                var r = batch[i];
                var idx = i.ToString();
                values.Add($"(@Id{idx}, @OrgId{idx}, @WorkCenterId{idx}, @CapacityHours{idx}, @DataVersion{idx}, @CreatedAt{idx})");
                parameters.Add($"Id{idx}", r.Id);
                parameters.Add($"OrgId{idx}", r.OrgId);
                parameters.Add($"WorkCenterId{idx}", r.WorkCenterId);
                parameters.Add($"CapacityHours{idx}", r.CapacityHours);
                parameters.Add($"DataVersion{idx}", r.DataVersion);
                parameters.Add($"CreatedAt{idx}", r.CreatedAt.ToString("o"));
            }
            conn.Execute(prefix + string.Join(", ", values), parameters, tx);
        }
    }
}
