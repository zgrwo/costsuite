using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Session
{
    /// <summary>
    /// 当前用户上下文 — 跨层获取已登录用户身份。
    /// Singleton 注册，在 AuthService.Authenticate 成功时写入，Logout 时清除。
    /// V1.1 引入以解决 H-1/H-2（各 UI 层硬编码 userId 导致同步功能失效或绕过认证）。
    /// </summary>
    public interface ICurrentUserContext
    {
        /// <summary>当前用户 ID，未登录时为 null</summary>
        long? CurrentUserId { get; }

        /// <summary>当前用户角色，未登录时回退 Viewer（最小权限原则）</summary>
        UserRole CurrentRole { get; }

        /// <summary>当前用户名，未登录时为 null</summary>
        string? CurrentUsername { get; }

        /// <summary>是否已通过认证</summary>
        bool IsAuthenticated { get; }

        /// <summary>登录成功时设置当前用户</summary>
        void SetUser(User user);

        /// <summary>登出时清除当前用户</summary>
        void Clear();
    }
}
