using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BomAddIn.Core.Services;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.UDF;
using Microsoft.Extensions.DependencyInjection;

namespace BomAddIn.UI.TaskPane;

/// <summary>
/// Excel 自定义任务窗格 — 同步状态 + 快速搜索 + 快照。
/// 通过 Excel-DNA CustomTaskPane API 注册，在 Excel 窗口右侧常驻。
/// </summary>
public partial class BomTaskPane : UserControl
{
    private int _syncInProgress;
    private readonly Dispatcher _dispatcher;

    public BomTaskPane()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        InitializeComponent();
        Loaded += (_, _) => RefreshSyncStatus();
    }

    private void RefreshSyncStatus()
    {
        try
        {
            using var scope = Container.BeginScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
            var lastSync = syncService.GetLastSyncTime();

            txtLastSync.Text = lastSync.HasValue
                ? lastSync.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : "从未同步";
        }
        catch
        {
            txtLastSync.Text = "状态未知";
        }
    }

    /// <summary>
    /// 同步按钮点击 — async void 由 WPF 事件签名决定。
    /// 所有 UI 更新通过 Dispatcher.Invoke 确保在 UI 线程执行。
    /// 全局 try/catch 防止异常崩溃 Excel 进程 (excel-dna-threading §4 反模式表)。
    /// </summary>
    private async void OnSyncClick(object sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _syncInProgress, 1, 0) != 0)
        {
            _dispatcher.Invoke(() =>
            {
                txtSyncStatus.Text = "同步已在进行中，请等待完成。";
            });
            return;
        }

        _dispatcher.Invoke(() =>
        {
            btnSync.IsEnabled = false;
            btnSync.Content = "同步中...";
            txtSyncStatus.Text = "进行中...";
        });

        try
        {
            using var scope = Container.BeginScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ISyncService>();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var currentUser = authService.GetCurrentUser(0); // 从当前会话获取
            var callerRole = currentUser?.Role ?? UserRole.Viewer;
            var result = await Task.Run(() => syncService.SyncAllAsync(callerRole));

            _dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    txtSyncStatus.Text = $"同步完成 ({result.TotalRecords} 条)";
                    txtSyncStatus.Foreground = new SolidColorBrush(Colors.Green);
                    RefreshSyncStatus();
                }
                else
                {
                    txtSyncStatus.Text = $"同步失败: {result.ErrorMessage}";
                    txtSyncStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                txtSyncStatus.Text = $"异常: {ex.Message}";
                txtSyncStatus.Foreground = new SolidColorBrush(Colors.Red);
            });
        }
        finally
        {
            _dispatcher.Invoke(() =>
            {
                btnSync.IsEnabled = true;
                btnSync.Content = "立即同步";
            });
            Interlocked.Exchange(ref _syncInProgress, 0);
        }
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => DoSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DoSearch();
    }

    private void DoSearch()
    {
        var code = txtSearchCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            txtSearchResult.Text = "请输入物料编码。";
            return;
        }

        try
        {
            using var scope = Container.BeginScope();
            var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
            var nodes = bomService.Expand(code!, DateTime.Today);

            txtSearchResult.Text = nodes.Count == 0
                ? $"未找到物料 '{code}'。"
                : $"找到 {nodes.Count} 个节点\n根节点: {nodes[0].Description}\n根用量: {nodes[0].Quantity} {nodes[0].Unit}";
        }
        catch (Exception ex)
        {
            txtSearchResult.Text = $"搜索异常: {ex.Message}";
        }
    }

    private async void OnSnapshotClick(object sender, RoutedEventArgs e)
    {
        btnSnapshot.IsEnabled = false;
        btnSnapshot.Content = "创建中...";

        try
        {
            var snapshot = await Task.Run(() =>
            {
                using var scope = Container.BeginScope();
                var svc = scope.ServiceProvider.GetRequiredService<ISnapshotService>();
                return svc.CreateSnapshot(UserRole.Admin, "Manual",
                    $"UI snapshot at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            });

            txtSnapshotResult.Text = $"快照已创建 (Id={snapshot.Id})\n时间: {snapshot.CreatedAt:yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception ex)
        {
            txtSnapshotResult.Text = $"快照失败: {ex.Message}";
        }
        finally
        {
            btnSnapshot.IsEnabled = true;
            btnSnapshot.Content = "创建手动快照";
        }
    }
}
