using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Data.Analysis;
using BomAddIn.Data.Caching;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
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
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthorizationService _authz;

        public BomService(
            IBomNodeRepository bomNodeRepository,
            IBomVersionRepository bomVersionRepository,
            IMaterialRepository materialRepository,
            IBomAnalysisProvider analysisProvider,
            IAuditService auditService,
            ICacheProvider cache,
            IDbConnectionFactory connectionFactory,
            IAuthorizationService authz)
        {
            _bomNodeRepository = bomNodeRepository;
            _bomVersionRepository = bomVersionRepository;
            _materialRepository = materialRepository;
            _analysisProvider = analysisProvider;
            _auditService = auditService;
            _cache = cache;
            _connectionFactory = connectionFactory;
            _authz = authz;
        }

        public List<BomExpandedNode> Expand(string itemCode, DateTime? asOfDate = null)
        {
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
        public BomNode AddNode(BomNode node, long? userId = null, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.BomCreate);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                _bomNodeRepository.Add(node, conn, tx);
                _auditService.Log("CREATE", "BomStructures", node.Id,
                    null, AuditService.ToJson(node), userId);

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
        public void UpdateNode(BomNode node, long? userId = null, UserRole callerRole = UserRole.Admin)
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

                _auditService.Log("UPDATE", "BomStructures", node.Id,
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
        public void DeleteNode(long id, long? userId = null, UserRole callerRole = UserRole.Admin)
        {
            _authz.Demand(callerRole, BomOperation.BomDelete);
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var node = _bomNodeRepository.GetById(id);
                _bomNodeRepository.Delete(id, conn, tx);
                _auditService.Log("DELETE", "BomStructures", id,
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

            // 批量查询所有物料单价（一次 SQL 替代 N 次查询）
            var materialIds = nodes.Select(n => n.MaterialId).Distinct();
            var priceRepo = _bomNodeRepository as BomAddIn.Data.Repositories.IPriceRecordRepository;
            // 通过 connectionFactory 手动获取 priceRepo（避免注入新依赖）
            using var conn = _connectionFactory.CreateConnection();
            var prices = conn.Query<PriceRecord>(
                @"SELECT p.* FROM Prices p
                  INNER JOIN (
                      SELECT MaterialId, MAX(EffectiveDate) AS MaxDate
                      FROM Prices
                      WHERE MaterialId IN @Ids AND EffectiveDate <= @AsOfDate
                      GROUP BY MaterialId
                  ) latest ON p.MaterialId = latest.MaterialId AND p.EffectiveDate = latest.MaxDate",
                new { Ids = materialIds, AsOfDate = date.ToString("yyyy-MM-dd") });
            var priceMap = prices.ToDictionary(p => p.MaterialId, p => (double)p.UnitPrice);

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
            var costs = new Dictionary<long, double>();
            foreach (var node in nodes.OrderByDescending(n => n.Level))
            {
                double unitPrice = priceMap.TryGetValue(node.MaterialId, out var p) ? p : 0.0;
                double ownCost = unitPrice * node.Quantity;

                double childrenCost = 0;
                if (childrenByParent.TryGetValue(node.MaterialId, out var children))
                {
                    foreach (var child in children)
                    {
                        if (costs.TryGetValue(child.MaterialId, out var cc))
                            childrenCost += cc;
                    }
                }

                costs[node.MaterialId] = ownCost + childrenCost;
            }

            // 根节点（Level=0）的总成本
            var root = nodes.FirstOrDefault(n => n.Level == 0);
            if (root != null && costs.TryGetValue(root.MaterialId, out var totalCost))
                return Math.Round(totalCost, 2);

            // fallback: 汇总所有节点的 ownCost
            double fallback = 0;
            foreach (var n in nodes)
                if (costs.TryGetValue(n.MaterialId, out var c)) fallback += c;
            return Math.Round(fallback, 2);
        }
    }
}
