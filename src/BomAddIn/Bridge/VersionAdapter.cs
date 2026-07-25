using System;
using ExcelDna.Integration;

namespace BomAddIn.Bridge
{
    /// <summary>
    /// VersionAdapter 实现。通过 Excel Application.Version 检测宿主版本能力。
    /// </summary>
    public class VersionAdapter : IVersionAdapter
    {
        // Excel 365 首次支持动态数组的版本号
        private static readonly Version DynamicArrayMinVersion = new Version(16, 0, 12026);

        private static bool? _isDynamicArraySupported;
        private static readonly object _initLock = new();

        public VersionAdapter()
        {
            // H-29: 线程安全初始化。首次构造时检测 Excel 版本能力并缓存。
            if (_isDynamicArraySupported == null)
            {
                lock (_initLock)
                {
                    if (_isDynamicArraySupported == null)
                        _isDynamicArraySupported = DetectDynamicArraySupport();
                }
            }
        }

        public bool IsDynamicArraySupported => _isDynamicArraySupported ?? false;

        public string GetArrayFormulaBehavior()
        {
            return IsDynamicArraySupported
                ? "动态数组自动溢出"
                : "Ctrl+Shift+Enter 数组公式";
        }

        public int GetDefaultReturnRowCount()
        {
            // Excel 2016 无法动态溢出，返回固定行数上限
            return IsDynamicArraySupported ? 0 : 1000;
        }

        private static bool DetectDynamicArraySupport()
        {
            try
            {
                var app = (dynamic)ExcelDnaUtil.Application;
                string versionStr = app.Version;
                if (Version.TryParse(versionStr, out var version))
                {
                    return version >= DynamicArrayMinVersion;
                }
                return false;
            }
            catch
            {
                // 保守策略：无法检测时假定不支持
                return false;
            }
        }
    }
}
