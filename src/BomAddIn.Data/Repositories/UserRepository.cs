using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Data.Connection;
using Dapper;

namespace BomAddIn.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public UserRepository(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public User? GetById(long id)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = id });
        }

        public User? GetByUsername(string username)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.QueryFirstOrDefault<User>(
                "SELECT * FROM Users WHERE Username = @Username",
                new { Username = username });
        }

        public void Add(User user)
        {
            using var conn = _connectionFactory.CreateConnection();
            user.Id = conn.ExecuteScalar<long>(
                @"INSERT INTO Users (Username, PasswordHash, Role, OrgId, IsActive, FailedLoginAttempts, CreatedAt)
                  VALUES (@Username, @PasswordHash, @Role, @OrgId, @IsActive, @FailedLoginAttempts, @CreatedAt);
                  SELECT last_insert_rowid();",
                new
                {
                    user.Username,
                    user.PasswordHash,
                    Role = user.Role.ToString(),
                    user.OrgId,
                    user.IsActive,
                    user.FailedLoginAttempts,
                    user.CreatedAt
                });
        }

        public void Update(User user)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE Users SET Username=@Username, PasswordHash=@PasswordHash,
                  Role=@Role, OrgId=@OrgId, IsActive=@IsActive, LastLoginAt=@LastLoginAt
                  WHERE Id=@Id",
                new
                {
                    user.Username,
                    user.PasswordHash,
                    Role = user.Role.ToString(),
                    user.OrgId,
                    user.IsActive,
                    user.LastLoginAt,
                    user.Id
                });
        }

        public void UpdateLoginAttempts(long userId, int attempts, DateTime? lockoutUntil)
        {
            using var conn = _connectionFactory.CreateConnection();
            conn.Execute(
                @"UPDATE Users SET FailedLoginAttempts=@Attempts, LockoutUntil=@LockoutUntil
                  WHERE Id=@Id",
                new { Id = userId, Attempts = attempts, LockoutUntil = lockoutUntil });
        }

        public int IncrementLoginAttempts(long userId)
        {
            // 原子自增 + 返回新值 + 检查是否需要锁仓（单条 SQL，消除 TOCTOU 竞态）
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<int>(
                @"UPDATE Users SET FailedLoginAttempts = FailedLoginAttempts + 1
                  WHERE Id = @Id;
                  SELECT FailedLoginAttempts FROM Users WHERE Id = @Id;",
                new { Id = userId });
        }

        /// <summary>
        /// 原子操作：自增失败计数并抢占锁仓。
        /// 单条 SQL 中完成"自增 → 判断 → 锁仓"，消除 TOCTOU 竞态窗口（code-review C-10）。
        /// 返回更新后的失败次数。
        /// </summary>
        public int IncrementAndLockIfNeeded(long userId, int maxAttempts, string lockoutTime)
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.ExecuteScalar<int>(
                @"UPDATE Users SET
                    FailedLoginAttempts = FailedLoginAttempts + 1,
                    LockoutUntil = CASE WHEN FailedLoginAttempts + 1 >= @MaxAttempts THEN @LockoutTime ELSE LockoutUntil END
                  WHERE Id = @Id;
                  SELECT FailedLoginAttempts FROM Users WHERE Id = @Id;",
                new { Id = userId, MaxAttempts = maxAttempts, LockoutTime = lockoutTime });
        }

        public IEnumerable<User> GetAll()
        {
            using var conn = _connectionFactory.CreateConnection();
            return conn.Query<User>("SELECT * FROM Users");
        }
    }
}
