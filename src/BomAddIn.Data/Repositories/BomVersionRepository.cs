using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class BomVersionRepository : IBomVersionRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public BomVersionRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IEnumerable<BomVersion> GetByBomId(long bomId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomVersion>(
                "SELECT * FROM BomVersions WHERE BomId = @BomId ORDER BY VersionNumber DESC",
                new { BomId = bomId }).ToList();
        }

        public BomVersion? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<BomVersion>(
                "SELECT * FROM BomVersions WHERE Id = @Id", new { Id = id });
        }

        public BomVersion? GetById(long id, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
        {
            return conn.QueryFirstOrDefault<BomVersion>(
                "SELECT * FROM BomVersions WHERE Id = @Id", new { Id = id }, tx);
        }

        public void Add(BomVersion version)
        {
            using var conn = _connectionFactory.CreateConnection();
            version.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO BomVersions (BomId, VersionNumber, State, ApprovedBy, ApprovedAt, CreatedAt)
                  VALUES (@BomId, @VersionNumber, @State, @ApprovedBy, @ApprovedAt, @CreatedAt);
                  SELECT last_insert_rowid();",
                new
                {
                    version.BomId,
                    version.VersionNumber,
                    State = version.State.ToString(),
                    version.ApprovedBy,
                    version.ApprovedAt,
                    version.CreatedAt
                });
        }

        public void UpdateState(long id, VersionState state, long? approvedBy = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            UpdateState(id, state, approvedBy, conn, null);
        }

        public void UpdateState(long id, VersionState state, long? approvedBy, System.Data.IDbConnection conn, System.Data.IDbTransaction? tx)
        {
            // ApprovedBy 在以下状态持久化:
            //   - PendingReview: 记录提交人（用于自我审批检查）
            //   - Approved/Released: 记录审批人
            var shouldSetApprovedBy = approvedBy.HasValue &&
                (state == VersionState.PendingReview || state == VersionState.Approved || state == VersionState.Released);
            conn.Execute(
                @"UPDATE BomVersions SET State=@State,
                  ApprovedBy = CASE WHEN @SetApprovedBy THEN @ApprovedBy ELSE ApprovedBy END,
                  ApprovedAt = CASE WHEN @SetApprovedAt THEN @ApprovedAt ELSE ApprovedAt END
                  WHERE Id=@Id",
                new
                {
                    Id = id,
                    State = state.ToString(),
                    SetApprovedBy = shouldSetApprovedBy,
                    ApprovedBy = approvedBy,
                    SetApprovedAt = state == VersionState.Approved || state == VersionState.Released,
                    ApprovedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }, tx);
        }

        public BomVersion? GetLatest(long bomId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<BomVersion>(
                "SELECT * FROM BomVersions WHERE BomId = @BomId ORDER BY VersionNumber DESC LIMIT 1",
                new { BomId = bomId });
        }
    }
}
