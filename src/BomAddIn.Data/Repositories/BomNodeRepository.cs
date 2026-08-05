using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class BomNodeRepository : IBomNodeRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public BomNodeRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public BomNode? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return GetById(id, conn, null);
        }

        public BomNode? GetById(long id, IDbConnection conn, IDbTransaction? tx)
        {
            return conn.QueryFirstOrDefault<BomNode>(
                "SELECT * FROM BomStructures WHERE Id = @Id", new { Id = id }, tx);
        }

        public IEnumerable<BomNode> GetChildren(long parentMaterialId, DateTime? asOfDate = null)
        {
            var date = (asOfDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomNode>(
                @"SELECT * FROM BomStructures
                  WHERE ParentMaterialId = @ParentId
                    AND date(ValidFrom) <= date(@Date)
                    AND (ValidTo IS NULL OR date(ValidTo) > date(@Date))
                    AND VersionState = 'Released'
                  ORDER BY Position",
                new { ParentId = parentMaterialId, Date = date }).ToList();
        }

        public IEnumerable<BomNode> GetByMaterialId(long materialId, DateTime? asOfDate = null)
        {
            var date = (asOfDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomNode>(
                @"SELECT * FROM BomStructures
                  WHERE ChildMaterialId = @MaterialId
                    AND date(ValidFrom) <= date(@Date)
                    AND (ValidTo IS NULL OR date(ValidTo) > date(@Date))
                    AND VersionState = 'Released'",
                new { MaterialId = materialId, Date = date }).ToList();
        }

        public void Add(BomNode node)
        {
            using var conn = _connectionFactory.CreateConnection();
            node.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO BomStructures
                  (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate,
                   BomViewType, Level, ValidFrom, ValidTo, VersionState, CreatedAt, UpdatedAt)
                  VALUES (@OrgId, @ParentMaterialId, @ChildMaterialId, @Quantity, @Position, @ScrapRate,
                   @BomViewType, @Level, @ValidFrom, @ValidTo, @VersionState, @CreatedAt, @UpdatedAt);
                  SELECT last_insert_rowid();",
                GetBomNodeParams(node));
        }

        public void Add(BomNode node, IDbConnection conn, IDbTransaction tx)
        {
            node.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO BomStructures
                  (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate,
                   BomViewType, Level, ValidFrom, ValidTo, VersionState, CreatedAt, UpdatedAt)
                  VALUES (@OrgId, @ParentMaterialId, @ChildMaterialId, @Quantity, @Position, @ScrapRate,
                   @BomViewType, @Level, @ValidFrom, @ValidTo, @VersionState, @CreatedAt, @UpdatedAt);
                  SELECT last_insert_rowid();",
                GetBomNodeParams(node), tx);
        }

        private static object GetBomNodeParams(BomNode node) => new
        {
            // Max-review P0 fix: UPDATE 语句引用 @Id，匿名参数必须包含 Id，否则绑定失败
            node.Id,
            node.OrgId,
            node.ParentMaterialId,
            node.ChildMaterialId,
            node.Quantity,
            node.Position,
            node.ScrapRate,
            node.BomViewType,
            node.Level,
            ValidFrom = node.ValidFrom.ToString("yyyy-MM-dd"),
            ValidTo = node.ValidTo?.ToString("yyyy-MM-dd"),
            VersionState = node.VersionState.ToString(),
            CreatedAt = node.CreatedAt.ToString("o"),
            UpdatedAt = node.UpdatedAt.ToString("o")
        };

        public void Update(BomNode node)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE BomStructures SET Quantity=@Quantity, Position=@Position,
                  ScrapRate=@ScrapRate, BomViewType=@BomViewType, ValidTo=@ValidTo,
                  VersionState=@VersionState, UpdatedAt=@UpdatedAt
                  WHERE Id=@Id AND OrgId=@OrgId",
                // H-1 fix: 复用 GetBomNodeParams 消除重复转换逻辑
                // Dapper 忽略 SQL 中未引用的额外参数，UPDATE 只使用其需要的列
                GetBomNodeParams(node));
        }

        public void Update(BomNode node, IDbConnection conn, IDbTransaction tx)
        {
            conn.Execute(
                @"UPDATE BomStructures SET Quantity=@Quantity, Position=@Position,
                  ScrapRate=@ScrapRate, BomViewType=@BomViewType, ValidTo=@ValidTo,
                  VersionState=@VersionState, UpdatedAt=@UpdatedAt
                  WHERE Id=@Id AND OrgId=@OrgId",
                // H-1 fix: 复用 GetBomNodeParams 消除重复转换逻辑
                GetBomNodeParams(node), tx);
        }

        public void Delete(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute("DELETE FROM BomStructures WHERE Id = @Id", new { Id = id });
        }

        public void Delete(long id, IDbConnection conn, IDbTransaction tx)
        {
            conn.Execute("DELETE FROM BomStructures WHERE Id = @Id", new { Id = id }, tx);
        }
    }
}
