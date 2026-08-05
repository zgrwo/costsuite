---
description: "离线优先架构 — SQLite 缓存、ERP 同步策略、网络降级、数据同步冲突处理。"
name: "离线优先架构"
---

# Skill: 离线优先架构

> **TRIGGER**: 修改 `src/BomAddIn.Core/Services/SyncService.cs`、`src/BomAddIn.Data/Sync/`、`src/BomAddIn.Infrastructure/Network/`、`src/BomAddIn.Data/Caching/` 或任何离线/同步/缓存逻辑时，**必须**先读此 Skill。

> **核心变更**: SQLite + DuckDB 本地主库意味着"数据库永远在线"。离线仅意味着"ERP 同步暂停"——这与传统服务端数据库的离线完全不同。

> **来源**: Excel-DNA 社区实践、SQLite 同步模式研究、本项目 [specification.md §10](../../../rules/specification.md#10-离线模式规格)  
> **v1.1 更新**: 数据存储改为 SQLite 本地主库 + DuckDB 分析引擎。离线仅是 ERP 同步暂停，本地读写始终可用。  
> **适用范围**: 离线模式、SQLite 缓存、网络检测、同步策略

---

## 1. 设计哲学：Offline-First

```text
核心原则：
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│   用户永远不应该因为网络问题而无法查看数据。                   │
│                                                             │
│   离线不是"故障模式" — 它是正常的运行模式之一。               │
│                                                             │
│   V1.1 架构：离线 = 本地读写 + ERP 同步暂停。               │
│   V2.0 目标：离线 = 读写 + 自动冲突合并。                    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## 2. 状态机实现

```text
                    ┌─────────────┐
         启动 ──────►│   初始化     │
                    └──────┬──────┘
                           │
                ┌──────────▼──────────┐
                │   网络检测结果？      │
                └──────┬─────┬────────┘
                       │     │
              在线     │     │  离线
                       ▼     ▼
          ┌─────────────┐   ┌─────────────┐
          │  ONLINE      │   │  SyncPaused  │
          │              │   │              │
          │ • 读写数据库  │──►│ • SQLite 读写 │
          │ • 实时同步    │网络│ • 同步暂停    │
          │ • 完整功能    │断开│ • 本地操作正常 │
          └──────┬───────┘   └──────┬───────┘
                 │                  │
                 │  网络恢复         │
                 └──────────────────┘
                      自动切换
```

```csharp
public class ConnectionStateMachine
{
    private ConnectionState _current = ConnectionState.Initializing;

    public ConnectionState Current
    {
        get => _current;
        private set
        {
            if (_current != value)
            {
                var oldState = _current;
                _current = value;
                OnStateChanged(oldState, value);
            }
        }
    }

    public async Task EvaluateAsync(INetworkMonitor monitor)
    {
        bool isOnline = await monitor.ProbeConnectionAsync();

        Current = isOnline ? ConnectionState.Online : ConnectionState.SyncPaused;
    }

    private void OnStateChanged(ConnectionState from, ConnectionState to)
    {
        var eventBus = BomAddInStartup.ServiceProvider.GetRequiredService<IEventBus>();

        if (to == ConnectionState.SyncPaused)
        {
            // 同步暂停，本地操作正常
            eventBus.Publish(new OfflineModeChangedEvent { Timestamp = DateTime.UtcNow, IsOffline = true });

            // 显示水印：TaskPane + Dashboard
            LogManager.GetCurrentClassLogger().Warn("ERP 同步已暂停");
        }
        else if (to == ConnectionState.Online && from == ConnectionState.SyncPaused)
        {
            // 恢复 UI
            eventBus.Publish(new OfflineModeChangedEvent { Timestamp = DateTime.UtcNow, IsOffline = false });

            // 触发缓存刷新
            Task.Run(async () =>
            {
                var syncService = BomAddInStartup.ServiceProvider.GetRequiredService<ISyncService>();
                await syncService.RefreshCacheAsync();
            });

            LogManager.GetCurrentClassLogger().Info("已恢复在线模式，缓存已刷新");
        }
    }
}

public enum ConnectionState
{
    Initializing,
    Online,
    SyncPaused
}
```

## 3. 网络检测：两种策略互补

```csharp
public class NetworkMonitor : INetworkMonitor, IDisposable
{
    private readonly Timer _pollTimer;
    private bool _lastKnownState = true;
    private DateTime _lastSuccessfulProbe = DateTime.MinValue;

    public NetworkMonitor()
    {
        // 策略 1: 被动检测 — Windows Network List Manager
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        // 策略 2: 主动探测 — 定时 ping API 端点（更可靠）
        _pollTimer = new Timer(async _ => await ProbeAsync(), null,
            TimeSpan.FromSeconds(30),  // 初始延迟
            TimeSpan.FromSeconds(30)); // 间隔
    }

    private async void OnNetworkAvailabilityChanged(object sender,
        NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
        {
            // 网络恢复信号 — 但可能只是 WiFi 连上了，实际 API 还不通
            // 延迟 3 秒再探测
            await Task.Delay(3000);
        }
        await ProbeAsync();
    }

    public async Task<bool> ProbeConnectionAsync()
    {
        try
        {
            var config = BomAddInStartup.ServiceProvider.GetRequiredService<IConfigProvider>();
            var healthUrl = config.Get("ErpApi:HealthCheckUrl");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(healthUrl);

            _lastSuccessfulProbe = DateTime.UtcNow;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // 判断"离线"的容错策略：不是断一次就切，而是持续超时
    public bool IsConsideredOffline
    {
        get
        {
            // 最近 60 秒内至少有一次成功探测 → 认为在线
            return (DateTime.UtcNow - _lastSuccessfulProbe) > TimeSpan.FromSeconds(60);
        }
    }

    public void Dispose() => _pollTimer?.Dispose();
}
```

**关键设计**: 不要因一次网络抖动就切到离线模式。用 "最近 N 秒内是否有成功探测" 作为判断标准，避免频繁切换。

## 4. SQLite 缓存策略

### 4.1 Schema 设计

```sql
-- 本地 SQLite 缓存表 = 业务表镜像 + 同步元数据
CREATE TABLE Cache_Materials (
    -- 与 Materials 表字段完全一致
    Id INTEGER PRIMARY KEY,
    OrgId INTEGER NOT NULL,
    Code TEXT NOT NULL,
    Name TEXT NOT NULL,
    Spec TEXT,
    Unit TEXT,
    Category TEXT,
    IsActive INTEGER DEFAULT 1,

    -- 同步元数据
    LastSyncedAt TEXT NOT NULL DEFAULT (datetime('now')),
    ServerRowVersion TEXT,           -- 服务器端版本号（用于增量同步）
    UNIQUE(OrgId, Code)
);

-- 缓存元数据表
CREATE TABLE CacheMetadata (
    TableName TEXT PRIMARY KEY,
    LastSyncTimestamp TEXT NOT NULL,
    RecordCount INTEGER NOT NULL DEFAULT 0,
    SyncStatus TEXT NOT NULL DEFAULT 'Never',
    -- SyncStatus: 'Complete', 'Partial', 'Error', 'Never'
    LastSyncError TEXT
);
```

### 4.2 缓存刷新流程

```csharp
public class SqliteCacheProvider : ISqliteCacheProvider
{
    public async Task RefreshAsync(string tableName,
        IEnumerable<object> serverData,
        DateTime syncTimestamp)
    {
        using var conn = new SQLiteConnection(_connectionString);
        conn.Open();

        using var tx = conn.BeginTransaction();

        try
        {
            // 1. 清空旧缓存
            conn.Execute($"DELETE FROM Cache_{tableName}");

            // 2. 批量插入新数据（事务内，速度最快）
            //    使用 Dapper 或原生批量 INSERT
            using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildBulkInsertSql(tableName);
            // ... 参数化批量插入

            // 3. 更新元数据
            conn.Execute(
                @"INSERT OR REPLACE INTO CacheMetadata (TableName, LastSyncTimestamp, RecordCount, SyncStatus)
                  VALUES (@table, @ts, @count, 'Complete')",
                new { table = tableName, ts = syncTimestamp, count = serverData.Count() });

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            // 缓存刷新失败不抛异常 — 用户仍可使用旧缓存
            conn.Execute(
                @"UPDATE CacheMetadata SET SyncStatus = 'Error', LastSyncError = @err
                  WHERE TableName = @table",
                new { table = tableName, err = $"刷新失败 @ {DateTime.UtcNow}" });
        }
    }

    public IEnumerable<T> ReadCache<T>(string tableName) where T : class
    {
        using var conn = new SQLiteConnection(_connectionString);
        return conn.Query<T>($"SELECT * FROM Cache_{tableName}");
    }
}
```

### 4.3 缓存位置与安全

```csharp
public static string GetCacheFilePath()
{
    // 放在 LocalAppData — 用户隔离、不污染文档目录
    var appData = Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData);
    var cacheDir = Path.Combine(appData, "BomAddIn", "Cache");

    Directory.CreateDirectory(cacheDir);

    var fileName = $"bom_cache_{GetCurrentOrgId()}.sqlite";
    return Path.Combine(cacheDir, fileName);
}
```

### 4.4 缓存过期策略

```csharp
public class CacheValidityChecker
{
    public bool IsStale(string tableName)
    {
        var conn = GetCacheConnection();
        var meta = conn.QueryFirstOrDefault<CacheMetadata>(
            "SELECT * FROM CacheMetadata WHERE TableName = @table",
            new { table = tableName });

        if (meta == null || meta.SyncStatus == "Never")
            return true;  // 从未同步

        var maxAge = GetMaxAge(tableName);  // 不同表不同过期时间

        return (DateTime.UtcNow - meta.LastSyncTimestamp) > maxAge;
    }

    private TimeSpan GetMaxAge(string tableName) => tableName switch
    {
        "Materials" => TimeSpan.FromHours(24),
        "Prices" => TimeSpan.FromHours(4),      // 价格变动更频繁
        "Inventories" => TimeSpan.FromHours(4),
        "BomStructures" => TimeSpan.FromHours(12),
        _ => TimeSpan.FromHours(24)
    };
}
```

## 5. V2.0 离线编辑的前置设计（V1.1 已实现本地读写）

V1.1 已支持本地读写，V2.0 增加双向同步冲突解决。V1.0 设计时保留以下扩展点：

```sql
-- V1.0 预留列（V2.0 激活）
ALTER TABLE Cache_Materials ADD COLUMN IsDirty INTEGER DEFAULT 0;
ALTER TABLE Cache_Materials ADD COLUMN LocalModifiedAt TEXT;
ALTER TABLE Cache_Materials ADD COLUMN ConflictState TEXT DEFAULT 'None';
-- ConflictState: 'None', 'LocalModified', 'ServerModified', 'Conflict'

-- V2.0 变更追踪表
CREATE TABLE IF NOT EXISTS ChangeLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TableName TEXT NOT NULL,
    RecordId INTEGER NOT NULL,
    Action TEXT NOT NULL,  -- 'INSERT', 'UPDATE', 'DELETE'
    OldValues TEXT,        -- JSON
    NewValues TEXT,        -- JSON
    CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
    SyncStatus TEXT DEFAULT 'Pending'  -- 'Pending', 'Synced', 'Conflict'
);
```

## 6. 自检清单

- [ ] 离线切换有容错延迟（不因单次网络抖动切换）
- [ ] 所有编辑按钮在离线时 `IsEnabled = false`（不仅是隐藏）
- [ ] UI 中有明确的水印/状态栏显示"离线数据 (更新于: yyyy-MM-dd HH:mm)"
- [ ] SQLite 文件放在 `LocalAppData`（用户隔离、不受 Excel 文件移动影响）
- [ ] 缓存刷新在事务中执行（失败回滚，不影响旧缓存）
- [ ] 不同表有不同缓存过期时间（价格比物料更频繁变化）
- [ ] V2.0 的离线编辑扩展点已预留（`IsDirty`, `ChangeLog`, `ConflictState`）
- [ ] 网络恢复后自动触发增量同步（不是全量重新拉取）

## 7. 参考

- [SQLite with Excel-DNA: 性能优化指南](https://www.sqlite.org/wal.html)（WAL 模式支持并发读）
- [Polly: .NET Resilience Framework](https://github.com/App-vNext/Polly)
- [Dapper: Lightweight ORM](https://github.com/DapperLib/Dapper)
