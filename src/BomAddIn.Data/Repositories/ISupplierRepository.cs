using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface ISupplierRepository
    {
        Supplier? GetById(long id);
        Supplier? GetByCode(long orgId, string code);
        IEnumerable<Supplier> GetAll(long orgId);
        void Add(Supplier supplier);
        void Update(Supplier supplier);
        void Delete(long id);
    }
}
