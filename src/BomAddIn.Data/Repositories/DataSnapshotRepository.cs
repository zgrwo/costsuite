using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class DataSnapshotRepository : IDataSnapshotRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DataSnapshotRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Add(DataSnapshot snapshot)
        {
            using var conn = _connectionFactory.CreateConnection();
            snapshot.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO DataSnapshots (SnapshotType, SnapshotData, CreatedAt, Description)
                  VALUES (@SnapshotType, @SnapshotData, @CreatedAt, @Description);
                  SELECT last_insert_rowid();",
                new
                {
                    snapshot.SnapshotType,
                    snapshot.SnapshotData,
                    CreatedAt = snapshot.CreatedAt.ToString("o"),
                    snapshot.Description
                });
        }

        public DataSnapshot? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<DataSnapshot>(
                "SELECT * FROM DataSnapshots WHERE Id = @Id", new { Id = id });
        }

        public DataSnapshot? GetLatest(string snapshotType)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<DataSnapshot>(
                @"SELECT * FROM DataSnapshots
                  WHERE SnapshotType = @Type
                  ORDER BY CreatedAt DESC LIMIT 1",
                new { Type = snapshotType });
        }

        public IEnumerable<DataSnapshot> GetByType(string snapshotType, int limit = 20)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<DataSnapshot>(
                @"SELECT * FROM DataSnapshots
                  WHERE SnapshotType = @Type
                  ORDER BY CreatedAt DESC LIMIT @Limit",
                new { Type = snapshotType, Limit = limit });
        }

        public void DeleteOlderThan(DateTime cutoff, string? snapshotType = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "DELETE FROM DataSnapshots WHERE CreatedAt < @Cutoff";
            var parameters = new DynamicParameters();
            parameters.Add("Cutoff", cutoff.ToString("o"));

            if (!string.IsNullOrWhiteSpace(snapshotType))
            {
                sql += " AND SnapshotType = @Type";
                parameters.Add("Type", snapshotType);
            }

            conn.Execute(sql, parameters);
        }
    }
}
