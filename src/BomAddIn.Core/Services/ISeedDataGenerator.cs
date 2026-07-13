using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>种子数据生成器 — 批量生成物料和 BOM 数据用于性能测试</summary>
    public interface ISeedDataGenerator
    {
        /// <summary>检查是否已有种子数据</summary>
        bool HasSeedData();

        /// <summary>
        /// 生成种子数据（幂等：已存在则跳过）。
        /// </summary>
        /// <param name="materialCount">物料总数</param>
        /// <param name="bomNodeCount">BOM 节点总数</param>
        /// <param name="historyMonths">价格/库存历史月份数</param>
        /// <param name="callerRole">调用者角色，仅限 Admin</param>
        /// <returns>实际生成的行数统计</returns>
        SeedResult Generate(int materialCount = 10000, int bomNodeCount = 50000, int historyMonths = 12, UserRole callerRole = UserRole.Admin);
    }

    public class SeedResult
    {
        public int MaterialsCreated { get; set; }
        public int BomNodesCreated { get; set; }
        public int PriceRecordsCreated { get; set; }
        public int InventoryRecordsCreated { get; set; }
        public bool Skipped { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
