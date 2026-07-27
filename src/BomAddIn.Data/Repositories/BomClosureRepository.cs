using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    /// <summary>
    /// BOM Closure Table 仓库 — 基于预计算的祖先-后代关系表。
    /// 查询复杂度 O(1)（单次 SQL），替代 BFS 逐层 N 次查询。
    /// </summary>
    public class BomClosureRepository : IBomClosureRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public BomClosureRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IEnumerable<BomClosureNode> GetDescendants(long materialId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomClosureNode>(
                @"SELECT DescendantId AS MaterialId, Depth, PathQuantity
                  FROM BomClosure
                  WHERE AncestorId = @Id
                  ORDER BY Depth, DescendantId",
                new { Id = materialId }).ToList();
        }

        public IEnumerable<BomClosureNode> GetAncestors(long materialId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomClosureNode>(
                @"SELECT AncestorId AS MaterialId, Depth, PathQuantity
                  FROM BomClosure
                  WHERE DescendantId = @Id
                  ORDER BY Depth, AncestorId",
                new { Id = materialId }).ToList();
        }

        public IEnumerable<BomClosureNode> GetDirectChildren(long materialId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<BomClosureNode>(
                @"SELECT DescendantId AS MaterialId, Depth, PathQuantity
                  FROM BomClosure
                  WHERE AncestorId = @Id AND Depth = 1
                  ORDER BY DescendantId",
                new { Id = materialId }).ToList();
        }

        public int GetMaxDepth(long materialId)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<int?>(
                "SELECT MAX(Depth) FROM BomClosure WHERE AncestorId = @Id",
                new { Id = materialId }) ?? 0;
        }

        public void Rebuild()
        {
            // 设计说明：Rebuild CTE 存储上限为 50 层（保持闭包表完整性），
            // 而查询层（BomAnalysisProvider）截断为 20 层并插入 [TRUNCATED] 哨兵。
            // 存储完整 + 展示截断，确保超深 BOM 数据不丢失且用户可见警告。
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(@"
                DELETE FROM BomClosure;

                INSERT OR IGNORE INTO BomClosure (AncestorId, DescendantId, Depth, PathQuantity)
                WITH RECURSIVE closure(ancestor, descendant, depth, qty) AS (
                    SELECT DISTINCT ParentMaterialId, ParentMaterialId, 0, 1.0
                    FROM BomStructures WHERE VersionState = 'Released'
                    UNION ALL
                    SELECT DISTINCT ChildMaterialId, ChildMaterialId, 0, 1.0
                    FROM BomStructures WHERE VersionState = 'Released'
                    UNION ALL
                    SELECT c.ancestor, b.ChildMaterialId, c.depth + 1, c.qty * b.Quantity
                    FROM closure c
                    JOIN BomStructures b ON b.ParentMaterialId = c.descendant
                    WHERE b.VersionState = 'Released' AND c.depth < 50
                )
                SELECT ancestor, descendant, MIN(depth), SUM(qty)
                FROM closure WHERE depth > 0
                GROUP BY ancestor, descendant;
            ");
        }

        public bool HasData()
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<long>("SELECT COUNT(*) FROM BomClosure LIMIT 1") > 0;
        }
    }
}
