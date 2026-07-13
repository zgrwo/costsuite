using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Infrastructure.Logging;

namespace BomAddIn.Core.Services
{
    /// <summary>审批工作流 — 强制状态转换规则 + 审计记录</summary>
    public class ApprovalService : IApprovalService
    {
        private readonly IBomVersionRepository _versionRepo;
        private readonly IAuditService _auditService;
        private readonly IDbConnectionFactory _connectionFactory;

        // 合法状态转换表
        private static readonly Dictionary<VersionState, HashSet<VersionState>> ValidTransitions =
            new Dictionary<VersionState, HashSet<VersionState>>
            {
                { VersionState.Draft,          new HashSet<VersionState> { VersionState.PendingReview, VersionState.Obsolete } },
                { VersionState.PendingReview,  new HashSet<VersionState> { VersionState.Approved, VersionState.Rejected, VersionState.Obsolete } },
                { VersionState.Approved,       new HashSet<VersionState> { VersionState.Released, VersionState.Obsolete } },
                { VersionState.Rejected,       new HashSet<VersionState> { VersionState.Draft, VersionState.Obsolete } },
                { VersionState.Released,       new HashSet<VersionState> { VersionState.Obsolete } },
                { VersionState.Obsolete,       new HashSet<VersionState>() } // 终态
            };

        public ApprovalService(IBomVersionRepository versionRepo, IAuditService auditService,
            IDbConnectionFactory connectionFactory)
        {
            _versionRepo = versionRepo;
            _auditService = auditService;
            _connectionFactory = connectionFactory;
        }

        public BomVersion SubmitForReview(long versionId, long? userId = null)
        {
            return Transition(versionId, VersionState.PendingReview, userId);
        }

        public BomVersion Approve(long versionId, long userId, string? comment = null)
        {
            // H-17: 验证审批人ID有效
            if (userId <= 0)
                throw new ArgumentException("审批人 ID 不能为空或无效。", nameof(userId));

            // C-5 fix: 自我审批检查已移入 Transition 事务内，消除 TOCTOU 窗口
            var result = Transition(versionId, VersionState.Approved, userId, checkSelfApproval: true);
            // 审计记录尽力而为，失败不回滚
            try
            {
                _auditService.Log("APPROVE", "BomVersions", versionId,
                    null, comment != null ? AuditService.ToJson(new { Comment = comment }) : null, userId);
            }
            catch (Exception ex) { Infrastructure.Logging.AppLogger.Warn($"审计日志写入失败 (APPROVE): {ex.Message}", typeof(ApprovalService)); }
            return result;
        }

        public BomVersion Reject(long versionId, long userId, string? comment = null)
        {
            var version = Transition(versionId, VersionState.Rejected, userId);
            _auditService.Log("REJECT", "BomVersions", versionId,
                null, comment != null ? AuditService.ToJson(new { Comment = comment }) : null, userId);
            return version;
        }

        public BomVersion Resubmit(long versionId, long? userId = null)
        {
            return Transition(versionId, VersionState.Draft, userId);
        }

        public BomVersion Release(long versionId, long? userId = null)
        {
            return Transition(versionId, VersionState.Released, userId);
        }

        public BomVersion Obsolete(long versionId, long? userId = null)
        {
            return Transition(versionId, VersionState.Obsolete, userId);
        }

        public bool IsValidTransition(VersionState from, VersionState to)
        {
            return ValidTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
        }

        public IEnumerable<Infrastructure.Models.AuditLog> GetHistory(long versionId)
        {
            return _auditService.GetTableHistory("BomVersions", versionId);
        }

        private BomVersion Transition(long versionId, VersionState targetState, long? userId,
            bool checkSelfApproval = false)
        {
            // B-4 fix: 在事务内读取版本状态，消除 TOCTOU 竞态条件
            using var conn = _connectionFactory.CreateConnection();
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                var version = _versionRepo.GetById(versionId, conn, tx)
                    ?? throw new InvalidOperationException($"BomVersion Id={versionId} not found.");

                var oldState = version.State;

                // C-5 fix: 自我审批检查移入事务内，读取事务内最新状态，消除 TOCTOU 窗口
                // ApprovedBy 字段在 PendingReview 状态时存储提交人 ID（用于此检查）
                if (checkSelfApproval && targetState == VersionState.Approved
                    && version.ApprovedBy.HasValue && version.ApprovedBy.Value == userId)
                    throw new InvalidOperationException("不能自行审批：审批人与提交人相同。");

                if (!IsValidTransition(oldState, targetState))
                    throw new InvalidOperationException(
                        $"Invalid state transition: {oldState} → {targetState}. " +
                        $"Valid targets from {oldState}: {string.Join(", ", GetValidTargets(oldState))}");

                _versionRepo.UpdateState(versionId, targetState, userId, conn, tx);

                // 记录审计日志（在同一事务内）
                _auditService.Log("STATE_CHANGE", "BomVersions", conn, tx,
                    versionId,
                    AuditService.ToJson(new { oldState = oldState.ToString() }),
                    AuditService.ToJson(new { newState = targetState.ToString(), userId }),
                    userId);

                tx.Commit();

                // 返回更新后的版本
                return _versionRepo.GetById(versionId)!;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static IEnumerable<string> GetValidTargets(VersionState from)
        {
            return ValidTransitions.TryGetValue(from, out var targets)
                ? targets.Select(t => t.ToString())
                : Enumerable.Empty<string>();
        }
    }
}
