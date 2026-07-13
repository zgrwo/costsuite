using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.UDF;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.Dashboard
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly IServiceProvider _services;
        private string _statusText = "就绪";
        private int _materialCount;
        private int _activeBomCount;
        private int _alertCount;
        private string _lastSyncText = "从未同步";
        private string _bomSearchCode = "";
        private ObservableCollection<AlertItem> _alerts = new();
        private ObservableCollection<BomTreeNode> _bomTreeNodes = new();

        public DashboardViewModel(IServiceProvider? services = null)
        {
            _services = services ?? BomAddInStartup.ServiceProvider;

            // 命令
            RefreshCommand = new RelayCommand(_ => RefreshAll());
            ExpandBomCommand = new RelayCommand(async _ => await ExpandBomAsync());
            SyncNowCommand = new RelayCommand(async _ => await SyncNowAsync());

            // H-24: 延迟加载 — 构造函数不再做同步 DB 查询，在 Window.Loaded 中异步触发
        }

        /// <summary>
        /// 窗口加载完成后异步刷新数据，避免阻塞 UI 线程 (code-review H-24)。
        /// </summary>
        public async Task InitializeAsync()
        {
            StatusText = "加载中...";
            await Task.Run(() => RefreshAll());
        }

        #region Properties

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public int MaterialCount
        {
            get => _materialCount;
            set { _materialCount = value; OnPropertyChanged(); }
        }

        public int ActiveBomCount
        {
            get => _activeBomCount;
            set { _activeBomCount = value; OnPropertyChanged(); }
        }

        public int AlertCount
        {
            get => _alertCount;
            set { _alertCount = value; OnPropertyChanged(); }
        }

        public string LastSyncText
        {
            get => _lastSyncText;
            set { _lastSyncText = value; OnPropertyChanged(); }
        }

        public string BomSearchCode
        {
            get => _bomSearchCode;
            set { _bomSearchCode = value; OnPropertyChanged(); }
        }

        public ObservableCollection<AlertItem> Alerts
        {
            get => _alerts;
            set { _alerts = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BomTreeNode> BomTreeNodes
        {
            get => _bomTreeNodes;
            set { _bomTreeNodes = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ExpandBomCommand { get; }
        public ICommand SyncNowCommand { get; }

        #endregion

        #region Commands

        public void RefreshAll()
        {
            StatusText = "刷新中...";

            try
            {
                using var scope = _services.CreateScope();
                var sp = scope.ServiceProvider;

                // v2 M-29: 使用 SQL COUNT(*) 而非 GetAll().Count() 避免拉取全量物料
                var materialRepo = sp.GetRequiredService<IMaterialRepository>();
                MaterialCount = materialRepo.GetCount(1);

                // KPI: 活跃 BOM 数（估算为有子节点的物料数）
                ActiveBomCount = MaterialCount; // V1.0 简化

                // 预警: 刷新预警列表 — M-27: 使用 Clear+Add 代替 new ObservableCollection 避免 UI 重建
                var evaluator = sp.GetRequiredService<IAlertEvaluator>();
                var alerts = evaluator.Evaluate(Array.Empty<VarianceResult>());
                Alerts.Clear();
                foreach (var a in alerts)
                    Alerts.Add(new AlertItem
                    {
                        Severity = a.Severity.ToString(),
                        Message = a.Message,
                        TriggeredRule = a.TriggeredRule
                    });
                AlertCount = alerts.Count;

                // 同步状态
                var syncService = sp.GetRequiredService<ISyncService>();
                var lastSync = syncService.GetLastSyncTime();
                LastSyncText = lastSync?.ToString("yyyy-MM-dd HH:mm") ?? "从未同步";

                StatusText = $"刷新完成 — {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText = $"刷新失败: {ex.Message}";
            }
        }

        // C-22 fix: ExpandBom 异步化，避免 UI 线程同步阻塞大 BOM 数据库查询
        public async Task ExpandBomAsync()
        {
            if (string.IsNullOrWhiteSpace(BomSearchCode))
            {
                StatusText = "请输入物料编码";
                return;
            }

            StatusText = $"展开 BOM: {BomSearchCode}...";

            try
            {
                var code = BomSearchCode; // 捕获当前值
                var treeNodes = await Task.Run(() =>
                {
                    using var scope = _services.CreateScope();
                    var sp = scope.ServiceProvider;
                    var bomService = sp.GetRequiredService<IBomService>();
                    var nodes = bomService.Expand(code);

                    return nodes
                        .Where(n => n.Level == 0)
                        .Select(n => BuildTreeNode(n, nodes))
                        .ToList();
                });

                BomTreeNodes.Clear();
                foreach (var tn in treeNodes)
                    BomTreeNodes.Add(tn);

                StatusText = $"BOM 展开完成: {treeNodes.Sum(t => CountNodes(t))} 个节点";
            }
            catch (Exception ex)
            {
                StatusText = $"BOM 展开失败: {ex.Message}";
            }
        }

        private static int CountNodes(BomTreeNode node)
        {
            return 1 + (node.Children?.Sum(c => CountNodes(c)) ?? 0);
        }

        private async Task SyncNowAsync()
        {
            StatusText = "同步中...";

            try
            {
                using var scope = _services.CreateScope();
                var sp = scope.ServiceProvider;
                var syncService = sp.GetRequiredService<ISyncService>();
                var result = await Task.Run(() => syncService.SyncAllAsync(UserRole.Admin));

                if (result.Success)
                    StatusText = $"同步完成: {result.TotalRecords} 条记录";
                else
                    StatusText = $"同步失败: {result.ErrorMessage}";
            }
            catch (Exception ex)
            {
                StatusText = $"同步异常: {ex.Message}";
            }
        }

        #endregion

        #region Tree Helper

        private static BomTreeNode BuildTreeNode(BomExpandedNode node,
            List<BomExpandedNode> allNodes,
            Dictionary<long, List<BomExpandedNode>>? childrenByParent = null)
        {
            // C-1 fix: 预建 parent→children 索引，O(n) 建树替代 O(n²)
            if (childrenByParent == null)
            {
                childrenByParent = new Dictionary<long, List<BomExpandedNode>>();
                foreach (var n in allNodes)
                {
                    var parentId = n.ParentMaterialId ?? -1;
                    if (!childrenByParent.ContainsKey(parentId))
                        childrenByParent[parentId] = new List<BomExpandedNode>();
                    childrenByParent[parentId].Add(n);
                }
            }

            childrenByParent.TryGetValue(node.MaterialId, out var children);
            var childNodes = children != null
                ? children.Select(c => BuildTreeNode(c, allNodes, childrenByParent))
                : Enumerable.Empty<BomTreeNode>();

            return new BomTreeNode
            {
                DisplayText = $"[L{node.Level}] {node.ItemCode} — {node.Description} (x{node.Quantity:F2} {node.Unit})",
                Children = new ObservableCollection<BomTreeNode>(childNodes)
            };
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            // C-20 fix: 确保属性变更通知始终在 UI 线程触发
            // Task.Run 中的属性更新会跨线程触发 PropertyChanged，导致 WPF 绑定异常
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => OnPropertyChanged(name));
                return;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

}
