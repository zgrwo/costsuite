using System;

namespace BomAddIn.Core.Events
{
    public class OfflineModeChangedEvent
    {
        /// <summary>状态变更时间</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>是否进入离线模式（ERP 同步暂停）</summary>
        public bool IsOffline { get; set; }
    }
}
