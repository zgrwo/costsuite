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
            IsThreadSafe = true, IsVolatile = false)]
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
            IsThreadSafe = true, IsVolatile = false)]
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

                // 数据库层精确查询: 按物料+仓库过滤，取最新一条 (U-3 fix: 消除全量拉取)
                var match = inventoryRepo.GetLatestByMaterialAndWarehouse(material.Id, warehouse);

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
            IsThreadSafe = true, IsVolatile = false)]
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
                // U-4 fix: 传 null 表示“无截止日期过滤”，语义比 DateTime.MaxValue 更清晰
                var orders = orderRepo.GetByMaterialDue(material.Id, dueBefore: null).ToList();

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
