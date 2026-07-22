using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;

namespace BomAddIn.Core.Services
{
    /// <summary>快照服务 — 序列化核心表 → JSON → DataSnapshots 表</summary>
    public class SnapshotService : ISnapshotService
    {
        private readonly IDataSnapshotRepository _snapshotRepo;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;

        public SnapshotService(
            IDataSnapshotRepository snapshotRepo,
            IDbConnectionFactory connectionFactory,
            IAuthorizationService authz)
        {
            _snapshotRepo = snapshotRepo;
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public DataSnapshot CreateSnapshot(UserRole callerRole, string type = "Manual", string? description = null)
        {
            _authz.Demand(callerRole, BomOperation.BomRead);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                // B-6 fix: 限制快照行数避免 OOM（32 位 Excel 2GB 进程空间）
                //     大数据集应使用数据库级备份而非内存 JSON 快照
                const int maxRowsPerTable = 50000;
                var materials = conn.Query<Material>(
                    "SELECT * FROM Materials LIMIT @Limit", new { Limit = maxRowsPerTable }, tx).AsList();
                var bomStructures = conn.Query<BomNode>(
                    "SELECT * FROM BomStructures LIMIT @Limit", new { Limit = maxRowsPerTable }, tx).AsList();
                var bomVersions = conn.Query<BomVersion>(
                    "SELECT * FROM BomVersions LIMIT @Limit", new { Limit = maxRowsPerTable }, tx).AsList();
                var prices = conn.Query<PriceRecord>(
                    "SELECT * FROM Prices ORDER BY EffectiveDate DESC LIMIT @Limit", new { Limit = maxRowsPerTable }, tx).AsList();
                var inventories = conn.Query<InventoryRecord>(
                    "SELECT * FROM Inventories ORDER BY SnapshotDate DESC LIMIT @Limit", new { Limit = maxRowsPerTable }, tx).AsList();

                // C-8 fix: 检测截断并记录警告，在快照 description 中标记不完整
                var truncations = new List<string>();
                // 使用 >= 作为保守截断判断：当 Count == maxRowsPerTable 时，LIMIT 恰好返回上限行数，
                // 实际可能有更多行未被捕获。此判定可能产生误报（恰好 50000 行的大概率是完整数据），
                // 但漏报的风险更高（大数据集下丢失数据导致 Compare() 产生误导结果）。
                if (materials.Count >= maxRowsPerTable) truncations.Add("Materials");
                if (bomStructures.Count >= maxRowsPerTable) truncations.Add("BomStructures");
                if (bomVersions.Count >= maxRowsPerTable) truncations.Add("BomVersions");
                if (prices.Count >= maxRowsPerTable) truncations.Add("Prices");
                if (inventories.Count >= maxRowsPerTable) truncations.Add("Inventories");

                var isTruncated = truncations.Count > 0;
                if (isTruncated)
                {
                    AppLogger.Warn(
                        $"快照数据被截断（上限 {maxRowsPerTable} 行/表）：{string.Join(", ", truncations)}。" +
                        "后续 Compare() 可能产生误导结果。建议使用数据库级备份。",
                        typeof(SnapshotService));
                }

                // 构建 JSON 快照
                var json = BuildSnapshotJson(materials, bomStructures, bomVersions, prices, inventories);

                var snapshot = new DataSnapshot
                {
                    SnapshotType = Enum.TryParse<SnapshotType>(type, true, out var st) ? st : SnapshotType.Manual,
                    SnapshotData = json,
                    CreatedAt = DateTime.UtcNow,
                    Description = (description ?? $"{type} snapshot at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
                        + (isTruncated ? $" [TRUNCATED: {string.Join(", ", truncations)}]" : "")
                };

                _snapshotRepo.Add(snapshot, conn, tx);
                tx.Commit();
                return snapshot;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public SnapshotComparisonResult Compare(long snapshotIdA, long snapshotIdB)
        {
            var snapA = _snapshotRepo.GetById(snapshotIdA)
                ?? throw new ArgumentException($"Snapshot {snapshotIdA} not found");
            var snapB = _snapshotRepo.GetById(snapshotIdB)
                ?? throw new ArgumentException($"Snapshot {snapshotIdB} not found");

            var result = new SnapshotComparisonResult
            {
                SnapshotIdA = snapshotIdA,
                SnapshotIdB = snapshotIdB
            };

            // 按表名分组比较行数
            var tablesA = ParseSnapshotTables(snapA.SnapshotData);
            var tablesB = ParseSnapshotTables(snapB.SnapshotData);

            var allTables = new HashSet<string>(tablesA.Keys);
            allTables.UnionWith(tablesB.Keys);

            foreach (var table in allTables)
            {
                var countA = tablesA.TryGetValue(table, out var ca) ? ca : 0;
                var countB = tablesB.TryGetValue(table, out var cb) ? cb : 0;
                var diff = countB - countA;

                if (diff > 0)
                    result.AddedCounts[table] = diff;
                else if (diff < 0)
                    result.RemovedCounts[table] = -diff;
                else
                    result.UnchangedCounts[table] = countA;
            }
            return result;
        }

        public void CleanupOldSnapshots(UserRole callerRole, int retentionDays = 90)
        {
            _authz.Demand(callerRole, BomOperation.UserManage); // 清理快照需要管理员权限
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            _snapshotRepo.DeleteOlderThan(cutoff);
        }

        public IEnumerable<DataSnapshot> GetRecent(string? type = null, int limit = 10)
        {
            if (!string.IsNullOrWhiteSpace(type))
                return _snapshotRepo.GetByType(type!, limit);

            // 获取所有类型：先 Daily 再 Manual
            var daily = _snapshotRepo.GetByType("Daily", limit);
            var manual = _snapshotRepo.GetByType("Manual", limit);
            var combined = new List<DataSnapshot>();
            combined.AddRange(daily);
            combined.AddRange(manual);
            combined.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            if (combined.Count > limit)
                combined.RemoveRange(limit, combined.Count - limit);
            return combined;
        }

        private static string BuildSnapshotJson(
            List<Material> materials,
            List<BomNode> bomStructures,
            List<BomVersion> bomVersions,
            List<PriceRecord> prices,
            List<InventoryRecord> inventories)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"capturedAt\": \"" + DateTime.UtcNow.ToString("o") + "\",");

            AppendTable(sb, "Materials", materials, m =>
                $"\"{Escape(m.Code)}\":{{\"id\":{m.Id},\"orgId\":{m.OrgId},\"name\":\"{Escape(m.Name)}\",\"spec\":\"{Escape(m.Spec)}\",\"unit\":\"{Escape(m.Unit)}\",\"category\":\"{Escape(m.Category)}\"}}");

            sb.AppendLine(",");
            AppendTable(sb, "BomStructures", bomStructures, b =>
                $"\"{b.Id}\":{{\"parentId\":{b.ParentMaterialId},\"childId\":{b.ChildMaterialId},\"qty\":{b.Quantity},\"level\":{b.Level},\"state\":\"{b.VersionState}\"}}");

            sb.AppendLine(",");
            AppendTable(sb, "BomVersions", bomVersions, v =>
                $"\"{v.Id}\":{{\"bomId\":{v.BomId},\"version\":{v.VersionNumber},\"state\":\"{v.State}\"}}");

            sb.AppendLine(",");
            AppendTable(sb, "Prices", prices, p =>
                $"\"{p.Id}\":{{\"materialId\":{p.MaterialId},\"price\":{p.UnitPrice},\"currency\":\"{p.Currency}\"}}");

            sb.AppendLine(",");
            AppendTable(sb, "Inventories", inventories, i =>
                $"\"{i.Id}\":{{\"materialId\":{i.MaterialId},\"warehouse\":\"{Escape(i.WarehouseId)}\",\"qty\":{i.Quantity}}}");

            sb.AppendLine();
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendTable<T>(StringBuilder sb, string tableName, List<T> rows, Func<T, string> serializer)
        {
            sb.AppendLine($"  \"{tableName}\": {{");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append("    " + serializer(rows[i]));
                if (i < rows.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.Append("  }");
        }

        // D-1 fix: 委托 AuditService.EscapeJsonString 消除重复实现
        private static string Escape(string? s)
        {
            if (s == null) return "";
            return AuditService.EscapeJsonString(s);
        }

        /// <summary>
        /// 解析快照 JSON，提取每张表的记录数。
        /// 使用字符级状态机 + 大括号深度计数，替代行级启发式匹配 (code-review H-3)。
        /// 不依赖外部 JSON 库（Core 层零 NuGet 依赖约束）。
        /// JSON 格式: {"capturedAt":"...","Materials": { entry, ... }, "BomStructures": { entry, ... }, ...}
        /// </summary>
        private static Dictionary<string, int> ParseSnapshotTables(string snapshotData)
        {
            var result = new Dictionary<string, int>();
            int pos = 0;
            int len = snapshotData.Length;

            // 找到根对象的每个顶层属性，跳过 "capturedAt" 标量值
            while (pos < len)
            {
                // 跳过空白
                SkipWhitespace(snapshotData, ref pos, len);
                if (pos >= len) break;

                // 期望读到 " 开头的属性名
                if (snapshotData[pos] != '"')
                {
                    pos++;
                    continue;
                }

                // 读取属性名
                int nameStart = pos + 1;
                int nameEnd = snapshotData.IndexOf('"', nameStart);
                if (nameEnd < 0) break;
                string propName = snapshotData.Substring(nameStart, nameEnd - nameStart);
                pos = nameEnd + 1;

                // 跳过 ": 和空白
                SkipWhitespace(snapshotData, ref pos, len);
                if (pos < len && snapshotData[pos] == ':') pos++;
                SkipWhitespace(snapshotData, ref pos, len);

                if (pos >= len) break;

                // 标量值（如 "capturedAt"）→ 跳过
                if (snapshotData[pos] == '"')
                {
                    pos++; // skip opening quote
                    while (pos < len && snapshotData[pos] != '"')
                    {
                        if (snapshotData[pos] == '\\') pos++; // skip escaped char
                        pos++;
                    }
                    pos++; // skip closing quote
                    continue;
                }

                // 对象值（表数据）→ 计数字段数
                if (snapshotData[pos] == '{')
                {
                    int entryCount = CountTopLevelFields(snapshotData, ref pos, len);
                    result[propName] = entryCount;
                    continue;
                }

                pos++;
            }

            return result;
        }

        /// <summary>
        /// 从当前位置开始（pos 指向 '{'），统计根对象内的顶层字段数。
        /// 消费整个对象（结束时 pos 指向 '}' 之后）。
        /// </summary>
        private static int CountTopLevelFields(string data, ref int pos, int len)
        {
            int count = 0;
            int depth = 0;
            bool inString = false;

            while (pos < len)
            {
                char c = data[pos];
                pos++;

                if (inString)
                {
                    if (c == '\\') { pos++; continue; } // 跳过转义字符
                    if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        // 深度为 1 时遇到字符串开头 → 这是一个顶层字段
                        if (depth == 1) count++;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        if (depth == 0) return count; // 当前对象结束
                        break;
                }
            }

            return count;
        }

        private static void SkipWhitespace(string data, ref int pos, int len)
        {
            while (pos < len && (data[pos] == ' ' || data[pos] == '\t' || data[pos] == '\n' || data[pos] == '\r' || data[pos] == ','))
                pos++;
        }
    }
}
