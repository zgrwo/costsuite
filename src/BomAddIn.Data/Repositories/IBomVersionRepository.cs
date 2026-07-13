using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Data.Repositories
{
    public interface IBomVersionRepository
    {
        IEnumerable<BomVersion> GetByBomId(long bomId);
        BomVersion? GetById(long id);
        /// <summary>在指定连接和事务中读取版本（用于 TOCTOU 安全的状态转换）</summary>
        BomVersion? GetById(long id, IDbConnection conn, IDbTransaction tx);
        void Add(BomVersion version);
        void UpdateState(long id, VersionState state, long? approvedBy = null);
        void UpdateState(long id, VersionState state, long? approvedBy, IDbConnection conn, IDbTransaction tx);
        BomVersion? GetLatest(long bomId);
    }
}
