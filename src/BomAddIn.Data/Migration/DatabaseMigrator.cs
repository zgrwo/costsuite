using System;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using BomAddIn.Data.Connection;

namespace BomAddIn.Data.Migration
{
    /// <summary>
    /// SQLite 数据库迁移器 — 使用 System.Data.SQLite 直接执行内嵌迁移脚本，
    /// 统一 SQLite provider 依赖，避免 Microsoft.Data.Sqlite 原生库冲突。
    ///
    /// 迁移脚本命名: BomAddIn.Data.Migrations.S###_Description.sql
    /// 按脚本名排序执行，SchemaVersions 表追踪已执行脚本（幂等）。
    /// </summary>
    public class DatabaseMigrator
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public DatabaseMigrator(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void RunPendingMigrations()
        {
            using var conn = _connectionFactory.CreateConnection();

            // 确保 SchemaVersions 日志表存在
            EnsureSchemaVersionsTable(conn);

            // 查找内嵌迁移脚本（按名称排序确保顺序一致）
            var assembly = Assembly.GetExecutingAssembly();
            var scriptNames = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("BomAddIn.Data.Migrations.S"))
                .OrderBy(n => n)
                .ToList();

            foreach (var resourceName in scriptNames)
            {
                // 提取短名（如 S001_InitialSchema.sql）
                var shortName = resourceName.Replace("BomAddIn.Data.Migrations.", "");

                // 幂等检查
                if (IsScriptApplied(conn, shortName))
                    continue;

                // 读取脚本 SQL
                string sql;
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                using (var reader = new StreamReader(stream!))
                {
                    sql = reader.ReadToEnd();
                }

                // 在一个事务中执行脚本 + 记录日志
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                        }

                        RecordScriptApplied(conn, shortName);

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* 尽力回滚 */ }

                        throw new InvalidOperationException(
                            $"数据库迁移脚本 {shortName} 执行失败。", ex);
                    }
                }
            }
        }

        private static void EnsureSchemaVersionsTable(IDbConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SchemaVersions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ScriptName TEXT NOT NULL UNIQUE,
                    Applied DATETIME NOT NULL DEFAULT (datetime('now'))
                )";
            cmd.ExecuteNonQuery();
        }

        private static bool IsScriptApplied(IDbConnection conn, string scriptName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName = @name";
            cmd.Parameters.Add(new SQLiteParameter("@name", scriptName));
            return (long)cmd.ExecuteScalar()! > 0;
        }

        private static void RecordScriptApplied(IDbConnection conn, string scriptName)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO SchemaVersions (ScriptName) VALUES (@name)";
            cmd.Parameters.Add(new SQLiteParameter("@name", scriptName));
            cmd.ExecuteNonQuery();
        }
    }
}
