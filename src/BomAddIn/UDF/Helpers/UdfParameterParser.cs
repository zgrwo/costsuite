using System;
using System.Globalization;
using ExcelDna.Integration;

namespace BomAddIn.UDF.Helpers
{
    /// <summary>UDF 参数解析辅助方法</summary>
    public static class UdfParameterParser
    {
        /// <summary>
        /// 解析日期参数 — 支持 DateTime / double(OADate) / string / null(ExcelMissing)
        /// 使用 InvariantCulture 确保跨语言 Excel 日期解析一致性 (code-review H-22)。
        /// </summary>
        public static DateTime? ParseDateArg(object? arg)
        {
            if (arg == null || arg is ExcelMissing || arg is ExcelDna.Integration.ExcelEmpty)
                return null;

            if (arg is DateTime dt)
                return dt;

            // OADate 0 = 1899-12-30, 1 = 1899-12-31 (code-review L-20: allow d >= 0)
            if (arg is double d && d >= 0)
                return DateTime.FromOADate(d);

            if (arg is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;

            return null;
        }

        /// <summary>
        /// 解析版本状态过滤参数
        /// </summary>
        public static string ParseVersionState(string? arg)
        {
            if (string.IsNullOrWhiteSpace(arg))
                return "Released";

            var normalized = arg!.Trim();
            var result = normalized switch
            {
                "Draft" => "Draft",
                "Released" => "Released",
                "All" => "All",
                _ => null
            };
            if (result == null)
            {
                BomAddIn.Infrastructure.Logging.AppLogger.Debug(
                    $"无效的版本状态 '{normalized}'，回退为 'Released'", typeof(UdfParameterParser));
                return "Released";
            }
            return result;
        }

        /// <summary>
        /// 判断参数是否由 Excel 用户提供了有效值
        /// </summary>
        public static bool IsProvided(object? arg)
        {
            return arg != null
                   && !(arg is ExcelMissing)
                   && !(arg is ExcelDna.Integration.ExcelEmpty);
        }

        /// <summary>
        /// 将 List 转换为 Excel 二维数组 object[,]
        /// </summary>
        public static object[,] ToRectangularArray<T>(System.Collections.Generic.List<T> items,
            Func<T, object?[]> rowSelector, string[] columnHeaders)
        {
            var rows = items.Count + 1; // +1 for header
            var cols = columnHeaders.Length;
            var result = new object[rows, cols];

            for (int c = 0; c < cols; c++)
                result[0, c] = columnHeaders[c];

            for (int r = 0; r < items.Count; r++)
            {
                var row = rowSelector(items[r]);
                if (row == null) continue;
                for (int c = 0; c < cols && c < row.Length; c++)
                    result[r + 1, c] = row[c] ?? string.Empty;
            }

            return result;
        }
    }
}
