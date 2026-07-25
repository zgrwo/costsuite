using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>BOM 操作权限枚举 — 对应 RBAC 角色检查</summary>
    public enum BomOperation
    {
        // Materials
        MaterialRead,
        MaterialCreate,
        MaterialUpdate,
        MaterialDelete,

        // BOM
        BomRead,
        BomCreate,
        BomUpdate,
        BomDelete,

        // Suppliers
        SupplierRead,
        SupplierCreate,
        SupplierUpdate,
        SupplierDelete,

        // Config
        ConfigRead,
        ConfigUpdate,

        // Users (Admin only)
        UserManage,

        // Sync
        SyncTrigger,

        // Approval
        BomApprove,
        BomReject,
        BomRelease,
        BomObsolete
    }

    /// <summary>RBAC 授权服务接口</summary>
    public interface IAuthorizationService
    {
        /// <summary>检查指定角色是否拥有某项操作权限</summary>
        bool Authorize(UserRole role, BomOperation operation);

        /// <summary>要求权限 — 无权限时抛出 UnauthorizedAccessException</summary>
        void Demand(UserRole role, BomOperation operation);

        /// <summary>是否为管理员角色</summary>
        bool IsAdmin(UserRole role);
    }
}
