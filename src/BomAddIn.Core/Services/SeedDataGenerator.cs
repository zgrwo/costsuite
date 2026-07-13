using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using BomAddIn.Data.Connection;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;

namespace BomAddIn.Core.Services
{
    /// <summary>
    /// 种子数据生成器 — 使用批量 INSERT 生成测试数据。
    /// 默认 1 万物料 + 5 万 BOM 节点（V1.0 合理规模），可配置到 10 万 + 50 万。
    /// </summary>
    public class SeedDataGenerator : ISeedDataGenerator
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;

        private static readonly string[] Categories = { "RawMaterial", "SemiFinished", "FinishedGood", "Electronic", "Mechanical", "Packaging", "Chemical" };
        private static readonly string[] Units = { "pcs", "kg", "m", "L", "set", "roll", "pair", "box" };
        private static readonly string[] Warehouses = { "MAIN", "EAST", "WEST", "NORTH" };
        private static readonly string[] Suppliers = { "SUP-A", "SUP-B", "SUP-C" };
        private readonly Random _rng = new Random(42); // 固定种子确保可重复

        public SeedDataGenerator(IDbConnectionFactory connectionFactory, IAuthorizationService authz)
        {
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public bool HasSeedData()
        {
            using var conn = _connectionFactory.CreateConnection();
            var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Materials WHERE IsActive = 1");
            return count > 100; // 超过 100 条视为已有种子数据
        }

        public SeedResult Generate(int materialCount = 100000, int bomNodeCount = 500000, int historyMonths = 12, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.UserManage); // 种子数据生成仅限管理员
            var result = new SeedResult();

            try
            {
                if (HasSeedData())
                {
                    result.Skipped = true;
                    return result;
                }

                using var conn = _connectionFactory.CreateConnection();
                // 禁用外键检查以加速批量插入（种子数据自身保证引用完整性）
                conn.Execute("PRAGMA foreign_keys = OFF;");
                using var tx = conn.BeginTransaction();

                try
                {
                    // 1. 生成物料
                    result.MaterialsCreated = GenerateMaterials(conn, tx, materialCount);

                    // 2. 生成 BOM 结构（树形，最大 5 层）
                    result.BomNodesCreated = GenerateBomTree(conn, tx, materialCount, bomNodeCount);

                    // 3. 生成价格历史
                    result.PriceRecordsCreated = GeneratePriceHistory(conn, tx, materialCount, historyMonths);

                    // 4. 生成库存历史
                    result.InventoryRecordsCreated = GenerateInventoryHistory(conn, tx, materialCount, historyMonths);

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private int GenerateMaterials(IDbConnection conn, IDbTransaction tx, int count)
        {
            var batchSize = 500;
            var inserted = 0;
            var sb = new StringBuilder();

            for (int i = 1; i <= count; i++)
            {
                var code = $"MAT-{i:D6}";
                var cat = Categories[_rng.Next(Categories.Length)];
                var unit = Units[_rng.Next(Units.Length)];
                var name = $"{cat} Item-{i:D5}";
                var spec = $"Spec-{_rng.Next(1000, 9999)}";
                var orgId = _rng.Next(1, 4);

                sb.AppendLine(sb.Length == 0
                    ? $"INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive, CreatedAt, UpdatedAt) VALUES"
                    : ",");
                sb.Append($"({orgId},'{code}','{name}','{spec}','{unit}','{cat}',1,datetime('now'),datetime('now'))");

                if (i % batchSize == 0 || i == count)
                {
                    conn.Execute(sb.ToString(), transaction: tx);
                    sb.Clear();
                    inserted += i % batchSize == 0 ? batchSize : i % batchSize;
                }
            }
            return inserted;
        }

        private int GenerateBomTree(IDbConnection conn, IDbTransaction tx, int materialCount, int targetNodes)
        {
            // 为每个物料构建 BOM 树：选择随机父节点和子节点
            var batchSize = 200;
            var inserted = 0;
            var sb = new StringBuilder();

            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();

            if (materialIds.Count == 0) return 0;

            // 每层约：顶层 20%，二层 25%，三层 25%，四层 20%，五层 10%
            var levels = new[] { 0.20, 0.25, 0.25, 0.20, 0.10 };

            for (int i = 0; i < targetNodes; i++)
            {
                var level = i < targetNodes * 0.2 ? 1 :
                            i < targetNodes * 0.45 ? 2 :
                            i < targetNodes * 0.7 ? 3 :
                            i < targetNodes * 0.9 ? 4 : 5;

                var parentIdx = _rng.Next(materialIds.Count);
                var childIdx = _rng.Next(materialIds.Count);
                while (childIdx == parentIdx) childIdx = _rng.Next(materialIds.Count);

                var qty = Math.Round(_rng.NextDouble() * 90 + 0.1, 2);
                var scrapRate = Math.Round(_rng.NextDouble() * 0.05, 4);
                var validFrom = DateTime.Today.AddDays(-_rng.Next(365));
                var bomType = level <= 2 ? "EBOM" : "MBOM";
                var orgId = _rng.Next(1, 4);

                sb.AppendLine(sb.Length == 0
                    ? $"INSERT INTO BomStructures (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate, BomViewType, Level, ValidFrom, VersionState, CreatedAt, UpdatedAt) VALUES"
                    : ",");
                sb.Append($"({orgId},{materialIds[parentIdx]},{materialIds[childIdx]},{qty},'{_rng.Next(1, 100)}',{scrapRate},'{bomType}',{level},'{validFrom:yyyy-MM-dd}','Released',datetime('now'),datetime('now'))");

                if (i % batchSize == 0 || i == targetNodes - 1)
                {
                    if (sb.Length > 0)
                    {
                        conn.Execute(sb.ToString(), transaction: tx);
                        var batchInserted = Math.Min(batchSize, targetNodes - i + batchSize - 1);
                        if (i == 0) batchInserted = batchSize;
                        inserted += batchInserted;
                        sb.Clear();
                    }
                }
            }
            return targetNodes;
        }

        private int GeneratePriceHistory(IDbConnection conn, IDbTransaction tx, int materialCount, int months)
        {
            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();
            if (materialIds.Count == 0) return 0;

            var sb = new StringBuilder();
            var totalInserted = 0;

            foreach (var matId in materialIds)
            {
                var basePrice = Math.Round(_rng.NextDouble() * 900 + 10, 2);
                var supplierId = _rng.Next(1, 4);
                var currency = _rng.Next(10) > 7 ? "USD" : "CNY";

                for (int m = 0; m < months; m++)
                {
                    var price = Math.Round(basePrice * (1 + (_rng.NextDouble() - 0.5) * 0.2), 4);
                    var effDate = DateTime.Today.AddMonths(-months + m);

                    sb.AppendLine(sb.Length == 0
                        ? $"INSERT INTO Prices (OrgId, MaterialId, SupplierId, UnitPrice, Currency, DataVersion, EffectiveDate, CreatedAt) VALUES"
                        : ",");
                    sb.Append($"(1,{matId},{supplierId},{price},'{currency}','{m + 1}','{effDate:yyyy-MM-dd}',datetime('now'))");

                    totalInserted++;

                    if (sb.Length > 50000) // 每 ~500 行 flush
                    {
                        conn.Execute(sb.ToString(), transaction: tx);
                        sb.Clear();
                    }
                }
            }

            if (sb.Length > 0)
                conn.Execute(sb.ToString(), transaction: tx);

            return totalInserted;
        }

        private int GenerateInventoryHistory(IDbConnection conn, IDbTransaction tx, int materialCount, int months)
        {
            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();
            if (materialIds.Count == 0) return 0;

            var sb = new StringBuilder();
            var totalInserted = 0;

            foreach (var matId in materialIds)
            {
                var baseQty = _rng.Next(100, 10000);

                for (int m = 0; m < months; m++)
                {
                    var qty = Math.Max(0, baseQty + _rng.Next(-200, 200));
                    var snapDate = DateTime.Today.AddMonths(-months + m);
                    var warehouse = Warehouses[_rng.Next(Warehouses.Length)];

                    sb.AppendLine(sb.Length == 0
                        ? $"INSERT INTO Inventories (OrgId, MaterialId, WarehouseId, Quantity, DataVersion, SnapshotDate, CreatedAt) VALUES"
                        : ",");
                    sb.Append($"(1,{matId},'{warehouse}',{qty},'{m + 1}','{snapDate:yyyy-MM-dd}',datetime('now'))");

                    totalInserted++;

                    if (sb.Length > 50000)
                    {
                        conn.Execute(sb.ToString(), transaction: tx);
                        sb.Clear();
                    }
                }
            }

            if (sb.Length > 0)
                conn.Execute(sb.ToString(), transaction: tx);

            return totalInserted;
        }
    }
}
