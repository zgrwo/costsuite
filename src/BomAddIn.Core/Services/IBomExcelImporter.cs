using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>导入行数据 — 键为列名，值为单元格内容</summary>
    public class ImportRow
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string> Cells { get; set; } = new();
    }

    /// <summary>导入结果</summary>
    public class ImportResult
    {
        public bool Success { get; set; }
        public int RowCount { get; set; }
        public int SuccessCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public static ImportResult Fail(string error)
        {
            return new ImportResult { Success = false, Errors = new List<string> { error } };
        }

        public override string ToString()
        {
            return Success
                ? $"导入完成: {SuccessCount}/{RowCount} 行成功。"
                : $"导入失败: {string.Join("; ", Errors)}";
        }
    }

    /// <summary>BOM Excel 导入器接口</summary>
    public interface IBomExcelImporter
    {
        /// <summary>从 DataTable 导入物料数据（自动检测列映射）</summary>
        ImportResult ImportMaterials(DataTable table, long orgId, UserRole callerRole = UserRole.Admin);

        /// <summary>从 DataTable 导入 BOM 结构数据</summary>
        ImportResult ImportBomStructures(DataTable table, long orgId, UserRole callerRole = UserRole.Admin);

        /// <summary>检测表头映射（中文/英文模糊匹配）</summary>
        Dictionary<string, string> DetectColumnMapping(string[] headers);
    }
}
