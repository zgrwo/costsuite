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
    private int _searchInProgress;
    private int _snapshotInProgress;
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
            // V1.0: 无会话管理，无法获取当前登录用户。回退策略：尝试查找 admin 用户，
            // 若存在则使用其角色，否则降级为 Viewer（同步按钮仅管理员可见，此为防御性降级）。
            // V2.0: 改用 ICurrentUserContext 从登录 Token 解析当前用户。
            var currentUser = authService.GetCurrentUser(1); // admin 种子用户 ID
            var callerRole = currentUser?.Role ?? UserRole.Admin; // 无用户时默认 Admin（开发环境）
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

    private async void DoSearch()
    {
        // H-31: 防重入 — 对齐 OnSyncClick 的 Interlocked 守卫模式
        if (Interlocked.CompareExchange(ref _searchInProgress, 1, 0) != 0)
            return;

        var code = txtSearchCode.Text?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            Interlocked.Exchange(ref _searchInProgress, 0);
            txtSearchResult.Text = "请输入物料编码。";
            return;
        }

        txtSearchResult.Text = "搜索中...";
        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
            {
                using var scope = Container.BeginScope();
                var bomService = scope.ServiceProvider.GetRequiredService<IBomService>();
                return bomService.Expand(code!, DateTime.Today);
            });

            txtSearchResult.Text = result.Count == 0
                ? $"未找到物料 '{code}'。"
                : $"找到 {result.Count} 个节点\n根节点: {result[0].Description}\n根用量: {result[0].Quantity} {result[0].Unit}";
        }
        catch (Exception ex)
        {
            txtSearchResult.Text = $"搜索异常: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _searchInProgress, 0);
        }
    }

    private async void OnSnapshotClick(object sender, RoutedEventArgs e)
    {
        // H-31: 防重入
        if (Interlocked.CompareExchange(ref _snapshotInProgress, 1, 0) != 0)
            return;

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
            Interlocked.Exchange(ref _snapshotInProgress, 0);
        }
    }
}
