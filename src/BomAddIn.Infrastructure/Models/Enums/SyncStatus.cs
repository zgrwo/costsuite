namespace BomAddIn.Infrastructure.Models.Enums
{
    /// <summary>同步状态枚举 — 替代 SyncLog.Status 字符串字段 (code-review L-11)</summary>
    public enum SyncStatus
    {
        /// <summary>等待执行</summary>
        Pending = 0,

        /// <summary>正在执行</summary>
        Running = 1,

        /// <summary>执行完成</summary>
        Complete = 2,

        /// <summary>执行失败</summary>
        Failed = 3
    }
}
