using System;
using System.Reflection;
using BomAddIn.Data.Connection;
using DbUp;
using DbUp.Engine;

namespace BomAddIn.Data.Migration
{
    /// <summary>DbUp 数据库迁移器 — 按 skill excel-dna-di-startup §4</summary>
    public class DatabaseMigrator
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DatabaseMigrator(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void RunPendingMigrations()
        {
            // DbUp 使用 Microsoft.Data.Sqlite，移除 System.Data.SQLite 专有参数
            var dbUpConnStr = System.Text.RegularExpressions.Regex.Replace(
                _connectionFactory.ConnectionString,
                @";\s*(Version|Journal Mode|Foreign Keys|Busy Timeout|BusyTimeout)=[^;]*",
                "");

            var upgrader = DeployChanges.To
                .SQLiteDatabase(dbUpConnStr)
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    s => s.StartsWith("BomAddIn.Data.Migrations.S"))
                .WithTransactionPerScript()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();

            if (!result.Successful)
            {
                throw new InvalidOperationException(
                    "数据库迁移失败。请检查 BomAddIn.Diagnostic.exe 了解详情。", result.Error);
            }
        }
    }
}
