using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
        private readonly BomAddIn.Infrastructure.Security.IPasswordHasher _passwordHasher;

        private static readonly string[] Categories = { "RawMaterial", "SemiFinished", "FinishedGood", "Electronic", "Mechanical", "Packaging", "Chemical" };
        private static readonly string[] Units = { "pcs", "kg", "m", "L", "set", "roll", "pair", "box" };
        private static readonly string[] Warehouses = { "MAIN", "EAST", "WEST", "NORTH" };
        private static readonly string[] Suppliers = { "SUP-A", "SUP-B", "SUP-C" };

        /// <summary>SQL 字符串值转义 — 种子数据虽为内部生成，但预防单引号导致 SQL 语法错误 (M-2)</summary>
        private static string EscapeSql(string? value) => (value ?? "").Replace("'", "''");
        private readonly Random _rng = new Random(42); // 固定种子确保可重复

        public SeedDataGenerator(IDbConnectionFactory connectionFactory, IAuthorizationService authz,
            BomAddIn.Infrastructure.Security.IPasswordHasher passwordHasher)
        {
            _connectionFactory = connectionFactory;
            _authz = authz;
            _passwordHasher = passwordHasher;
        }

        public bool HasSeedData()
        {
            using var conn = _connectionFactory.CreateConnection();
            var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Materials WHERE IsActive = 1");
            return count > 100; // 超过 100 条视为已有种子数据
        }

        public SeedResult Generate(UserRole callerRole, int materialCount = 100000, int bomNodeCount = 500000, int historyMonths = 12)
        {
            _authz.Demand(callerRole, BomOperation.UserManage); // 种子数据生成仅限管理员
            var result = new SeedResult();

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                // 禁用外键检查以加速批量插入（种子数据自身保证引用完整性）
                conn.Execute("PRAGMA foreign_keys = OFF;");
                using var tx = conn.BeginTransaction();

                // TOCTOU fix: 在事务内检查，消除并发竞态条件
                var existingCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Materials WHERE IsActive = 1", transaction: tx);
                if (existingCount > 100)
                {
                    tx.Rollback();
                    result.Skipped = true;
                    return result;
                }

                try
                {
                    // 1. 生成物料
                    result.MaterialsCreated = GenerateMaterials(conn, tx, materialCount);

                    // 2. 生成供应商
                    result.SuppliersCreated = GenerateSuppliers(conn, tx);

                    // 3. 生成 BOM 结构（树形，最大 5 层）
                    result.BomNodesCreated = GenerateBomTree(conn, tx, materialCount, bomNodeCount);

                    // 4. 生成 BOM 版本历史
                    result.BomVersionsCreated = GenerateBomVersions(conn, tx);

                    // 5. 生成价格历史
                    result.PriceRecordsCreated = GeneratePriceHistory(conn, tx, materialCount, historyMonths);

                    // 6. 生成库存历史
                    result.InventoryRecordsCreated = GenerateInventoryHistory(conn, tx, materialCount, historyMonths);

                    // 7. 生成订单需求
                    result.OrdersCreated = GenerateOrders(conn, tx, materialCount);

                    // 8. 生成产能数据
                    result.CapacitiesCreated = GenerateCapacities(conn, tx);

                    // 9. 生成成本估算
                    result.EstimatesCreated = GenerateEstimates(conn, tx);

                    // 10. 生成同步日志
                    result.SyncLogsCreated = GenerateSyncLogs(conn, tx);

                    // 11. 创建默认用户
                    GenerateDefaultUser(conn, tx);

                    // 12. 初始化应用配置
                    GenerateAppConfig(conn, tx);

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
                // H-26: 所有物料统一 OrgId=1，确保 UDF 默认参数可直接查询
                const int orgId = 1;

                sb.AppendLine(sb.Length == 0
                    ? $"INSERT INTO Materials (OrgId, Code, Name, Spec, Unit, Category, IsActive, CreatedAt, UpdatedAt) VALUES"
                    : ",");
                sb.Append(
                    $"({orgId},'{EscapeSql(code)}','{EscapeSql(name)}','{EscapeSql(spec)}','{EscapeSql(unit)}','{EscapeSql(cat)}',1,datetime('now'),datetime('now'))");

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
            // H-26: 生成真实 BOM 树结构 — 可变扇出、受控共享、真实数量。
            // 扇出: 父节点 3-8 个子节点 (40%→3-4, 40%→5-6, 20%→7-8)
            // 根节点: 5% 物料 (成品/顶层组件)
            // 共享: ~15% 物料复用在 2 个父节点下 (模拟通用件)
            // 深度: 50% L1-2, 35% L3-4, 15% L5 (多数 BOM 较浅)
            // 数量: L1→1-5 (组装), L2-3→1-3 (子件), L4-5→1-10 (原材料)
            var sb = new StringBuilder();

            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();

            if (materialIds.Count == 0) return 0;

            var shuffled = materialIds.OrderBy(_ => _rng.Next()).ToList();

            // 5% 根物料
            int rootCount = Math.Max(3, shuffled.Count * 5 / 100);
            var roots = shuffled.Take(rootCount).ToList();
            var pool = shuffled.Skip(rootCount).ToList(); // 剩余的待分配物料池

            const int maxDepth = 5;

            // 层级分配权重 — 多数物料在前 3 层
            var levelWeights = new[] { 30, 25, 20, 15, 10 };
            var levelBuckets = new List<long>[maxDepth];
            int poolIdx = 0;
            for (int l = 0; l < maxDepth; l++)
            {
                int take = l == maxDepth - 1
                    ? pool.Count - poolIdx
                    : Math.Min(pool.Count * levelWeights[l] / 100, pool.Count - poolIdx);
                levelBuckets[l] = pool.Skip(poolIdx).Take(take).ToList();
                poolIdx += take;
            }

            var nodeParentCount = new Dictionary<long, int>(); // 物料被引用的次数
            double shareRate = 0.15; // 15% 共用件
            int inserted = 0;

            // Fanout generator: weighted random 3-8
            int NextFanout()
            {
                int roll = _rng.Next(100);
                if (roll < 40) return _rng.Next(3, 5);      // 40% → 3-4
                if (roll < 80) return _rng.Next(5, 7);      // 40% → 5-6
                return _rng.Next(7, 9);                      // 20% → 7-8
            }

            // Quantity by level
            double NextQty(int level) => level switch
            {
                1 => Math.Round(_rng.NextDouble() * 4 + 1, 2),          // 1-5 (组装用量)
                2 or 3 => Math.Round(_rng.NextDouble() * 2 + 1, 2),     // 1-3 (子件用量)
                _ => Math.Round(_rng.NextDouble() * 9 + 1, 2)           // 1-10 (原材料)
            };

            string NextBomType(int level)
            {
                int roll = _rng.Next(100);
                if (roll < 5) return "CBOM";         // 5% 客户视图
                if (roll < 95 - level * 10) return "EBOM";
                return "MBOM";
            }

            string NextVersionState()
            {
                int roll = _rng.Next(100);
                if (roll < 2) return "Obsolete";     // 2%
                if (roll < 7) return "Draft";        // 5%
                return "Released";                    // 93%
            }

            // 为每层构建 BOM 边
            var nodesByLevel = new Dictionary<int, List<long>> { [0] = roots };

            for (int level = 1; level <= maxDepth && poolIdx > 0; level++)
            {
                nodesByLevel[level] = new List<long>();
                var parents = nodesByLevel[level - 1].OrderBy(_ => _rng.Next()).ToList();
                var children = levelBuckets[level - 1];
                if (children.Count == 0) continue;

                int parentIdx = 0;
                int childIdx = 0;

                if (parents.Count == 0) break;

                while (childIdx < children.Count)
                {
                    var parentId = parents[parentIdx % parents.Count];
                    parentIdx++;

                    // 确定该父节点本层扇出
                    int fanout = Math.Min(NextFanout(), children.Count - childIdx);
                    for (int f = 0; f < fanout && childIdx < children.Count; f++, childIdx++)
                    {
                        var childId = children[childIdx];
                        var qty = NextQty(level);
                        var scrapRate = Math.Round(_rng.NextDouble() * 0.03, 4);
                        var validFrom = DateTime.Today.AddDays(-_rng.Next(365));
                        var bomType = NextBomType(level);
                        var versionState = NextVersionState();

                        sb.AppendLine(sb.Length == 0
                            ? $"INSERT INTO BomStructures (OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate, BomViewType, Level, ValidFrom, VersionState, CreatedAt, UpdatedAt) VALUES"
                            : ",");
                        sb.Append(
                            $"(1,{parentId},{childId},{qty},'{_rng.Next(1, 100)}',{scrapRate},'{EscapeSql(bomType)}',{level},'{validFrom:yyyy-MM-dd}','{EscapeSql(versionState)}',datetime('now'),datetime('now'))");

                        nodeParentCount.TryGetValue(childId, out var existing);
                        nodeParentCount[childId] = existing + 1;
                        nodesByLevel[level].Add(childId);
                        inserted++;

                        // 共享件: ~15% 物料额外加到另一个父节点 (在后续层级)
                        if (level < maxDepth && _rng.NextDouble() < shareRate)
                        {
                            var spareParent = parents[_rng.Next(parents.Count)];
                            if (spareParent != parentId)
                            {
                                sb.AppendLine(",");
                                sb.Append(
                                    $"(1,{spareParent},{childId},{Math.Round(_rng.NextDouble()*2+1,2)},'{_rng.Next(1,100)}',{scrapRate},'{EscapeSql(bomType)}',{level + 1},'{validFrom:yyyy-MM-dd}','{EscapeSql(versionState)}',datetime('now'),datetime('now'))");
                                nodeParentCount.TryGetValue(childId, out var existing2);
                                nodeParentCount[childId] = existing2 + 1;
                                inserted++;
                            }
                        }

                        if (sb.Length >= 40000)
                        {
                            conn.Execute(sb.ToString(), transaction: tx);
                            sb.Clear();
                        }

                        if (inserted >= targetNodes)
                            break;
                    }

                    if (inserted >= targetNodes || childIdx >= children.Count)
                        break;
                }

                if (inserted >= targetNodes)
                    break;
            }

            if (sb.Length > 0)
                conn.Execute(sb.ToString(), transaction: tx);

            return inserted;
        }

        private int GeneratePriceHistory(IDbConnection conn, IDbTransaction tx, int materialCount, int months)
        {
            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();
            var supplierIds = conn.Query<long>(
                "SELECT Id FROM Suppliers ORDER BY Id", transaction: tx).AsList();
            if (materialIds.Count == 0) return 0;

            var sb = new StringBuilder();
            var totalInserted = 0;

            foreach (var matId in materialIds)
            {
                var basePrice = Math.Round(_rng.NextDouble() * 900 + 10, 2);
                var supplierId = supplierIds.Count > 0 ? supplierIds[_rng.Next(supplierIds.Count)] : 1L;
                var currency = _rng.Next(10) > 7 ? "USD" : "CNY";

                for (int m = 0; m < months; m++)
                {
                    var price = Math.Round(basePrice * (1 + (_rng.NextDouble() - 0.5) * 0.2), 4);
                    var effDate = DateTime.Today.AddMonths(-months + m);

                    sb.AppendLine(sb.Length == 0
                        ? $"INSERT INTO Prices (OrgId, MaterialId, SupplierId, UnitPrice, Currency, DataVersion, EffectiveDate, CreatedAt) VALUES"
                        : ",");
                    sb.Append(
                        $"(1,{matId},{supplierId},{price},'{EscapeSql(currency)}','{m + 1}','{effDate:yyyy-MM-dd}',datetime('now'))");

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
                    sb.Append(
                        $"(1,{matId},'{EscapeSql(warehouse)}',{qty},'{m + 1}','{snapDate:yyyy-MM-dd}',datetime('now'))");

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

        // ============================================
        // H-27: 新增数据表生成
        // ============================================

        private int GenerateSuppliers(IDbConnection conn, IDbTransaction tx)
        {
            var names = new[] {
                "深圳华强电子", "上海精密机械", "广州化工原料", "北京半导体",
                "成都航空材料", "武汉光电科技", "南京轴承集团", "杭州包装材料",
                "天津线缆工业", "苏州自动化", "东莞模具制造", "青岛橡胶制品",
                "西安电子元件", "重庆精密铸造", "厦门复合材料",
                "郑州五金加工", "长沙传感器", "大连注塑成型", "无锡冲压件", "佛山表面处理"
            };

            int inserted = 0;
            for (int i = 0; i < names.Length; i++)
            {
                var code = $"SUP-{i + 1:D3}";
                var contact = $"联系人{_rng.Next(1, 9)}";
                var rating = _rng.Next(3, 6); // 3-5 星评分

                conn.Execute(
                    @"INSERT INTO Suppliers (OrgId, Code, Name, Contact, Rating, CreatedAt, UpdatedAt)
                      VALUES (1, @Code, @Name, @Contact, @Rating, datetime('now'), datetime('now'))",
                    new { Code = code, Name = names[i], Contact = contact, Rating = rating }, tx);
                inserted++;
            }
            return inserted;
        }

        private int GenerateBomVersions(IDbConnection conn, IDbTransaction tx)
        {
            // 为 Draft/Obsolete 状态的 BOM 节点生成版本历史
            var nodes = conn.Query<(long Id, string State)>(
                "SELECT Id, VersionState FROM BomStructures WHERE VersionState IN ('Draft', 'Obsolete')", transaction: tx).AsList();

            if (nodes.Count == 0) return 0;

            int inserted = 0;
            foreach (var (bomId, state) in nodes)
            {
                // 先有一个 Released 版本
                conn.Execute(
                    @"INSERT INTO BomVersions (BomId, VersionNumber, State, CreatedAt)
                      VALUES (@BomId, 1, 'Released', datetime('now', '-30 days'))",
                    new { BomId = bomId }, tx);
                inserted++;

                // 然后是当前 Draft/Obsolete 版本
                conn.Execute(
                    @"INSERT INTO BomVersions (BomId, VersionNumber, State, CreatedAt)
                      VALUES (@BomId, 2, @State, datetime('now'))",
                    new { BomId = bomId, State = state }, tx);
                inserted++;
            }

            return inserted;
        }

        private int GenerateOrders(IDbConnection conn, IDbTransaction tx, int materialCount)
        {
            // 为 ~30% 物料生成未完成订单
            var materialIds = conn.Query<long>(
                "SELECT Id FROM Materials WHERE IsActive = 1 ORDER BY Id", transaction: tx).AsList();

            int targetCount = materialIds.Count * 30 / 100;
            var selected = materialIds.OrderBy(_ => _rng.Next()).Take(targetCount).ToList();

            var sb = new StringBuilder();
            int inserted = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                var matId = selected[i];
                var qty = _rng.Next(100, 5000);
                var dueDate = DateTime.Today.AddDays(_rng.Next(7, 90));
                var version = _rng.Next(1, 4);

                sb.AppendLine(sb.Length == 0
                    ? $"INSERT INTO Orders (OrgId, MaterialId, OrderQty, DueDate, DataVersion, CreatedAt) VALUES"
                    : ",");
                sb.Append($"(1,{matId},{qty},'{dueDate:yyyy-MM-dd}',{version},datetime('now'))");
                inserted++;

                if (sb.Length >= 50000)
                {
                    conn.Execute(sb.ToString(), transaction: tx);
                    sb.Clear();
                }
            }
            if (sb.Length > 0)
                conn.Execute(sb.ToString(), transaction: tx);

            return inserted;
        }

        private int GenerateCapacities(IDbConnection conn, IDbTransaction tx)
        {
            var workCenters = new[] { "WC-ASSY-01", "WC-MACH-02", "WC-WELD-03", "WC-PAINT-04",
                                      "WC-TEST-05", "WC-PACK-06", "WC-QUAL-07" };
            int inserted = 0;
            foreach (var wc in workCenters)
            {
                var hours = Math.Round(160.0 + _rng.NextDouble() * 80, 1); // 160-240 小时/月
                var version = _rng.Next(1, 4);
                conn.Execute(
                    @"INSERT INTO Capacities (OrgId, WorkCenterId, CapacityHours, DataVersion, CreatedAt)
                      VALUES (1, @WC, @Hours, @Version, datetime('now'))",
                    new { WC = wc, Hours = hours, Version = version }, tx);
                inserted++;
            }
            return inserted;
        }

        private int GenerateEstimates(IDbConnection conn, IDbTransaction tx)
        {
            // 为根物料生成成本估算
            var roots = conn.Query<long>(
                @"SELECT DISTINCT ParentMaterialId FROM BomStructures
                  WHERE ParentMaterialId NOT IN (SELECT DISTINCT ChildMaterialId FROM BomStructures)
                  ORDER BY ParentMaterialId LIMIT 15", transaction: tx).AsList();

            var bomVersionIds = conn.Query<long>(
                "SELECT Id FROM BomVersions ORDER BY Id LIMIT 30", transaction: tx).AsList();

            if (roots.Count == 0) return 0;

            int inserted = 0;
            foreach (var rootId in roots)
            {
                var totalCost = Math.Round(_rng.NextDouble() * 500000 + 50000, 2); // 5万-55万
                var laborHours = Math.Round(_rng.NextDouble() * 200 + 20, 1);      // 20-220 小时
                var bomVersionId = bomVersionIds.Count > 0
                    ? bomVersionIds[_rng.Next(bomVersionIds.Count)]
                    : (long?)null;

                conn.Execute(
                    @"INSERT INTO Estimates (OrgId, BomVersionId, TotalCost, LaborHours, Notes, CreatedAt, UpdatedAt)
                      VALUES (1, @BomVersionId, @TotalCost, @LaborHours, @Notes, datetime('now'), datetime('now'))",
                    new
                    {
                        BomVersionId = bomVersionId,
                        TotalCost = totalCost,
                        LaborHours = laborHours,
                        Notes = $"成本估算 #{inserted + 1}"
                    }, tx);
                inserted++;
            }
            return inserted;
        }

        private void GenerateDefaultUser(IDbConnection conn, IDbTransaction tx)
        {
            var existing = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Users WHERE Username = 'admin'", transaction: tx);
            if (existing > 0) return;

            var adminHash = _passwordHasher.Hash("admin123");
            conn.Execute(
                @"INSERT INTO Users (Username, PasswordHash, Role, OrgId, IsActive, CreatedAt)
                  VALUES ('admin', @Hash, 'Admin', 1, 1, datetime('now'))",
                new { Hash = adminHash }, tx);

            var viewerHash = _passwordHasher.Hash("viewer123");
            conn.Execute(
                @"INSERT INTO Users (Username, PasswordHash, Role, OrgId, IsActive, CreatedAt)
                  VALUES ('viewer', @Hash, 'Viewer', 1, 1, datetime('now'))",
                new { Hash = viewerHash }, tx);
        }

        private void GenerateAppConfig(IDbConnection conn, IDbTransaction tx)
        {
            var existing = conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM AppConfig WHERE Key = 'Environment:Current'", transaction: tx);
            if (existing > 0) return;

            var configs = new[] {
                ("Environment:Current", "DEV", "当前运行环境 (DEV/PROD)"),
                ("Sync:ErpEndpoint", "https://erp-sim.example.com/api", "ERP 同步端点 URL"),
                ("Sync:AutoIntervalMinutes", "30", "自动同步间隔（分钟）"),
                ("Cache:DefaultTtlMinutes", "5", "默认缓存 TTL（分钟）"),
                ("Bom:MaxExpandDepth", "20", "BOM 展开最大层级"),
                ("Alert:PriceChangeThreshold", "0.15", "价格变化预警阈值 (15%)")
            };

            foreach (var (key, value, desc) in configs)
            {
                conn.Execute(
                    @"INSERT INTO AppConfig (Key, Value, Description, UpdatedAt)
                      VALUES (@Key, @Value, @Desc, datetime('now'))",
                    new { Key = key, Value = value, Desc = desc }, tx);
            }
        }

        private int GenerateSyncLogs(IDbConnection conn, IDbTransaction tx)
        {
            var types = new[] { "Full", "Incremental", "Prices", "Full", "Incremental" };
            var statuses = new[] { "Complete", "Complete", "Complete", "Complete", "Complete" };
            var recordCounts = new[] { 6000, 120, 1500, 6050, 45 };

            int inserted = 0;
            for (int i = 0; i < types.Length; i++)
            {
                var startedAt = DateTime.Today.AddDays(-(types.Length - i));
                conn.Execute(
                    @"INSERT INTO SyncLogs (SyncType, StartedAt, CompletedAt, RecordsProcessed, Status, ErrorMessage)
                      VALUES (@Type, @StartedAt, @CompletedAt, @Records, @Status, @Error)",
                    new
                    {
                        Type = types[i],
                        StartedAt = startedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        CompletedAt = startedAt.AddMinutes(_rng.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss"),
                        Records = recordCounts[i],
                        Status = statuses[i],
                        Error = (string?)null
                    }, tx);
                inserted++;
            }
            return inserted;
        }
    }
}
