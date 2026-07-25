namespace BomAddIn.Dashboard
{
    /// <summary>仪表盘预警列表项</summary>
    public class AlertItem
    {
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string TriggeredRule { get; set; } = "";
    }
}
