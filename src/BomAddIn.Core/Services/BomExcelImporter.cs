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
            ["ScrapRate"] = new[] { "损耗率", "Scrap Rate", "废品率", "Scrap" },
        };

        public BomExcelImporter(IMaterialRepository materialRepo, IBomNodeRepository bomNodeRepo,
            IDbConnectionFactory connectionFactory, IAuthorizationService authz)
        {
            _materialRepo = materialRepo;
            _bomNodeRepo = bomNodeRepo;
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public ImportResult ImportMaterials(DataTable table, long orgId, UserRole callerRole)
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

                // Skip-bad-rows: commit successfully imported rows, leave errors for caller to review
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        public ImportResult ImportBomStructures(DataTable table, long orgId, UserRole callerRole)
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

                // C-10 fix: 第一阶段 — 验证所有行并收集边（不实际插入），避免插入后回滚浪费
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var rowNum = i + 2;

                    try
                    {
                        var parentCode = GetCell(row, mapping, "ParentItemCode");
                        var childCode = GetCell(row, mapping, "ItemCode");

                        materialLookup.TryGetValue(parentCode, out var parent);
                        materialLookup.TryGetValue(childCode, out var child);

                        if (parent == null) { result.Errors.Add($"第 {rowNum} 行: 父物料 '{parentCode}' 不存在。"); continue; }
                        if (child == null) { result.Errors.Add($"第 {rowNum} 行: 子物料 '{childCode}' 不存在。"); continue; }

                        if (!materialLookup.ContainsKey(parentCode))
                            materialLookup[parentCode] = parent;
                        if (!materialLookup.ContainsKey(childCode))
                            materialLookup[childCode] = child;

                        edges.Add((parent.Id, child.Id));
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"第 {rowNum} 行: {ex.Message}");
                    }
                }

                // 第二阶段：全量循环检测
                if (edges.Count > 0)
                {
                    var cycles = DetectAllCycles(edges);
                    foreach (var cycle in cycles)
                    {
                        result.Errors.Add($"检测到循环依赖: {string.Join(" → ", cycle)}");
                    }
                }

                if (result.Errors.Count > 0)
                {
                    tx.Rollback();
                    result.Success = false;
                    return result;
                }

                // 第三阶段：确认无循环后批量插入
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

                        if (qty <= 0)
                        {
                            result.Errors.Add($"第 {rowNum} 行: 数量 {qty} 无效，必须大于 0。");
                            continue;
                        }

                        materialLookup.TryGetValue(parentCode, out var parent);
                        materialLookup.TryGetValue(childCode, out var child);

                        if (parent == null) { result.Errors.Add($"第 {rowNum} 行: 父物料 '{parentCode}' 不存在。"); continue; }
                        if (child == null) { result.Errors.Add($"第 {rowNum} 行: 子物料 '{childCode}' 不存在。"); continue; }

                        if (!materialLookup.ContainsKey(parentCode))
                            materialLookup[parentCode] = parent;
                        if (!materialLookup.ContainsKey(childCode))
                            materialLookup[childCode] = child;

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

                // Skip-bad-rows: commit successfully imported rows, leave errors for caller to review
                tx.Commit();
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

        // C-11 fix: 迭代 DFS 替代递归，避免深层 BOM 的栈溢出（默认上限 100 层）
        // 使用显式 Stack 模拟调用栈，每帧记录 (node, childEnumerator, isBacktracking)
        private const int MaxCycleDetectionDepth = 100;

        private static void DfsAll(
            long node,
            Dictionary<long, List<long>> graph,
            HashSet<long> visited,
            HashSet<long> inStack,
            List<long> path,
            List<List<long>> allCycles)
        {
            // 迭代 DFS 栈：每帧记录 (node, childIndex, phase: 0=enter, 1=process children)
            var stack = new Stack<(long Node, int ChildIndex, int Phase)>();
            stack.Push((node, 0, 0));

            while (stack.Count > 0)
            {
                // 深度保护：防止极端线性 BOM 路径耗尽内存
                if (stack.Count > MaxCycleDetectionDepth)
                {
                    Infrastructure.Logging.AppLogger.Warn(
                        $"循环检测达到最大深度 {MaxCycleDetectionDepth}，可能存在超深 BOM 结构。",
                        typeof(BomExcelImporter));
                    // E-2 fix: 清理所有 DFS 状态（inStack, path, 以及未完成处理的 visited 节点）
                    // 不清除会导致后续根节点遍历时使用脏 path 产生假阳性环报告
                    while (stack.Count > 0)
                    {
                        var (n, _, _) = stack.Pop();
                        inStack.Remove(n);
                        visited.Remove(n); // 未完成遍历的节点不应标记为 visited
                    }
                    inStack.Clear();
                    path.Clear();
                    return;
                }

                var (current, childIdx, phase) = stack.Pop();

                if (phase == 0)
                {
                    // 进入节点阶段
                    if (inStack.Contains(current))
                    {
                        // 发现环路 — 收集路径中从 current 开始的节点
                        var startIdx = path.IndexOf(current);
                        var cycle = new List<long>(path.GetRange(startIdx, path.Count - startIdx));
                        cycle.Add(current);
                        allCycles.Add(cycle);
                        continue;
                    }
                    if (visited.Contains(current))
                        continue;

                    visited.Add(current);
                    inStack.Add(current);
                    path.Add(current);

                    // 推入回溯帧
                    stack.Push((current, 0, 1));

                    // 推入子节点（逆序推入以保持与递归版本相同的遍历顺序）
                    if (graph.TryGetValue(current, out var children))
                    {
                        for (int i = children.Count - 1; i >= 0; i--)
                        {
                            stack.Push((children[i], 0, 0));
                        }
                    }
                }
                else
                {
                    // 回溯阶段
                    path.RemoveAt(path.Count - 1);
                    inStack.Remove(current);
                }
            }
        }
    }
}
