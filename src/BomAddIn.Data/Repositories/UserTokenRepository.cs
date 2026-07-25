using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class UserTokenRepository : IUserTokenRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserTokenRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public void Add(UserToken token)
        {
            using var conn = _connectionFactory.CreateConnection();
            token.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO UserTokens (UserId, TokenHash, ExpiresAt, CreatedAt, IsRevoked)
                  VALUES (@UserId, @TokenHash, @ExpiresAt, @CreatedAt, @IsRevoked);
                  SELECT last_insert_rowid();",
                new
                {
                    token.UserId,
                    token.TokenHash,
                    token.ExpiresAt,
                    token.CreatedAt,
                    IsRevoked = token.IsRevoked ? 1 : 0
                });
        }

        public UserToken? GetByHash(string tokenHash)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<UserToken>(
                @"SELECT * FROM UserTokens
                  WHERE TokenHash = @TokenHash AND IsRevoked = 0 AND ExpiresAt > @Now",
                new { TokenHash = tokenHash, Now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff") });
        }

        public void Revoke(string tokenHash)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                "UPDATE UserTokens SET IsRevoked = 1 WHERE TokenHash = @TokenHash",
                new { TokenHash = tokenHash });
        }

        public void RevokeAllForUser(long userId)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                "UPDATE UserTokens SET IsRevoked = 1 WHERE UserId = @UserId AND IsRevoked = 0",
                new { UserId = userId });
        }

        public int CleanupExpired(DateTime before)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Execute(
                "DELETE FROM UserTokens WHERE ExpiresAt < @Before",
                new { Before = before.ToString("yyyy-MM-dd HH:mm:ss.fff") });
        }
    }
}
