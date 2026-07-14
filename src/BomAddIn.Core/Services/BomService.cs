using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using Dapper;

namespace BomAddIn.Core.Services
{
    /// <summary>BOM 服务 — DuckDB 展开 + 版本管理 + 审计 + 缓存</summary>
    public class BomService : IBomService
    {
        private readonly IBomNodeRepository _bomNodeRepository;
        private readonly IBomVersionRepository _bomVersionRepository;
        private readonly IMaterialRepository _materialRepository;
        private readonly IBomAnalysisProvider _analysisProvider;
        private readonly IAuditService _auditService;
        private readonly ICacheProvider _cache;
        private readonly IPriceRecordRepository _priceRecordRepo;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;

        public BomService(
            IBomNodeRepository bomNodeRepository,
            IBomVersionRepository bomVersionRepository,
            IMaterialRepository materialRepository,
            IBomAnalysisProvider analysisProvider,
            IAuditService auditService,
            ICacheProvider cache,
            IPriceRecordRepository priceRecordRepo,
            IDbConnectionFactory connectionFactory,
            IAuthorizationService authz)
        {
            _bomNodeRepository = bomNodeRepository;
            _bomVersionRepository = bomVersionRepository;
            _materialRepository = materialRepository;
            _analysisProvider = analysisProvider;
            _auditService = auditService;
            _cache = cache;
            _priceRecordRepo = priceRecordRepo;
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public List<BomExpandedNode> Expand(string itemCode, DateTime? asOfDate = null)
        {
            if (itemCode == null) throw new ArgumentNullException(nameof(itemCode));

            var date = (asOfDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            var cacheKey = $"bom_expand:{itemCode}:{date}";

            // 查缓存
            var cached = _cache.Get<List<BomExpandedNode>>(cacheKey);
            if (cached != null)
                return cached;

            // 缓存未命中 → DuckDB 展开
            var nodes = _analysisProvider.ExpandBom(itemCode, asOfDate);

            // 写入缓存（TTL = 5 分钟）
            _cache.Set(cacheKey, nodes, TimeSpan.FromMinutes(5));

            return nodes;
        }

        public BomNode? GetById(long id)
        {
            return _bomNodeRepository.GetById(id);
        }

        public IEnumerable<BomNode> GetChildren(long parentMaterialId, DateTime? asOfDate = null)
        {
            return _bomNodeRepository.GetChildren(parentMaterialId, asOfDate);
        }

        /// <summary>
        /// 添加 BOM 节点 — 共享连接+事务保证原子性 (code-review C-13)。
        /// 数据写入成功后清除 BOM 展开缓存。
        /// </summary>
        public BomNode AddNode(BomNode node, UserRole callerRole, long? userId = null)
        {
            _authz.Demand(callerRole, BomOperation.BomCreate);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                _bomNodeRepository.Add(node, conn, tx);

                TryLogAudit(AuditAction.Create, node.Id, null, AuditService.ToJson(node), userId);

                tx.Commit();
                _cache.RemoveByPrefix("bom_expand:");
                return node;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 更新 BOM 节点 — 共享连接+事务+原子版本号 (code-review C-12, C-13)。
        /// </summary>
        public void UpdateNode(BomNode node, UserRole callerRole, long? userId = null)
        {
            _authz.Demand(callerRole, BomOperation.BomUpdate);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var oldNode = _bomNodeRepository.GetById(node.Id);
                _bomNodeRepository.Update(node, conn, tx);

                // 原子版本号：使用 Repository 获取最新版号+1 (code-review C-12)
                var latest = _bomVersionRepository.GetLatest(node.Id);
                var nextVersion = (latest?.VersionNumber ?? 0) + 1;

                _bomVersionRepository.Add(new BomVersion
                {
                    BomId = node.Id,
                    VersionNumber = nextVersion,
                    State = VersionState.Draft,
                    CreatedAt = DateTime.UtcNow
                });

                TryLogAudit(AuditAction.Update, node.Id,
                    oldNode != null ? AuditService.ToJson(oldNode) : null,
                    AuditService.ToJson(node), userId);

                tx.Commit();
                _cache.RemoveByPrefix("bom_expand:");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 删除 BOM 节点 — 共享连接+事务 (code-review C-13)。
        /// </summary>
        public void DeleteNode(long id, UserRole callerRole, long? userId = null)
        {
            _authz.Demand(callerRole, BomOperation.BomDelete);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var node = _bomNodeRepository.GetById(id);
                _bomNodeRepository.Delete(id, conn, tx);
                TryLogAudit(AuditAction.Delete, id,
                    node != null ? AuditService.ToJson(node) : null, null, userId);

                tx.Commit();
                _cache.RemoveByPrefix("bom_expand:");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public IEnumerable<BomVersion> GetVersionHistory(long bomId)
        {
            return _bomVersionRepository.GetByBomId(bomId);
        }

        /// <summary>
        /// 计算物料完整 BOM 汇总成本 — 自底向上 Quantity×UnitPrice 汇总 (U-1 fix)。
        /// 从 UDF 层提取到 Core Service，遵循"UI薄壳、逻辑厚"原则。
        /// </summary>
        public double CalculateCost(string itemCode, DateTime? asOfDate = null)
        {
            var date = asOfDate ?? DateTime.Today;
            var nodes = Expand(itemCode, date);

            if (nodes.Count == 0)
                return 0;

            // 批量查询所有物料单价（通过 IPriceRecordRepository 替代原始 Dapper）
            var materialIds = nodes.Select(n => n.MaterialId).Distinct();
            var priceMap = _priceRecordRepo.GetByMaterialIdsAndDate(materialIds, date)
                .ToDictionary(p => p.Key, p => (double)p.Value.UnitPrice);

            // 预构建 parent→children 索引（O(n)）
            var childrenByParent = new Dictionary<long, List<BomExpandedNode>>();
            foreach (var node in nodes)
            {
                if (node.ParentMaterialId.HasValue)
                {
                    var parentId = node.ParentMaterialId.Value;
                    if (!childrenByParent.ContainsKey(parentId))
                        childrenByParent[parentId] = new List<BomExpandedNode>();
                    childrenByParent[parentId].Add(node);
                }
            }

            // 自底向上成本汇总（O(n)）
            // C-1 fix: 使用节点引用作为字典键（而非 MaterialId），避免同物料多出现时成本覆盖
            // 同一物料在 BOM 中多处出现（不同父节点），各为独立节点，拥有独立成本子树
            //
            // ⚠️ G-1 note: Dictionary<BomExpandedNode,double> 依赖引用相等（BomExpandedNode 未重写 Equals）。
            // costs[node] 和 costs[child] 使用来自同一 Expand() 返回列表的同一对象实例 → 安全。
            // 如未来 Expand() 返回新实例（克隆/投影/反序列化），需改用复合键或为 BomExpandedNode 添加 identity 列。
            string NodeKey(BomExpandedNode n) => $"{n.ItemCode}|{n.ParentMaterialId}|{n.Level}|{n.MaterialId}";
            var costs = new Dictionary<string, double>();
            foreach (var node in nodes.OrderByDescending(n => n.Level))
            {
                double unitPrice = priceMap.TryGetValue(node.MaterialId, out var p) ? p : 0.0;
                double ownCost = unitPrice * node.Quantity;

                double childrenCost = 0;
                if (childrenByParent.TryGetValue(node.MaterialId, out var children))
                {
                    foreach (var child in children)
                    {
                        if (costs.TryGetValue(NodeKey(child), out var cc))
                            childrenCost += cc;
                    }
                }

                costs[NodeKey(node)] = ownCost + childrenCost;
            }

            // 根节点（Level=0）的总成本
            var root = nodes.FirstOrDefault(n => n.Level == 0);
            if (root != null && costs.TryGetValue(NodeKey(root), out var totalCost))
                return Math.Round(totalCost, 2);

            // C-2 fix: fallback 只汇总顶层节点（未被任何其他节点作为子节点引用的节点）
            // costs[node] 已包含节点自身成本 + 所有子树成本，汇总全部节点会双重计数
            var childMaterialIds = new HashSet<long>(
                nodes.Where(n => n.ParentMaterialId.HasValue).Select(n => n.MaterialId));
            double fallback = 0;
            foreach (var n in nodes)
                if (!childMaterialIds.Contains(n.MaterialId))
                    if (costs.TryGetValue(NodeKey(n), out var c))
                        fallback += c;
            return Math.Round(fallback, 2);
        }

        // H-3 fix: 提取审计日志 try/catch 辅助方法，消除 3 处重复代码
        private void TryLogAudit(AuditAction action, long? recordId, string? oldValues, string? newValues, long? userId)
        {
            try
            {
                _auditService.Log(action, "BomStructures", recordId, oldValues, newValues, userId);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"审计日志写入失败 ({action} BomStructures): {ex.Message}", typeof(BomService));
            }
        }
    }
}
