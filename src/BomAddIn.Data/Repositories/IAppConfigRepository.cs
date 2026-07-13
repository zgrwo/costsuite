using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IAppConfigRepository
    {
        AppConfig? GetByKey(string key);
        IEnumerable<AppConfig> GetAll();
        void Set(AppConfig config);
        void Delete(string key);
    }
}
