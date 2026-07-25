using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>数据快照服务 — 全量 + 对比</summary>
    public interface ISnapshotService
    {
        /// <summary>创建快照（Daily/Manual），捕获 5 张核心表的全量数据</summary>
        DataSnapshot CreateSnapshot(UserRole callerRole, string type = "Manual", string? description = null);

        /// <summary>对比两个快照，返回每张表的差异摘要</summary>
        SnapshotComparisonResult Compare(long snapshotIdA, long snapshotIdB);

        /// <summary>清理过期快照（默认保留 90 天）</summary>
        void CleanupOldSnapshots(UserRole callerRole, int retentionDays = 90);

        /// <summary>获取最近的快照列表</summary>
        IEnumerable<DataSnapshot> GetRecent(string? type = null, int limit = 10);
    }

    /// <summary>快照对比结果</summary>
    public class SnapshotComparisonResult
    {
        public long SnapshotIdA { get; set; }
        public long SnapshotIdB { get; set; }
        public Dictionary<string, int> AddedCounts { get; set; } = new();
        public Dictionary<string, int> RemovedCounts { get; set; } = new();
        public Dictionary<string, int> ModifiedCounts { get; set; } = new();
        public Dictionary<string, int> UnchangedCounts { get; set; } = new();
    }
}
