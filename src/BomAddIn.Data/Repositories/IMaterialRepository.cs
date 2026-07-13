using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IMaterialRepository
    {
        Material? GetById(long id);
        Material? GetByCode(long orgId, string code);
        Material? GetByCode(long orgId, string code, IDbConnection conn, IDbTransaction tx);
        /// <summary>批量按编码查询物料 (R2-16)。在事务内连接上执行，返回 code→Material 映射。</summary>
        Dictionary<string, Material> GetByCodes(long orgId, HashSet<string> codes, IDbConnection conn, IDbTransaction tx);
        /// <summary>高效 COUNT 查询 — 避免 GetAll().Count() 拉取全量数据 (v2 M-29)</summary>
        int GetCount(long orgId);
        IEnumerable<Material> GetAll(long orgId);
        IEnumerable<Material> Search(long orgId, string? category = null, string? keyword = null);
        void Add(Material material);
        void Add(Material material, IDbConnection conn, IDbTransaction tx);
        void Update(Material material);
        void Delete(long id);
    }
}
