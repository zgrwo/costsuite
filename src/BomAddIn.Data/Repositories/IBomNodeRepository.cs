using System;
using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IBomNodeRepository
    {
        BomNode? GetById(long id);
        BomNode? GetById(long id, IDbConnection conn, IDbTransaction? tx);
        IEnumerable<BomNode> GetChildren(long parentMaterialId, DateTime? asOfDate = null);
        IEnumerable<BomNode> GetByMaterialId(long materialId, DateTime? asOfDate = null);
        void Add(BomNode node);
        void Add(BomNode node, IDbConnection conn, IDbTransaction tx);
        void Update(BomNode node);
        void Update(BomNode node, IDbConnection conn, IDbTransaction tx);
        void Delete(long id);
        void Delete(long id, IDbConnection conn, IDbTransaction tx);
    }
}
