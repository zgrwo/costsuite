using System;
using System.Threading.Tasks;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>ERP 数据同步服务接口</summary>
    public interface ISyncService
    {
        /// <summary>执行全量同步（Materials + Prices + Inventories + Orders + Capacities）</summary>
        /// <param name="callerRole">调用者角色，用于 RBAC 权限检查</param>
        Task<SyncResult> SyncAllAsync(UserRole callerRole);

        /// <summary>获取上次同步时间</summary>
        DateTime? GetLastSyncTime();
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
