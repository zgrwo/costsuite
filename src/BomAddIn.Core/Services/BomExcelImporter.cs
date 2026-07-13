using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using Dapper;

namespace BomAddIn.Core.Services
{
    /// <summary>BOM Excel 导入器 — 智能列检测 + 数据校验</summary>
    /// <remarks>
    /// 支持中英文表头模糊匹配。DataTable 从 Excel/CSV 文件解析后传入。
    /// 文件解析在 UI 层完成（EPPlus/ExcelDataReader），Core 层只做业务校验。
    /// </remarks>
    public class BomExcelImporter : IBomExcelImporter
    {
        private readonly IMaterialRepository _materialRepo;
        private readonly IBomNodeRepository _bomNodeRepo;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;

        // 中英文列名别名映射（按 skill bom-modeling-patterns §5）
        private static readonly Dictionary<string, string[]> ColumnAliases = new()
        {
            ["ItemCode"] = new[] { "物料编码", "Item Code", "Code", "编码", "料号", "物料代码" },
            ["Name"] = new[] { "物料名称", "Name", "名称", "描述", "Description", "Desc" },
            ["Quantity"] = new[] { "数量", "Quantity", "Qty", "用量", "单位用量" },
            ["Unit"] = new[] { "单位", "Unit", "UOM", "计量单位" },
            ["Spec"] = new[] { "规格", "Spec", "型号", "Specification", "规格型号" },
            ["Category"] = new[] { "类别", "Category", "分类", "物料类别", "Type" },
            ["Level"] = new[] { "层级", "Level", "BOM Level", "BOM层级" },
            ["ParentItemCode"] = new[] { "父项编码", "Parent Code", "Parent", "上层编码", "父物料" },
            ["Position"] = new[] { "位号", "Position", "工位", "参考标识符", "RefDes" },
        };

        public BomExcelImporter(IMaterialRepository materialRepo, IBomNodeRepository bomNodeRepo,
            IDbConnectionFactory connectionFactory, IAuthorizationService authz)
        {
            _materialRepo = materialRepo;
            _bomNodeRepo = bomNodeRepo;
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public ImportResult ImportMaterials(DataTable table, long orgId, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.MaterialCreate);
            var result = new ImportResult { RowCount = table.Rows.Count };

            // 1. 检测列映射
            var headers = new string[table.Columns.Count];
            for (int i = 0; i < table.Columns.Count; i++) headers[i] = table.Columns[i].ColumnName;

            var mapping = DetectColumnMapping(headers);

            if (!mapping.ContainsKey("ItemCode"))
                return ImportResult.Fail("未找到物料编码列。请检查表头是否包含：物料编码、Item Code、Code");

            // 2. 逐行校验 + 导入（事务保护）
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNum = i + 2;

                    try
                    {
                        var code = GetCell(row, mapping, "ItemCode");
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            result.Warnings.Add($"第 {rowNum} 行: 物料编码为空，已跳过。");
                            continue;
                        }

                        // H-14: 使用共享连接+事务查询重复
                        var existing = _materialRepo.GetByCode(orgId, code, conn, tx);
                        if (existing != null)
                        {
                            result.Warnings.Add($"第 {rowNum} 行: 物料编码 '{code}' 已存在，已跳过。");
                            continue;
                        }

                        var material = new Material
                        {
                            OrgId = orgId,
                            Code = code,
                            Name = GetCell(row, mapping, "Name", code),
                            Spec = GetCell(row, mapping, "Spec", ""),
                            Unit = GetCell(row, mapping, "Unit", "PCS"),
                            Category = GetCell(row, mapping, "Category", ""),
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _materialRepo.Add(material, conn, tx);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"第 {rowNum} 行: {ex.Message}");
                    }
                }

                if (result.Errors.Count == 0)
                    tx.Commit();
                else
                    tx.Rollback();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public ImportResult ImportBomStructures(DataTable table, long orgId, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.BomCreate);

            var result = new ImportResult { RowCount = table.Rows.Count };

            var headers = new string[table.Columns.Count];
            for (int i = 0; i < table.Columns.Count; i++) headers[i] = table.Columns[i].ColumnName;

            var mapping = DetectColumnMapping(headers);

            if (!mapping.ContainsKey("ParentItemCode"))
                return ImportResult.Fail("未找到父项编码列。请检查表头是否包含：父项编码、Parent Code");
            if (!mapping.ContainsKey("ItemCode"))
                return ImportResult.Fail("未找到子项编码列。请检查表头是否包含：物料编码、Item Code");

            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                // R2-16: 预先批量查询所有涉及的物料编码，构建内存 code→id 映射
                // 避免循环检测阶段逐行调用 GetByCode（O(2N) 次 SQL → O(1) 次 SQL）
                var allCodes = new HashSet<string>();
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var parentCode = GetCell(table.Rows[i], mapping, "ParentItemCode");
                    var childCode = GetCell(table.Rows[i], mapping, "ItemCode");
                    if (!string.IsNullOrWhiteSpace(parentCode)) allCodes.Add(parentCode);
                    if (!string.IsNullOrWhiteSpace(childCode)) allCodes.Add(childCode);
                }

                // R2-16: 批量查询物料编码→ID。若返回 null 则回退至空字典（后续逐行 GetByCode）
                var materialLookup = allCodes.Count > 0
                    ? _materialRepo.GetByCodes(orgId, allCodes, conn, tx) ?? new Dictionary<string, Material>()
                    : new Dictionary<string, Material>();

                // 循环检测边收集
                var edges = new List<(long Parent, long Child)>();

                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNum = i + 2;

                    try
                    {
                        var parentCode = GetCell(row, mapping, "ParentItemCode");
                        var childCode = GetCell(row, mapping, "ItemCode");
                        var qtyStr = GetCell(row, mapping, "Quantity", "1");

                        if (!double.TryParse(qtyStr, out var qty))
                        {
                            result.Errors.Add($"第 {rowNum} 行: 数量 '{qtyStr}' 不是有效数字。");
                            continue;
                        }

                        // M-7: 拒绝零或负数用量
                        if (qty <= 0)
                        {
                            result.Errors.Add($"第 {rowNum} 行: 数量 {qty} 无效，必须大于 0。");
                            continue;
                        }

                        // R2-15: 外键校验使用事务内连接 + 内存查找，确保读取事务中刚插入的物料
                        Material? parent = null;
                        if (!materialLookup.TryGetValue(parentCode, out var p))
                            parent = _materialRepo.GetByCode(orgId, parentCode, conn, tx);
                        else
                            parent = p;

                        Material? child = null;
                        if (!materialLookup.TryGetValue(childCode, out var c))
                            child = _materialRepo.GetByCode(orgId, childCode, conn, tx);
                        else
                            child = c;

                        if (parent == null) { result.Errors.Add($"第 {rowNum} 行: 父物料 '{parentCode}' 不存在。"); continue; }
                        if (child == null) { result.Errors.Add($"第 {rowNum} 行: 子物料 '{childCode}' 不存在。"); continue; }

                        // 更新内存查找缓存（如果是新发现的物料）
                        if (!materialLookup.ContainsKey(parentCode))
                            materialLookup[parentCode] = parent;
                        if (!materialLookup.ContainsKey(childCode))
                            materialLookup[childCode] = child;

                        // R2-17: 收集所有边，在插入前进行全量循环检测
                        edges.Add((parent.Id, child.Id));

                        var node = new BomNode
                        {
                            OrgId = orgId,
                            ParentMaterialId = parent.Id,
                            ChildMaterialId = child.Id,
                            Quantity = qty,
                            Position = GetCell(row, mapping, "Position", ""),
                            Level = int.TryParse(GetCell(row, mapping, "Level", "0"), out var l) ? l : 0,
                            ValidFrom = DateTime.UtcNow,
                            ValidTo = null,
                            VersionState = Infrastructure.Models.Enums.VersionState.Draft,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _bomNodeRepo.Add(node, conn, tx);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"第 {rowNum} 行: {ex.Message}");
                    }
                }

                // R2-17: 全量循环检测 — 报告所有环，而非仅第一个
                if (edges.Count > 0)
                {
                    var cycles = DetectAllCycles(edges);
                    foreach (var cycle in cycles)
                    {
                        result.Errors.Add($"检测到循环依赖: {string.Join(" → ", cycle)}");
                    }
                }

                if (result.Errors.Count == 0)
                    tx.Commit();
                else
                    tx.Rollback();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public Dictionary<string, string> DetectColumnMapping(string[] headers)
        {
            var mapping = new Dictionary<string, string>();

            foreach (var header in headers)
            {
                var normalized = header.Trim();
                foreach (var kv in ColumnAliases)
                {
                    if (kv.Value.Any(alias =>
                        string.Equals(normalized, alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        mapping[kv.Key] = header; // 保留原始列名用于 DataTable 索引
                        break;
                    }
                }
            }

            return mapping;
        }

        private static string GetCell(DataRow row, Dictionary<string, string> mapping, string key, string defaultValue = "")
        {
            if (!mapping.TryGetValue(key, out var colName)) return defaultValue;
            var val = row[colName]?.ToString()?.Trim() ?? defaultValue;
            return string.IsNullOrEmpty(val) ? defaultValue : val;
        }

        /// <summary>DFS 循环依赖检测 — 检测 BOM 有向图中所有环 (R2-17)</summary>
        /// <returns>所有检测到的环，每个环是物料 ID 序列</returns>
        private static List<List<long>> DetectAllCycles(List<(long Parent, long Child)> edges)
        {
            var graph = new Dictionary<long, List<long>>();
            foreach (var (parent, child) in edges)
            {
                if (!graph.ContainsKey(parent))
                    graph[parent] = new List<long>();
                graph[parent].Add(child);
                if (!graph.ContainsKey(child))
                    graph[child] = new List<long>();
            }

            var allCycles = new List<List<long>>();
            var visited = new HashSet<long>();
            var inStack = new HashSet<long>();
            var path = new List<long>();

            foreach (var node in graph.Keys)
            {
                DfsAll(node, graph, visited, inStack, path, allCycles);
            }
            return allCycles;
        }

        private static void DfsAll(
            long node,
            Dictionary<long, List<long>> graph,
            HashSet<long> visited,
            HashSet<long> inStack,
            List<long> path,
            List<List<long>> allCycles)
        {
            if (inStack.Contains(node))
            {
                var startIdx = path.IndexOf(node);
                var cycle = new List<long>(path.GetRange(startIdx, path.Count - startIdx));
                cycle.Add(node);
                allCycles.Add(cycle);
                return;
            }
            if (visited.Contains(node))
                return;

            visited.Add(node);
            inStack.Add(node);
            path.Add(node);

            if (graph.TryGetValue(node, out var children))
            {
                foreach (var child in children)
                {
                    DfsAll(child, graph, visited, inStack, path, allCycles);
                }
            }

            path.RemoveAt(path.Count - 1);
            inStack.Remove(node);
        }
    }
}
