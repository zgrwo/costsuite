using System;

namespace BomAddIn.Core.Events
{
    public class SyncCompletedEvent
    {
        /// <summary>同步完成时间</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>是否成功</summary>
        public bool Success { get; set; }
        /// <summary>错误信息（如有）</summary>
        public string? ErrorMessage { get; set; }
        /// <summary>同步记录总数</summary>
        public int TotalRecords { get; set; }
    }
}
