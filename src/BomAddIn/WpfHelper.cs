using System;
using System.Windows;
using System.Windows.Threading;

namespace BomAddIn
{
    /// <summary>
    /// WPF Application 生命周期共享管理。
    /// WPF 在单个 AppDomain 内只允许一个 Application 实例——
    /// TaskPane (Excel 主线程) 和 Dashboard (独立 STA 线程) 共用一个 AppDomain，
    /// 必须通过此 Helper 协调，避免第二个 new Application() 抛出 InvalidOperationException。
    /// </summary>
    public static class WpfHelper
    {
        private static Application? _appInstance;
        private static readonly object _lock = new object();

        /// <summary>当前 AppDomain 是否已创建 WPF Application。</summary>
        public static bool IsApplicationCreated
        {
            get { lock (_lock) return _appInstance != null; }
        }

        /// <summary>
        /// 在当前线程创建 WPF Application（如不存在且当前线程无 Application.Current）。
        /// 线程安全：通过锁保证 AppDomain 内只创建一次。
        /// </summary>
        public static void EnsureInitialized()
        {
            lock (_lock)
            {
                if (_appInstance != null) return;

                if (Application.Current == null)
                {
                    _appInstance = new Application
                    {
                        ShutdownMode = ShutdownMode.OnExplicitShutdown
                    };
                }
                else
                {
                    _appInstance = Application.Current;
                }
            }
        }

        /// <summary>
        /// 关闭 WPF Application。AutoClose() 中调用，确保插件卸载时确定性清理。
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                try { _appInstance?.Shutdown(); }
                catch (Exception)
                {
                    // Application 可能已在 AppDomain 卸载时被意外销毁
                }
                _appInstance = null;
            }
        }

        /// <summary>
        /// 在独立 STA 线程上显示 Window，不创建新的 Application。
        /// 当 Application 已由 TaskPane 创建时使用此路径——
        /// 直接 push DispatcherFrame 作为消息泵，绕过 Application.Run()。
        /// </summary>
        public static void RunWindowWithoutApplication(Window window)
        {
            var frame = new DispatcherFrame();
            window.Closed += (_, _) => { frame.Continue = false; };
            window.Show();
            Dispatcher.PushFrame(frame);
            window = null!;
        }
    }
}
