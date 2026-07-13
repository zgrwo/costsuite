using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BomAddIn.Data.Connection;
using BomAddIn.Infrastructure.Models;
using Dapper;

namespace BomAddIn.Data.Sync
{
    /// <summary>模拟 ERP 适配器 — V1.0 从本地 SQLite 读取作为"ERP 数据源"</summary>
    /// <remarks>
    /// 真实 ERP 适配器只需实现 IErpAdapter 接口即可替换。
    /// 此模拟适配器用于验证同步流程（Pull → Write → SyncLog）。
    /// </remarks>
    public class SimulatedErpAdapter : IErpAdapter
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public SimulatedErpAdapter(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public Task<IEnumerable<Material>> PullMaterialsAsync(DateTime? since = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Materials WHERE IsActive = 1";
            if (since.HasValue) sql += " AND (CreatedAt > @Since OR UpdatedAt > @Since)";
            var result = conn.Query<Material>(sql, new { Since = since?.ToString("o") });
            return Task.FromResult(result);
        }

        public Task<IEnumerable<PriceRecord>> PullPricesAsync(DateTime? since = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Prices";
            if (since.HasValue) sql += " WHERE CreatedAt > @Since";
            var result = conn.Query<PriceRecord>(sql, new { Since = since?.ToString("o") });
            return Task.FromResult(result);
        }

        public Task<IEnumerable<InventoryRecord>> PullInventoriesAsync(DateTime? since = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Inventories";
            if (since.HasValue) sql += " WHERE CreatedAt > @Since";
            var result = conn.Query<InventoryRecord>(sql, new { Since = since?.ToString("o") });
            return Task.FromResult(result);
        }

        public Task<IEnumerable<OrderRecord>> PullOrdersAsync(DateTime? since = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Orders";
            if (since.HasValue) sql += " WHERE CreatedAt > @Since";
            var result = conn.Query<OrderRecord>(sql, new { Since = since?.ToString("o") });
            return Task.FromResult(result);
        }

        public Task<IEnumerable<CapacityRecord>> PullCapacitiesAsync(DateTime? since = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            var sql = "SELECT * FROM Capacities";
            if (since.HasValue) sql += " WHERE CreatedAt > @Since";
            var result = conn.Query<CapacityRecord>(sql, new { Since = since?.ToString("o") });
            return Task.FromResult(result);
        }

        public Task<bool> TestConnectionAsync()
        {
            // 模拟 ERP 连通性
            return Task.FromResult(true);
        }
    }
}
