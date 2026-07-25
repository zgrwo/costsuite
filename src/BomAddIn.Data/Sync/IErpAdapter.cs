using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Sync
{
    /// <summary>ERP 数据适配器接口 — 适配器模式，支持多种 ERP 系统</summary>
    public interface IErpAdapter
    {
        Task<IEnumerable<Material>> PullMaterialsAsync(DateTime? since = null);
        Task<IEnumerable<PriceRecord>> PullPricesAsync(DateTime? since = null);
        Task<IEnumerable<InventoryRecord>> PullInventoriesAsync(DateTime? since = null);
        Task<IEnumerable<OrderRecord>> PullOrdersAsync(DateTime? since = null);
        Task<IEnumerable<CapacityRecord>> PullCapacitiesAsync(DateTime? since = null);
        Task<bool> TestConnectionAsync();
    }
}
