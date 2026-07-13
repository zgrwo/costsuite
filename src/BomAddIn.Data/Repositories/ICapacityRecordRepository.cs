using System.Collections.Generic;
using System.Data;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface ICapacityRecordRepository
    {
        IEnumerable<CapacityRecord> GetByWorkCenter(string workCenterId, long dataVersion);
        void BulkUpsert(IEnumerable<CapacityRecord> records);
        void BulkUpsert(IEnumerable<CapacityRecord> records, IDbConnection conn, IDbTransaction tx);
    }
}
