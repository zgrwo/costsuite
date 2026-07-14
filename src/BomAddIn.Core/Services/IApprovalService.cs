using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>BOM 审批工作流 — 状态机：Draft → PendingReview → Approved/Rejected → Released → Obsolete。所有变更方法需传入 UserRole 进行 RBAC 检查。</summary>
    public interface IApprovalService
    {
        /// <summary>提交审批 Draft → PendingReview</summary>
        BomVersion SubmitForReview(long versionId, UserRole callerRole, long? userId = null);

        /// <summary>审批通过 PendingReview → Approved</summary>
        BomVersion Approve(long versionId, long userId, UserRole callerRole, string? comment = null);

        /// <summary>审批拒绝 PendingReview → Rejected</summary>
        BomVersion Reject(long versionId, long userId, UserRole callerRole, string comment);

        /// <summary>重新提交 Rejected → Draft</summary>
        BomVersion Resubmit(long versionId, UserRole callerRole, long? userId = null);

        /// <summary>发布版本 Approved → Released</summary>
        BomVersion Release(long versionId, UserRole callerRole, long? userId = null);

        /// <summary>废弃版本 Released → Obsolete</summary>
        BomVersion Obsolete(long versionId, UserRole callerRole, long? userId = null);

        /// <summary>验证状态转换是否合法</summary>
        bool IsValidTransition(VersionState from, VersionState to);

        /// <summary>获取版本审批历史（通过审计日志）</summary>
        IEnumerable<Infrastructure.Models.AuditLog> GetHistory(long versionId);
    }
}
