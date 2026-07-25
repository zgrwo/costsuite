using System;

namespace BomAddIn.Core.Events
{
    public class DataRefreshedEvent
    {
        /// <summary>数据刷新时间</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>刷新的数据集名称</summary>
        public string DataSetName { get; set; } = string.Empty;
        /// <summary>刷新记录数</summary>
        public int RecordCount { get; set; }
    }
}
