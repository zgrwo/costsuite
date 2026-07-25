namespace BomAddIn.Bridge
{
    /// <summary>
    /// Excel 版本适配器 — 检测并适配不同 Excel 版本的能力差异。
    /// 探针 P-0.2: UDF 在 Excel 2016 与 365 下行为一致。
    /// </summary>
    public interface IVersionAdapter
    {
        /// <summary>Excel 365 动态数组支持</summary>
        bool IsDynamicArraySupported { get; }

        /// <summary>获取数组公式行为描述</summary>
        string GetArrayFormulaBehavior();

        /// <summary>Excel 2016 需预设输出区域大小</summary>
        int GetDefaultReturnRowCount();
    }
}
