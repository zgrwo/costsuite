using System;
using System.Threading.Tasks;

namespace BomAddIn.Infrastructure.Network
{
    /// <summary>网络监控接口 — ERP 连通性检测</summary>
    public interface INetworkMonitor
    {
        bool IsNetworkAvailable();
        Task<bool> ProbeConnectionAsync();
        bool IsConsideredOffline { get; }

        /// <summary>连通性变化事件 (V1.2 🔜 — 当前 SyncService 使用轮询方式)</summary>
        event EventHandler<bool>? ConnectivityChanged;
    }
}
