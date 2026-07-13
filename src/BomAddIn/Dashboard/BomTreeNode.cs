using System.Collections.ObjectModel;

namespace BomAddIn.Dashboard
{
    /// <summary>仪表盘 BOM 树节点</summary>
    public class BomTreeNode
    {
        public string DisplayText { get; set; } = "";
        public ObservableCollection<BomTreeNode> Children { get; set; } = new();
    }
}
