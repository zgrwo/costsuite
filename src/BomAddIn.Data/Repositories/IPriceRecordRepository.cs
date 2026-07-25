using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IPriceRecordRepository
    {
        PriceRecord? GetLatestByMaterialId(long materialId);
        /// <summary>批量获取多个物料的最新单价 (v2 C-18)。返回 materialId→PriceRecord 映射。</summary>
        Dictionary<long, PriceRecord> GetLatestByMaterialIds(IEnumerable<long> materialIds);
        PriceRecord? GetByMaterialIdAndDate(long materialId, DateTime asOfDate);
        /// <summary>批量获取多个物料在指定日期的价格 (C-2 fix: 消除 N+1)</summary>
        Dictionary<long, PriceRecord> GetByMaterialIdsAndDate(IEnumerable<long> materialIds, DateTime asOfDate);
        IEnumerable<PriceRecord> GetByMaterialVersion(long materialId, long dataVersion);
        IEnumerable<PriceRecord> GetHistory(long materialId, DateTime from, DateTime to);
        /// <summary>批量获取多个物料的价格历史 (U-2 fix: 消除 ALERTCHECK N+1)</summary>
        IEnumerable<PriceRecord> GetHistoryBatch(IEnumerable<long> materialIds, DateTime from, DateTime to);
        void BulkUpsert(IEnumerable<PriceRecord> records);
        void BulkUpsert(IEnumerable<PriceRecord> records, IDbConnection conn, IDbTransaction tx);
    }
}
