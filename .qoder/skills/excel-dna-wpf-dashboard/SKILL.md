---
description: "Excel-DNA WPF 仪表盘集成 — TaskPane、DashboardWindow、数据绑定、Ribbon 交互。"
name: "Excel-DNA WPF 仪表盘集成"
---

# Skill: Excel-DNA WPF 仪表盘集成

> **TRIGGER**: 修改 `src/BomAddIn/Dashboard/`、`src/BomAddIn/UI/TaskPane/`、`src/BomAddIn/Ribbon/` 或任何 WPF XAML/ViewModel 代码时，**必须**先读此 Skill。
>
> **来源**: [DotNetRefEdit](https://github.com/Ron-Ldn/DotNetRefEdit)、[FinAnSu](https://github.com/brymck/finansu)、[Excel-DNA 社区](https://github.com/Excel-DNA/ExcelDna/discussions)  
> **适用范围**: WPF 仪表盘、自定义弹窗、任何需要独立 UI 线程的 Excel-DNA 场景

---

## 1. 核心挑战

WPF 要求 STA 线程；Excel 主线程也是 STA。如果 WPF 窗口直接运行在 Excel 主线程上：
- Excel 计算时会冻结 WPF UI
- WPF 弹窗会阻塞 Excel 消息泵
- 关闭 WPF 窗口可能导致 Excel 一起崩溃

**解决方案**: WPF 运行在独立 STA 线程，通过 `ExcelThreadDispatcher` 与 Excel 通信。

## 2. 架构模式

```
┌──────────────────────┐        ┌──────────────────────┐
│  Excel STA 主线程     │        │  WPF STA 独立线程     │
│                      │        │                      │
│  • UDF               │        │  • DashboardWindow    │
│  • Ribbon 事件       │ 封送   │  • ViewModels         │
│  • TaskPane          │◄─────►│  • 图表渲染           │
│  • COM 写入          │QueueAs │  • 用户交互           │
│                      │Macro   │                      │
└──────────────────────┘        └──────────────────────┘
         │                               │
         └───────────┬───────────────────┘
                     │ 共享
                     ▼
         ┌──────────────────────┐
         │   DataContext         │
         │   (线程安全数据)      │
         │   ObservableCollection│
         └──────────────────────┘
```

## 3. 独立 STA 线程启动 WPF 窗口

```csharp
public class DashboardBootstrapper
{
    private static Thread _wpfThread;
    private static DashboardWindow _window;

    public static void Show()
    {
        if (_window != null && _window.Dispatcher.CheckAccess())
        {
            // 窗口已存在，激活它
            _window.Dispatcher.Invoke(() =>
            {
                _window.Show();
                _window.Activate();
            });
            return;
        }

        // 启动独立 STA 线程
        _wpfThread = new Thread(RunWpfApplication);
        _wpfThread.SetApartmentState(ApartmentState.STA);
        _wpfThread.IsBackground = true;  // Excel 退出时自动终止
        _wpfThread.Start();
    }

    private static void RunWpfApplication()
    {
        // 创建 WPF Application（此线程独占）
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _window = new DashboardWindow();

        // 关键：Excel 关闭时自动清理
        _window.Closed += (s, e) =>
        {
            _window = null;
            app.Shutdown();
        };

        app.Run(_window);  // 启动 WPF 消息泵
    }

    public static void Close()
    {
        _window?.Dispatcher.Invoke(() => _window.Close());
    }
}
```

## 4. Click-Twice 问题与修复

**问题**: WPF 窗口运行在独立线程时，点击 WPF 窗口中的按钮后，再次点击 Excel 单元格需要**点两次**才能激活。

**原因**: Windows 的焦点管理在跨线程窗口间需要额外的 `WH_CALLWNDPROC` 钩子。

**修复**（来自 DotNetRefEdit）:

```csharp
public class ClickTwiceFix : IDisposable
{
    private readonly HwndSource _hwndSource;
    private IntPtr _hookId;

    public ClickTwiceFix(Window wpfWindow)
    {
        _hwndSource = HwndSource.FromVisual(wpfWindow)
                      ?? PresentationSource.FromVisual(wpfWindow) as HwndSource;

        _hwndSource?.AddHook(WndProcHook);
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam,
                                IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_ACTIVATE = 1;

        if (msg == WM_MOUSEACTIVATE)
        {
            // 激活当前 WPF 窗口，同时让 Excel 窗口知道焦点转移
            handled = true;
            return (IntPtr)MA_ACTIVATE;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => _hwndSource?.RemoveHook(WndProcHook);
}
```

## 5. WPF → Excel 数据写入

```csharp
// DashboardViewModel.cs
public class DashboardViewModel : ObservableObject
{
    private readonly IExcelThreadDispatcher _dispatcher;

    // ✅ 正确：异步封送，不阻塞 UI
    public async Task ExportToSheetAsync(DataTable data)
    {
        await _dispatcher.RunOnExcelThreadAsync(() =>
        {
            var app = (Application)ExcelDnaUtil.Application;
            var sheet = (Worksheet)app.ActiveSheet;

            // 从 A1 开始写数据
            int rows = data.Rows.Count;
            int cols = data.Columns.Count;
            var range = sheet.Range[sheet.Cells[1, 1], sheet.Cells[rows, cols]];

            object[,] values = new object[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    values[r, c] = data.Rows[r][c];

            range.Value2 = values;
        });

        // 写完后通知用户（仍在 WPF 线程）
        StatusMessage = $"已导出 {data.Rows.Count} 行到活动工作表";
    }
}
```

## 6. 数据刷新策略

| 策略 | 适用场景 | 实现 |
|------|---------|------|
| **事件驱动** | 数据量小、实时性高 | `IEventBus.Subscribe<DataRefreshedEvent>()` → 直接更新 ViewModel |
| **定时轮询** | 数据量大、变化慢 | `DispatcherTimer` 每 30s 拉一次 |
| **手动刷新** | 用户主动触发 | Ribbon 按钮 → `RefreshCommand` |
| **混合模式** | 生产推荐 | 事件驱动增量 + 定时全量兜底 + 手动强制刷新 |

```csharp
// 混合刷新示例
public class DashboardViewModel
{
    private readonly DispatcherTimer _pollTimer;

    public DashboardViewModel(IEventBus eventBus)
    {
        // 1. 事件驱动（订阅 BLL 层的变更通知）
        eventBus.Subscribe<DataRefreshedEvent>(e =>
        {
            Application.Current.Dispatcher.Invoke(() => LoadIncremental(e.ChangedItems));
        });

        // 2. 定时全量刷新（兜底）
        _pollTimer = new DispatcherTimer(TimeSpan.FromSeconds(30));
        _pollTimer.Tick += (s, e) => LoadFullData();
        _pollTimer.Start();
    }
}
```

## 7. Excel 关闭时的优雅退出

```csharp
// AutoOpen.cs
public static void AutoOpen()
{
    // 注册 Excel 关闭事件
    var app = (Application)ExcelDnaUtil.Application;
    app.WorkbookBeforeClose += (wb, ref cancel) =>
    {
        // 检查是否最后一个工作簿
        if (app.Workbooks.Count <= 1)
        {
            DashboardBootstrapper.Close();
        }
    };
}
```

## 8. 自检清单

- [ ] WPF 窗口运行在独立 STA 线程（`new Thread(() => { ... })`）
- [ ] `ShutdownMode = OnExplicitShutdown`（不是 `OnLastWindowClose`）
- [ ] `IsBackground = true`（Excel 退出时自毁）
- [ ] 已集成 Click-Twice 修复（`HwndSource.AddHook`）
- [ ] 所有 Excel 写操作通过 `RunOnExcelThreadAsync`
- [ ] ViewModel 中的集合使用 `ObservableCollection` + `Dispatcher.Invoke` 更新
- [ ] Excel 关闭时通知 WPF 线程退出
- [ ] 窗口恢复逻辑（show + activate，而非每次 new）

## 9. ⚠️ 已知陷阱

### 9.1 CustomTaskPane + WPF 导致 Excel 崩溃

`CustomTaskPaneFactory.CreateCustomTaskPane(typeof(BomTaskPane), ...)` 在 AutoOpen 中直接调用时，如果 WPF 资源（`PresentationFramework`、`WindowsBase` 等）未完全就绪，会导致 Excel **静默崩溃**（进程退出，无异常日志）。

**症状**: 加载 .xll 时 Excel 立即退出，下次打开提示"安全模式"。

**规避**: 当前版本禁用 `RegisterTaskPane()`。恢复前需确认 WPF 初始化在 Excel-DNA 加载链中的时序。

### 9.2 Dashboard/ViewModel 构造不查数据库

ViewModel 构造函数中同步查询数据库 → 阻塞 Excel 主线程 → 超时或死锁。使用 `async Task InitializeAsync()` + `Window.Loaded` 事件触发。

## 10. 参考

- [DotNetRefEdit: WPF + Excel-DNA 完整演示](https://github.com/Ron-Ldn/DotNetRefEdit)
- [FinAnSu: Ribbon + RTD + 图表的综合 Excel-DNA 项目](https://github.com/brymck/finansu)
- [Excel-DNA: COM Object Model Notes](https://excel-dna.net/docs/archive/wiki/COM-object-model-notes/)
- [StackOverflow: Async WPF window in Excel-DNA](https://stackoverflow.com/questions/tagged/excel-dna)
