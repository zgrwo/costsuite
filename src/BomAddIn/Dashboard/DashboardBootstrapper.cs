using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace BomAddIn.Dashboard
{
    /// <summary>
    /// WPF Dashboard 生命周期管理。
    /// WPF 运行在独立 STA 线程，通过 ExcelThreadDispatcher 与 Excel 通信。
    /// 参考 skill excel-dna-wpf-dashboard §3。
    /// </summary>
    public static class DashboardBootstrapper
    {
        private static Thread? _wpfThread;
        private static DashboardWindow? _window;
        private static Application? _app;

        public static void Show()
        {
            // 窗口已存在 → 激活
            if (_window != null)
            {
                _window.Dispatcher.Invoke(() =>
                {
                    _window.Show();
                    _window.Activate();
                });
                return;
            }

            // 启动独立 STA 线程
            _wpfThread = new Thread(RunWpfApplication)
            {
                Name = "BomAddIn.Dashboard",
                IsBackground = true // Excel 退出时自动终止
            };
            _wpfThread.SetApartmentState(ApartmentState.STA);
            _wpfThread.Start();
        }

        private static void RunWpfApplication()
        {
            // 尝试创建 WPF Application——WPF 单 AppDomain 只允许一个实例。
            // 若 TaskPane 已先创建（AutoOpen → RegisterTaskPane → WpfHelper.EnsureInitialized），
            // 则走无 Application 的轻量路径：Show + Dispatcher.PushFrame。
            try
            {
                _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
            catch (InvalidOperationException)
            {
                _app = null; // Application 已被 TaskPane 在 Excel 主线程创建
            }

            _window = new DashboardWindow();

            if (_app != null)
            {
                _window.Closed += (_, _) =>
                {
                    _window = null;
                    _app.Shutdown();
                };
                _app.Run(_window);
            }
            else
            {
                BomAddIn.WpfHelper.RunWindowWithoutApplication(_window);
            }
        }

        public static void Close()
        {
            if (_window != null)
            {
                // H-21: 设置关闭标志，允许窗口真正关闭而非 Hide
                // M-4: 使用 BeginInvoke 避免 Excel 主线程死锁
                _window.Dispatcher.BeginInvoke(() =>
                {
                    if (_window != null)
                    {
                        _window.IsShuttingDown = true;
                        _window.Close();
                    }
                });
            }
        }
    }
}
