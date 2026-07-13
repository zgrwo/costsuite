using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IDataSnapshotRepository
    {
        void Add(DataSnapshot snapshot);
        DataSnapshot? GetById(long id);
        DataSnapshot? GetLatest(string snapshotType);
        IEnumerable<DataSnapshot> GetByType(string snapshotType, int limit = 20);
        void DeleteOlderThan(DateTime cutoff, string? snapshotType = null);
    }
}
