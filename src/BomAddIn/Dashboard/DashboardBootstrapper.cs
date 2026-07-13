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
            _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            _window = new DashboardWindow();

            _window.Closed += (_, _) =>
            {
                _window = null;
                _app.Shutdown();
            };

            _app.Run(_window);
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
