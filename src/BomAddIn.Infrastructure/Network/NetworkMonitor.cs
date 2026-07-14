using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using BomAddIn.Infrastructure.Config;

namespace BomAddIn.Infrastructure.Network
{
    /// <summary>网络监控 — 被动检测 + 主动探测双策略</summary>
    public class NetworkMonitor : INetworkMonitor, IDisposable
    {
#pragma warning disable CS0067 // V1.2 占位事件，当前未被订阅
        /// <summary>连通性变化事件 (V1.2 🔜 — 当前 SyncService 使用轮询)</summary>
        public event EventHandler<bool>? ConnectivityChanged;
#pragma warning restore CS0067
        private readonly object _probeLock = new();
        private DateTime _lastSuccessfulProbe = DateTime.MinValue;
        private readonly HttpClient _httpClient;
        private readonly string _probeUrl;

        public NetworkMonitor() : this(new AppConfigProvider())
        {
        }

        public NetworkMonitor(IConfigProvider configProvider)
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _probeUrl = configProvider.Get("ErpApi:HealthCheckUrl");
            if (string.IsNullOrWhiteSpace(_probeUrl))
                _probeUrl = "https://erp.example.com/api/health";
        }

        public bool IsNetworkAvailable() =>
            NetworkInterface.GetIsNetworkAvailable();

        public async Task<bool> ProbeConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(_probeUrl);
                if (response.IsSuccessStatusCode)
                {
                    lock (_probeLock) { _lastSuccessfulProbe = DateTime.UtcNow; }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logging.AppLogger.Debug($"网络探测失败: {ex.Message}", typeof(NetworkMonitor));
                return false;
            }
        }

        /// <summary>容错判断：最近 60 秒内有一次成功探测 → 认为在线</summary>
        public bool IsConsideredOffline
        {
            get { lock (_probeLock) { return (DateTime.UtcNow - _lastSuccessfulProbe) > TimeSpan.FromSeconds(60); } }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
