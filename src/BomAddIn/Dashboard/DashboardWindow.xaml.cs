using System.Windows;

namespace BomAddIn.Dashboard
{
    public partial class DashboardWindow : Window
    {
        private readonly ClickTwiceFix _clickTwiceFix;
        /// <summary>用于区分用户关闭（隐藏缓存）和程序关闭（真正退出）(code-review H-21)</summary>
        internal bool IsShuttingDown { get; set; }

        public DashboardWindow()
        {
            InitializeComponent();

            var vm = new DashboardViewModel();
            DataContext = vm;

            // 应用 Click-Twice 修复（在 SourceInitialized 中安装 Hook，构造时窗口未初始化）
            _clickTwiceFix = new ClickTwiceFix(this);
            SourceInitialized += (_, _) => _clickTwiceFix.Initialize();

            // 用户关闭时：隐藏而非销毁（保持数据缓存）
            // 程序关闭时：允许真正关闭
            Closing += (_, e) =>
            {
                if (!IsShuttingDown)
                {
                    e.Cancel = true;
                    Hide();
                }
            };

            // H-24: 窗口加载完成后异步初始化数据
            Loaded += async (_, _) => await vm.InitializeAsync();

            // 窗口关闭时释放 ClickTwiceFix 的 WndProc hook
            Closed += (_, _) => _clickTwiceFix.Dispose();
        }
    }
}
