using System;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Models
{
    public class BomVersion
    {
        public long Id { get; set; }
        public long BomId { get; set; }
        public int VersionNumber { get; set; }
        public VersionState State { get; set; } = VersionState.Draft;
        /// <summary>
        /// 审批人/提交人 ID（双用途字段）。
        /// PendingReview 状态时：存储提交人 ID（用于自审批检查）。
        /// Approved/Rejected/Released 等后续状态时：存储执行操作的审批人 ID。
        /// 此约定允许在事务内进行自审批检查（见 ApprovalService.Transition）。
        /// </summary>
        public long? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
