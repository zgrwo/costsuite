using System.Data;

namespace BomAddIn.Data.Connection
{
    public interface IDbConnectionFactory
    {
        IDbConnection CreateConnection();
        string ConnectionString { get; }
    }
}
