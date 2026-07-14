namespace BomAddIn.Infrastructure.Models.Enums
{
    /// <summary>审计操作类型 — 对应 AuditLogs.Action 列</summary>
    public enum AuditAction
    {
        Create = 0,
        Update = 1,
        Delete = 2,
        Approve = 3,
        Reject = 4,
        StateChange = 5
    }
}
