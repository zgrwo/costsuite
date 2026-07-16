using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using DuckDB.NET.Data;

namespace BomAddIn.Data.Analysis
{
    /// <summary>DuckDB 内存分析引擎 — BOM 递归展开 + 价格聚合</summary>
    /// <remarks>
    /// 使用 DuckDB 内存模式，数据从 SQLite 加载。
    /// 启动时调用 EnsureLoaded() 预热。
    /// 同步完成后调用 LoadFromSqlite() 刷新。
    /// </remarks>
    public class BomAnalysisProvider : IBomAnalysisProvider, IDisposable
    {
        private DuckDBConnection? _duckDb;
        private readonly object _lock = new object();
        private volatile bool _isLoaded;

        /// <summary>DuckDB 是否已完成全量数据加载（预热完成）</summary>
        public bool IsLoaded => _isLoaded;

        public void EnsureLoaded(IDbConnection sqliteConn)
        {
            if (_isLoaded) return;
            lock (_lock)
            {
                if (_isLoaded) return;
                LoadFromSqlite(sqliteConn);
                _isLoaded = true;
            }
        }

        public void LoadFromSqlite(IDbConnection sqliteConn)
        {
            if (sqliteConn == null) throw new ArgumentNullException(nameof(sqliteConn));
            lock (_lock)
            {
                // D-5 fix: 先构建新连接，加载数据，再原子替换
                var newDb = new DuckDBConnection("DataSource=:memory:");
                newDb.Open();

                // 临时指向新数据库（锁内无竞态）
                var oldDb = _duckDb;
                _duckDb = newDb;
                _isLoaded = false;

                using var cmd = newDb.CreateCommand();

                // 创建内存表 — 与 SQLite schema 对齐
                cmd.CommandText = @"
                    CREATE TABLE Materials (
                        Id BIGINT, OrgId BIGINT, Code VARCHAR, Name VARCHAR,
                        Spec VARCHAR, Unit VARCHAR, Category VARCHAR, IsActive BOOLEAN
                    );
                    CREATE TABLE BomNodes (
                        Id BIGINT, OrgId BIGINT, ParentMaterialId BIGINT,
                        ChildMaterialId BIGINT, Quantity DOUBLE,
                        Position VARCHAR, ScrapRate DOUBLE, BomViewType VARCHAR,
                        Level INTEGER, ValidFrom VARCHAR, ValidTo VARCHAR,
                        VersionState VARCHAR
                    );
                    CREATE TABLE Prices (
                        Id BIGINT, OrgId BIGINT, MaterialId BIGINT,
                        SupplierId BIGINT, UnitPrice DOUBLE, Currency VARCHAR,
                        DataVersion BIGINT, EffectiveDate VARCHAR
                    );
                ";
                cmd.ExecuteNonQuery();

                // 从 SQLite 加载到 DuckDB（批量模式：每批 200 行）
                try
                {
                    LoadTableBatched(sqliteConn, "Materials",
                        "SELECT Id, OrgId, Code, Name, Spec, Unit, Category, IsActive FROM Materials WHERE IsActive = 1");
                    LoadTableBatched(sqliteConn, "BomNodes",
                        "SELECT Id, OrgId, ParentMaterialId, ChildMaterialId, Quantity, Position, ScrapRate, BomViewType, Level, ValidFrom, ValidTo, VersionState FROM BomStructures");
                    LoadTableBatched(sqliteConn, "Prices",
                        "SELECT Id, OrgId, MaterialId, SupplierId, UnitPrice, Currency, DataVersion, EffectiveDate FROM Prices");
                }
                catch
                {
                    // 加载失败：清理失败的新连接，恢复旧连接（保持分析子系统可用）
                    _duckDb?.Close();
                    _duckDb?.Dispose();
                    _duckDb = oldDb;
                    _isLoaded = oldDb != null;
                    throw;
                }

                // 创建 DuckDB 内存索引加速 CTE 递归
                using var indexCmd = newDb.CreateCommand();
                indexCmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS idx_bom_parent ON BomNodes(ParentMaterialId);
                    CREATE INDEX IF NOT EXISTS idx_bom_state ON BomNodes(VersionState);
                    CREATE INDEX IF NOT EXISTS idx_materials_code ON Materials(Code);
                    CREATE INDEX IF NOT EXISTS idx_prices_mat ON Prices(MaterialId, EffectiveDate);
                ";
                indexCmd.ExecuteNonQuery();

                // 数据加载完成，关闭旧连接
                oldDb?.Close();
                oldDb?.Dispose();
                _isLoaded = true;
            }
        }

        public List<BomExpandedNode> ExpandBom(string itemCode, DateTime? asOfDate = null)
        {
            var date = (asOfDate ?? DateTime.Today).ToString("yyyy-MM-dd");

            lock (_lock)
            {
                if (_duckDb == null)
                    throw new InvalidOperationException("DuckDB 未初始化。请先调用 LoadFromSqlite()。");

                // H-25: 迭代 BFS 展开 + 全局 HashSet 去重。
                // 替代递归 CTE 的 list_contains/Path 方案，避免 DAG 中枚举所有路径的指数爆炸。
                // 每个 MaterialId 只展开一次（首次出现在最浅层级），O(N) 替代 O(分支^深度)。
                var visited = new HashSet<long>();
                var results = new List<BomExpandedNode>();
                const int maxLevel = 20;

                // Step 1: 查找根物料
                using (var rootCmd = _duckDb.CreateCommand())
                {
                    rootCmd.CommandText = @"
                        SELECT Id, Code, Name, Unit, Category
                        FROM Materials WHERE Code = $1";
                    rootCmd.Parameters.Add(new DuckDBParameter(itemCode));

                    using var reader = rootCmd.ExecuteReader();
                    if (!reader.Read())
                        return results; // 物料不存在，返回空列表

                    var rootId = reader.GetInt64(0);
                    var rootCode = reader.GetString(1);
                    var rootName = reader.GetString(2);
                    var rootUnit = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var rootCategory = reader.IsDBNull(4) ? "" : reader.GetString(4);

                    visited.Add(rootId);
                    results.Add(new BomExpandedNode
                    {
                        Level = 0,
                        MaterialId = rootId,
                        ParentMaterialId = null,
                        ItemCode = rootCode,
                        Description = rootName,
                        Quantity = 1.0,
                        Unit = rootUnit,
                        Source = rootCategory,
                        VersionState = "Released"
                    });
                }

                // Step 2: BFS 逐层展开
                var currentParentIds = new List<long> { results[0].MaterialId };
                var levelQuantity = new Dictionary<long, double> { [results[0].MaterialId] = 1.0 };
                var levelParent = new Dictionary<long, long?> { [results[0].MaterialId] = null };

                for (int level = 1; level <= maxLevel && currentParentIds.Count > 0; level++)
                {
                    // 批量查询当前层所有父节点的子节点
                    // 安全说明：currentParentIds 为 List&lt;long&gt;（Int64 数据库主键），非用户输入，无注入风险。
                    // DuckDB.NET v1.0.2 不支持数组/列表原生参数 — 升级后可替换为参数化查询。
                    var parentIdList = string.Join(", ", currentParentIds);
                    var children = new List<(long childId, long parentId, string code, string name,
                        string unit, double qty, string category)>();

                    using (var childCmd = _duckDb.CreateCommand())
                    {
                        childCmd.CommandText = $@"
                            SELECT b.ChildMaterialId, b.ParentMaterialId,
                                   m.Code, m.Name, m.Unit, b.Quantity, m.Category
                            FROM BomNodes b
                            JOIN Materials m ON b.ChildMaterialId = m.Id
                            WHERE b.ParentMaterialId IN ({parentIdList})
                              AND date(b.ValidFrom) <= date($1)
                              AND (b.ValidTo IS NULL OR date(b.ValidTo) > date($1))
                              AND b.VersionState = 'Released'";
                        childCmd.Parameters.Add(new DuckDBParameter(date));

                        using var reader = childCmd.ExecuteReader();
                        while (reader.Read())
                        {
                            children.Add((
                                reader.GetInt64(0),
                                reader.GetInt64(1),
                                reader.GetString(2),
                                reader.GetString(3),
                                reader.IsDBNull(4) ? "" : reader.GetString(4),
                                Convert.ToDouble(reader.GetValue(5)),
                                reader.IsDBNull(6) ? "" : reader.GetString(6)
                            ));
                        }
                    }

                    // 去重 + 准备下一层
                    var nextParentIds = new List<long>();
                    foreach (var (childId, parentId, code, name, unit, qty, category) in children)
                    {
                        if (!visited.Add(childId))
                            continue; // 全局去重：已展开过的不再处理

                        var parentQty = levelQuantity[parentId];
                        var cumulativeQty = parentQty * qty;

                        levelQuantity[childId] = cumulativeQty;
                        levelParent[childId] = parentId;
                        nextParentIds.Add(childId);

                        results.Add(new BomExpandedNode
                        {
                            Level = level,
                            MaterialId = childId,
                            ParentMaterialId = parentId,
                            ItemCode = code,
                            Description = name,
                            Quantity = cumulativeQty,
                            Unit = unit,
                            Source = category,
                            VersionState = "Released"
                        });
                    }

                    currentParentIds = nextParentIds;
                }

                // C-19: 深度达到上限时记录警告 + 向结果插入截断哨兵
                if (currentParentIds.Count > 0)
                {
                    Infrastructure.Logging.AppLogger.Warn(
                        $"BOM \"{itemCode}\" 展开达到深度上限 ({maxLevel} 层)，结果可能不完整。" +
                        $"最大层级: {maxLevel}，总节点数: {results.Count}",
                        typeof(BomAnalysisProvider));

                    // 插入截断哨兵节点，UDF/BLL 层可以检测此节点向用户发出警告
                    results.Add(new BomExpandedNode
                    {
                        Level = -1,
                        MaterialId = -1,
                        ParentMaterialId = null,
                        ItemCode = "[TRUNCATED]",
                        Description = $"BOM depth exceeded {maxLevel} levels. Results incomplete.",
                        Quantity = 0,
                        Unit = "",
                        Source = "System",
                        VersionState = ""
                    });
                }

                return results;
            }
        }

        public DataTable AggregatePrices(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                if (_duckDb == null)
                    throw new InvalidOperationException("DuckDB 未初始化。");

                using var cmd = _duckDb.CreateCommand();
                cmd.CommandText = @"
                    SELECT m.Code, m.Name,
                           AVG(p.UnitPrice) AS AvgPrice,
                           MIN(p.UnitPrice) AS MinPrice,
                           MAX(p.UnitPrice) AS MaxPrice,
                           COUNT(*) AS DataPoints
                    FROM Prices p
                    JOIN Materials m ON p.MaterialId = m.Id
                    WHERE date(p.EffectiveDate) >= $1 AND date(p.EffectiveDate) <= $2
                    GROUP BY m.Code, m.Name
                    ORDER BY m.Code
                ";
                // B-7 fix: 使用 DuckDB 原生位置参数，通过 date() 函数统一日期比较格式
                cmd.Parameters.Add(new DuckDBParameter(from.ToString("yyyy-MM-dd")));
                cmd.Parameters.Add(new DuckDBParameter(to.ToString("yyyy-MM-dd")));

                var dt = new DataTable();
                using var reader = cmd.ExecuteReader();
                dt.Load(reader);
                return dt;
            }
        }

        /// <summary>
        /// 批量加载：从 SQLite 读取数据，按批次（200行/批）INSERT 到 DuckDB。
        /// 比逐行插入减少 ~200x 次 RTT，显著降低加载延迟。
        /// </summary>
        private void LoadTableBatched(IDbConnection sqliteConn, string table, string sql)
        {
            using var sqliteCmd = sqliteConn.CreateCommand();
            sqliteCmd.CommandText = sql;
            using var reader = sqliteCmd.ExecuteReader();

            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cols.Add(reader.GetName(i));
            var colList = string.Join(", ", cols);

            var batchSize = 200;
            var valueRows = new List<string>();
            var parameters = new List<DuckDBParameter>();
            var paramIdx = 0;

            using var transaction = _duckDb!.BeginTransaction();
            try
            {
                while (reader.Read())
                {
                    var placeholders = new List<string>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var pName = $"@p{paramIdx}";
                        placeholders.Add(pName);
                        var val = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        parameters.Add(new DuckDBParameter(pName, val));
                        paramIdx++;
                    }
                    valueRows.Add($"({string.Join(", ", placeholders)})");

                    // 每 batchSize 行执行一次批量 INSERT
                    if (valueRows.Count >= batchSize)
                    {
                        FlushBatch(table, colList, valueRows, parameters);
                        valueRows.Clear();
                        parameters.Clear();
                        paramIdx = 0;
                    }
                }

                // 刷新最后一组
                if (valueRows.Count > 0)
                    FlushBatch(table, colList, valueRows, parameters);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                // R2-10: 批量加载失败时关闭 DuckDB 连接，避免残留不完整的内存表
                CloseDuckDb();
                throw;
            }
        }

        /// <summary>
        /// DuckDB v1.5.4 兼容: 批量 INSERT 使用内联值。
        /// 数据来源为本地 SQLite（可信源），非用户输入，无注入风险。
        /// V2.0 建议: 升级 DuckDB.NET.Data 后改用 DuckDB Appender API。
        /// </summary>
        private void FlushBatch(string table, string colList, List<string> valueRows, List<DuckDBParameter> parameters)
        {
            // 将参数值还原为内联 SQL 字面量
            var inlineRows = new List<string>();
            foreach (var row in valueRows)
            {
                var replaced = System.Text.RegularExpressions.Regex.Replace(row, @"@p(\d+)", match =>
                {
                    var idx = int.Parse(match.Groups[1].Value);
                    var val = parameters[idx].Value;
                    if (val == null || val == DBNull.Value)
                        return "NULL";
                    if (val is string s)
                        return $"'{s.Replace("'", "''")}'";
                    if (val is DateTime dt)
                        return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
                    if (val is bool b)
                        return b ? "TRUE" : "FALSE";
                    if (val is double d)
                        return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (val is long l)
                        return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (val is int i)
                        return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (val is decimal m)
                        return m.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    // C-17 fix: 未知类型回退值加单引号转义，防止 SQL 注入
                    var str = val.ToString()!;
                    return $"'{str.Replace("'", "''")}'";
                });
                inlineRows.Add(replaced);
            }

            var insertSql = $"INSERT INTO {table} ({colList}) VALUES {string.Join(", ", inlineRows)}";
            using var insertCmd = _duckDb!.CreateCommand();
            insertCmd.CommandText = insertSql;
            insertCmd.ExecuteNonQuery();
        }

        private void CloseDuckDb()
        {
            _duckDb?.Close();
            _duckDb?.Dispose();
            _duckDb = null;
            _isLoaded = false;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                CloseDuckDb();
            }
        }
    }
}
