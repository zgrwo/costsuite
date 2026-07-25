using System;
using System.Windows;
using System.Windows.Interop;

namespace BomAddIn.Dashboard
{
    /// <summary>
    /// Click-Twice 修复 — WPF 窗口在独立线程运行时，
    /// 点击 WPF 后回到 Excel 需要点两次的问题修复。
    /// 参考 skill excel-dna-wpf-dashboard §4。
    /// </summary>
    public class ClickTwiceFix : IDisposable
    {
        private HwndSource? _hwndSource;
        private readonly Window _wpfWindow;

        public ClickTwiceFix(Window wpfWindow)
        {
            _wpfWindow = wpfWindow;
        }

        /// <summary>
        /// 窗口初始化完成后安装 Hook（在 SourceInitialized 事件中调用）。
        /// 构造时调用 PresentationSource.FromVisual 会返回 null，因为窗口尚未初始化。
        /// </summary>
        public void Initialize()
        {
            if (_hwndSource != null) return;
            _hwndSource = PresentationSource.FromVisual(_wpfWindow) as HwndSource;
            _hwndSource?.AddHook(WndProcHook);
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam,
                                     IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_ACTIVATE = 1;

            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return (IntPtr)MA_ACTIVATE;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _hwndSource?.RemoveHook(WndProcHook);
        }
    }
}
