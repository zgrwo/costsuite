using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Infrastructure.Session
{
    /// <summary>
    /// 当前用户上下文实现 — 线程安全的进程单例。
    /// 所有 Excel 插件操作在单进程中执行，Singleton 生命周期匹配进程级别。
    /// </summary>
    public class CurrentUserContext : ICurrentUserContext
    {
        private User? _currentUser;
        private readonly object _lock = new();

        public long? CurrentUserId
        {
            get { lock (_lock) return _currentUser?.Id; }
        }

        public UserRole CurrentRole
        {
            get { lock (_lock) return _currentUser?.Role ?? UserRole.Viewer; }
        }

        public string? CurrentUsername
        {
            get { lock (_lock) return _currentUser?.Username; }
        }

        public bool IsAuthenticated
        {
            get { lock (_lock) return _currentUser != null; }
        }

        public void SetUser(User user)
        {
            lock (_lock) _currentUser = user ?? throw new System.ArgumentNullException(nameof(user));
        }

        public void Clear()
        {
            lock (_lock) _currentUser = null;
        }
    }
}
