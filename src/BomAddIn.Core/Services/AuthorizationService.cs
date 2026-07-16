using System;
using System.Collections.Generic;
using BomAddIn.Infrastructure.Models.Enums;

namespace BomAddIn.Core.Services
{
    /// <summary>RBAC 授权服务 — V1.0 基于角色的简单权限映射</summary>
    /// <remarks>
    /// V1.0 仅 3 个角色，不使用完整权限矩阵。
    /// 角色→操作映射在此集中定义，便于审计和修改。
    /// </remarks>
    public class AuthorizationService : IAuthorizationService
    {
        private static readonly Dictionary<UserRole, HashSet<BomOperation>> RolePermissions =
            new Dictionary<UserRole, HashSet<BomOperation>>
            {
                [UserRole.Admin] = new HashSet<BomOperation>
                {
                    // 全部操作
                    BomOperation.MaterialRead, BomOperation.MaterialCreate,
                    BomOperation.MaterialUpdate, BomOperation.MaterialDelete,
                    BomOperation.BomRead, BomOperation.BomCreate,
                    BomOperation.BomUpdate, BomOperation.BomDelete,
                    BomOperation.SupplierRead, BomOperation.SupplierCreate,
                    BomOperation.SupplierUpdate, BomOperation.SupplierDelete,
                    BomOperation.ConfigRead, BomOperation.ConfigUpdate,
                    BomOperation.UserManage,
                    BomOperation.SyncTrigger,
                    BomOperation.BomApprove, BomOperation.BomReject,
                    BomOperation.BomRelease, BomOperation.BomObsolete
                },
                [UserRole.Analyst] = new HashSet<BomOperation>
                {
                    BomOperation.MaterialRead,
                    BomOperation.MaterialCreate, BomOperation.MaterialUpdate,
                    BomOperation.BomRead, BomOperation.BomCreate, BomOperation.BomUpdate,
                    BomOperation.SupplierRead,
                    BomOperation.SupplierCreate, BomOperation.SupplierUpdate,
                    BomOperation.ConfigRead,
                    BomOperation.SyncTrigger,
                    BomOperation.BomApprove, BomOperation.BomReject
                },
                [UserRole.Viewer] = new HashSet<BomOperation>
                {
                    BomOperation.MaterialRead,
                    BomOperation.BomRead,
                    BomOperation.SupplierRead,
                    BomOperation.ConfigRead
                }
            };

        public bool Authorize(UserRole role, BomOperation operation)
        {
            return RolePermissions.TryGetValue(role, out var permissions)
                   && permissions.Contains(operation);
        }

        /// <summary>
        /// 权限断言。V1.0 信任边界：调用方（UDF/UI/Dashboard）负责从受信上下文（登录 Token）
        /// 解析当前用户角色后传入。此方法本身不验证调用者身份——它依赖调用方正确传递角色。
        /// V2.0 建议：引入 ICurrentUserContext 服务，从 Token/Session 解析角色，消除参数化角色传递。
        /// </summary>
        public void Demand(UserRole role, BomOperation operation)
        {
            if (!Authorize(role, operation))
            {
                throw new UnauthorizedAccessException(
                    $"角色 '{role}' 没有执行 '{operation}' 操作的权限。");
            }
        }

        public bool IsAdmin(UserRole role)
        {
            return role == UserRole.Admin;
        }
    }
}
