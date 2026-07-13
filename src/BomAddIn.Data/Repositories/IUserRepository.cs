using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models;

namespace BomAddIn.Data.Repositories
{
    public interface IUserRepository
    {
        User? GetById(long id);
        User? GetByUsername(string username);
        void Add(User user);
        void Update(User user);
        void UpdateLoginAttempts(long userId, int attempts, DateTime? lockoutUntil);
        int IncrementLoginAttempts(long userId);
        /// <summary>原子自增失败计数并抢占锁仓（消除 TOCTOU 竞态）。返回更新后的失败次数。</summary>
        int IncrementAndLockIfNeeded(long userId, int maxAttempts, string lockoutTime);
        IEnumerable<User> GetAll();
    }
}
