using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class AppConfigRepository : IAppConfigRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public AppConfigRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public AppConfig? GetByKey(string key)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<AppConfig>(
                "SELECT * FROM AppConfig WHERE Key = @Key", new { Key = key });
        }

        public IEnumerable<AppConfig> GetAll()
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<AppConfig>("SELECT * FROM AppConfig ORDER BY Key");
        }

        public void Set(AppConfig config)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"INSERT INTO AppConfig (Key, Value, Description, UpdatedAt)
                  VALUES (@Key, @Value, @Description, @UpdatedAt)
                  ON CONFLICT(Key) DO UPDATE SET
                    Value=@Value, Description=@Description, UpdatedAt=@UpdatedAt",
                config);
        }

        public void Delete(string key)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                "DELETE FROM AppConfig WHERE Key = @Key", new { Key = key });
        }
    }
}
