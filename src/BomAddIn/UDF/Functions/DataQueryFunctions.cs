using System;
using System.Linq;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.UDF.Helpers;
using ExcelDna.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UDF.Functions
{
    public static class DataQueryFunctions
    {
        // V1.0 默认 OrgId=1，多组织场景需改为可配置 (code-review L-19)
        private const long DefaultOrgId = 1;

        /// <summary>
        /// =PRICELOOKUP(itemCode, [supplierCode], [asOfDate])
        /// 查询指定物料的最新单价。
        /// </summary>
        [ExcelFunction(Name = "PRICELOOKUP", Description = "查询物料最新单价",
            IsThreadSafe = false, IsVolatile = false)]
        public static object PriceLookup(
            [ExcelArgument("物料编码")] string itemCode,
            [ExcelArgument("供应商编码（可选）")] object? supplierCode = null,
            [ExcelArgument("截止日期（默认今天）")] object? asOfDate = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                    return ExcelError.ExcelErrorNA;

                using var scope = Container.BeginScope();
                var sp = scope.ServiceProvider;
                var materialRepo = sp.GetRequiredService<IMaterialRepository>();
                var material = materialRepo.GetByCode(DefaultOrgId, itemCode);
                if (material == null)
                    return ExcelError.ExcelErrorNA;

                // C-20: 使用 GetLatestByMaterialId 替代 GetHistory(...).ToList().OrderBy(...).First()
                // 减少不必要的全量历史查询和客户端排序
                var priceRepo = sp.GetRequiredService<IPriceRecordRepository>();
                var latest = priceRepo.GetLatestByMaterialId(material.Id);

                if (latest == null)
                    return ExcelError.ExcelErrorNA;

                return Math.Round((double)latest.UnitPrice, 4);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UDF 错误: {ex.Message}", typeof(DataQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }

        /// <summary>
        /// =INVENTORYQTY(itemCode, [warehouseId])
        /// 查询物料当前库存量。
        /// </summary>
        [ExcelFunction(Name = "INVENTORYQTY", Description = "查询物料当前库存量",
            IsThreadSafe = false, IsVolatile = false)]
        public static object InventoryQty(
            [ExcelArgument("物料编码")] string itemCode,
            [ExcelArgument("仓库编码（可选）")] object? warehouseId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                    return ExcelError.ExcelErrorNA;

                using var scope = Container.BeginScope();
                var sp = scope.ServiceProvider;
                var materialRepo = sp.GetRequiredService<IMaterialRepository>();
                var material = materialRepo.GetByCode(DefaultOrgId, itemCode);
                if (material == null)
                    return ExcelError.ExcelErrorNA;

                var inventoryRepo = sp.GetRequiredService<IInventoryRecordRepository>();
                var warehouse = (warehouseId as string) ?? "MAIN";

                // 从数据库获取物料全量快照，在内存中按仓库过滤和排序
                // V1.0 限制: 未在数据库层过滤，大数据量时建议改为 SQL WHERE 子句
                var records = inventoryRepo.GetSnapshot(material.Id, DateTime.UtcNow.Date);
                var match = records
                    .Where(r => r.WarehouseId == warehouse)
                    .OrderByDescending(r => r.SnapshotDate)
                    .FirstOrDefault();

                // M-24: 无数据时返回 #N/A 与其他 UDF 保持一致
                if (match == null)
                    return ExcelError.ExcelErrorNA;

                return match.Quantity;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UDF 错误: {ex.Message}", typeof(DataQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }

        /// <summary>
        /// =ORDERSTATUS(itemCode)
        /// 查询物料的未完成订单数量。
        /// </summary>
        [ExcelFunction(Name = "ORDERSTATUS", Description = "查询物料未完成订单数量",
            IsThreadSafe = false, IsVolatile = false)]
        public static object OrderStatus(
            [ExcelArgument("物料编码")] string itemCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemCode))
                    return ExcelError.ExcelErrorNA;

                using var scope = Container.BeginScope();
                var sp = scope.ServiceProvider;
                var materialRepo = sp.GetRequiredService<IMaterialRepository>();
                var material = materialRepo.GetByCode(DefaultOrgId, itemCode);
                if (material == null)
                    return ExcelError.ExcelErrorNA;

                var orderRepo = sp.GetRequiredService<IOrderRecordRepository>();
                var orders = orderRepo.GetByMaterialDue(material.Id, DateTime.MaxValue).ToList();

                // M-24: 无订单时返回 #N/A 与其他 UDF 保持一致
                if (orders.Count == 0)
                    return ExcelError.ExcelErrorNA;

                // 返回未完成订单的总数量
                return orders.Sum(o => o.OrderQty);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"UDF 错误: {ex.Message}", typeof(DataQueryFunctions));
                return ExcelError.ExcelErrorValue;
            }
        }
    }
}
