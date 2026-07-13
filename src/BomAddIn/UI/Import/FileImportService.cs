using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using ExcelDataReader;

namespace BomAddIn.UI.Import
{
    /// <summary>
    /// Excel/CSV 文件 → DataTable 解析器。
    /// 支持 .xlsx / .xls / .csv 格式。
    /// 使用 ExcelDataReader 库读取（LGPL 协议）。
    /// </summary>
    public static class FileImportService
    {
        /// <summary>从 Excel/CSV 文件解析第一张工作表为 DataTable</summary>
        public static DataTable ParseFile(string filePath, bool hasHeaders = true)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            IExcelDataReader reader;

            if (ext == ".csv")
            {
                // CSV: 使用 ExcelDataReader 的 CSV reader，需要配置编码
                var config = new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.UTF8
                };
                reader = ExcelReaderFactory.CreateCsvReader(stream, config);
            }
            else
            {
                reader = ExcelReaderFactory.CreateReader(stream);
            }

            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = hasHeaders
                }
            });

            var table = dataSet.Tables[0];
            reader.Close();

            return table;
        }

        /// <summary>解析文件并返回所有工作表</summary>
        public static DataSet ParseFileAllSheets(string filePath)
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            return reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = true
                }
            });
        }

        /// <summary>获取文件可读的工作表名称列表</summary>
        public static string[] GetSheetNames(string filePath)
        {
            var names = new List<string>();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".csv")
            {
                names.Add("Sheet1");
                return names.ToArray();
            }

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            do { names.Add(reader.Name); } while (reader.NextResult());
            return names.ToArray();
        }

        /// <summary>快速预览：返回前 N 行数据</summary>
        public static DataTable PreviewFile(string filePath, int maxRows = 5)
        {
            var table = ParseFile(filePath, hasHeaders: true);
            if (table.Rows.Count <= maxRows)
                return table;

            var preview = table.Clone();
            for (int i = 0; i < maxRows; i++)
            {
                preview.ImportRow(table.Rows[i]);
            }
            return preview;
        }
    }
}
