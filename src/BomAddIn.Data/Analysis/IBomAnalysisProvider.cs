using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Analysis
{
    /// <summary>DuckDB 分析查询接口 — 内存列式引擎</summary>
    public interface IBomAnalysisProvider
    {
        /// <summary>展开物料完整 BOM 树（递归 CTE）</summary>
        List<BomExpandedNode> ExpandBom(string itemCode, DateTime? asOfDate = null);

        /// <summary>聚合时间段内价格趋势</summary>
        DataTable AggregatePrices(DateTime from, DateTime to);

        /// <summary>从 SQLite 连接加载全量数据到 DuckDB 内存表</summary>
        void LoadFromSqlite(IDbConnection sqliteConn);

        /// <summary>确保 DuckDB 内存表已创建并填充数据</summary>
        void EnsureLoaded(IDbConnection sqliteConn);
    }
}
