using System;
using BCrypt.Net;

namespace BomAddIn.Infrastructure.Security
{
    /// <summary>BCrypt 密码哈希器 — work factor ≥ 12</summary>
    public class BCryptPasswordHasher : IPasswordHasher
    {
        private const int WorkFactor = 12;

        public string Hash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        public bool Verify(string password, string hash)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
    }
}
