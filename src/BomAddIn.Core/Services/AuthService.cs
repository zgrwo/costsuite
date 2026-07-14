using System;
using System.Security.Cryptography;
using System.Text;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Infrastructure.Logging;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Security;

namespace BomAddIn.Core.Services
{
    /// <summary>认证服务 — BCrypt 密码验证 + 登录锁定 + Token 持久化</summary>
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserTokenRepository _tokenRepository;
        private readonly IPasswordHasher _passwordHasher;

        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

        public AuthService(IUserRepository userRepository, IUserTokenRepository tokenRepository,
            IPasswordHasher passwordHasher)
        {
            _userRepository = userRepository;
            _tokenRepository = tokenRepository;
            _passwordHasher = passwordHasher;
        }

        public AuthResult Authenticate(string username, string password)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));

            var user = _userRepository.GetByUsername(username);

            if (user == null)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "用户名或密码错误。"
                };
            }

            // 检查账户状态
            if (!user.IsActive)
            {
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = "账户已被禁用。请联系管理员。"
                };
            }

            // 检查锁定状态
            if (user.LockoutUntil.HasValue && user.LockoutUntil.Value > DateTime.UtcNow)
            {
                var remaining = user.LockoutUntil.Value - DateTime.UtcNow;
                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = $"账户已锁定。请在 {(int)remaining.TotalMinutes} 分钟后重试。"
                };
            }

            // 验证密码 — 原子自增+锁仓，消除 TOCTOU 竞态 (code-review C-10)
            if (!_passwordHasher.Verify(password, user.PasswordHash))
            {
                var lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                var newAttempts = _userRepository.IncrementAndLockIfNeeded(
                    user.Id, MaxFailedAttempts, lockoutUntil);

                return new AuthResult
                {
                    Success = false,
                    ErrorMessage = newAttempts >= MaxFailedAttempts
                        ? $"账户已锁定。请在 {LockoutDuration.TotalMinutes:F0} 分钟后重试。"
                        : "用户名或密码错误。"
                };
            }

            // 登录成功 — 重置失败计数，更新最后登录时间
            _userRepository.UpdateLoginAttempts(user.Id, 0, null);
            user.LastLoginAt = DateTime.UtcNow;
            _userRepository.Update(user);

            // 生成简单 Token（Sprint 2 升级为 JWT）
            var token = GenerateToken(user);

            return new AuthResult
            {
                Success = true,
                Token = token,
                User = user
            };
        }

        public void Logout(long userId)
        {
            _tokenRepository.RevokeAllForUser(userId);
        }

        public User? GetCurrentUser(long userId)
        {
            return _userRepository.GetById(userId);
        }

        public void SeedAdminUser(string? password = null)
        {
            var existing = _userRepository.GetByUsername("admin");
            if (existing != null) return; // Already seeded

            if (string.IsNullOrWhiteSpace(password))
            {
                password = Environment.GetEnvironmentVariable("BOM_ADMIN_SEED_PASSWORD");
                if (string.IsNullOrWhiteSpace(password))
                {
                    AppLogger.Warn("SeedAdminUser 未提供密码且环境变量 BOM_ADMIN_SEED_PASSWORD 未设置。使用默认密码 — 仅适用于开发环境。", typeof(AuthService));
                    password = "admin123";
                }
            }

            var admin = new User
            {
                Username = "admin",
                PasswordHash = _passwordHasher.Hash(password!),
                Role = UserRole.Admin,
                OrgId = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _userRepository.Add(admin);
        }

        private string GenerateToken(User user)
        {
            // 生成随机 Token（Sprint 5 升级为 JWT HMAC-SHA256）
            var randomBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            var token = Convert.ToBase64String(randomBytes);

            // SHA256 哈希后存入 UserTokens 表（不存明文）
            var tokenHash = HashToken(token);
            var now = DateTime.UtcNow;
            var userToken = new UserToken
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = now.Add(TokenLifetime),
                CreatedAt = now,
                IsRevoked = false
            };
            _tokenRepository.Add(userToken);

            // 清理过期 Token
            _tokenRepository.CleanupExpired(now);

            return token;
        }

        private static string HashToken(string token)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hash);
        }
    }
}
