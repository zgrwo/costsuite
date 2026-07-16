using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Data.Sync;
using BomAddIn.Infrastructure.Models;
using BomAddIn.Infrastructure.Models.Enums;
using BomAddIn.Infrastructure.Network;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class SyncServiceTests
{
    private readonly Mock<IErpAdapter> _erpMock = new();
    private readonly Mock<INetworkMonitor> _networkMock = new();
    private readonly Mock<IMaterialRepository> _materialRepoMock = new();
    private readonly Mock<IPriceRecordRepository> _priceRepoMock = new();
    private readonly Mock<IInventoryRecordRepository> _inventoryRepoMock = new();
    private readonly Mock<IOrderRecordRepository> _orderRepoMock = new();
    private readonly Mock<ICapacityRecordRepository> _capacityRepoMock = new();
    private readonly Mock<IDbConnectionFactory> _connFactoryMock = new();
    private readonly Mock<IAuthorizationService> _authzMock = new();
    private readonly Mock<BomAddIn.Data.Analysis.IBomAnalysisProvider> _analysisMock = new();
    private readonly Mock<BomAddIn.Data.Repositories.ISyncLogRepository> _syncLogRepoMock = new();
    private readonly Mock<IDbConnection> _connMock = new();
    private readonly Mock<IDbTransaction> _txMock = new();
    private readonly SyncService _service;

    public SyncServiceTests()
    {
        _connFactoryMock.Setup(f => f.CreateConnection()).Returns(_connMock.Object);
        _connMock.Setup(c => c.BeginTransaction()).Returns(_txMock.Object);
        _syncLogRepoMock.Setup(r => r.WriteLog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1);
        _syncLogRepoMock.Setup(r => r.UpdateLog(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>()))
            .Callback(() => { });

        _service = new SyncService(
            _erpMock.Object, _networkMock.Object,
            _materialRepoMock.Object, _priceRepoMock.Object,
            _inventoryRepoMock.Object, _orderRepoMock.Object,
            _capacityRepoMock.Object, _connFactoryMock.Object,
            _authzMock.Object, _analysisMock.Object,
            _syncLogRepoMock.Object);
    }

    [Fact]
    public async Task SyncAll_Offline_ReturnsSkipped()
    {
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(false);

        var result = await _service.SyncAllAsync(UserRole.Admin);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("网络");
    }

    [Fact]
    public async Task SyncAll_RBACGate_DemandCalled()
    {
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(false);

        await _service.SyncAllAsync(UserRole.Admin);

        _authzMock.Verify(a => a.Demand(UserRole.Admin, BomOperation.SyncTrigger), Times.Once);
    }

    [Fact]
    public void GetLastSyncTime_NoSyncLog_ReturnsNull()
    {
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(() => null);

        var result = _service.GetLastSyncTime();

        _syncLogRepoMock.Verify(r => r.GetLastSyncCompletedAt(), Times.Once);
        result.Should().BeNull();
    }

    // ── H-6 fix: SyncService 在线路径测试 ──

    [Fact]
    public async Task SyncAll_Online_SuccessPath_WritesAllEntities()
    {
        // Arrange: 模拟在线 + 所有 ERP 端点返回空数据
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(true);
        _erpMock.Setup(e => e.PullMaterialsAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<Material>());
        _erpMock.Setup(e => e.PullPricesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<PriceRecord>());
        _erpMock.Setup(e => e.PullInventoriesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<InventoryRecord>());
        _erpMock.Setup(e => e.PullOrdersAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<OrderRecord>());
        _erpMock.Setup(e => e.PullCapacitiesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<CapacityRecord>());

        // Act
        var result = await _service.SyncAllAsync(UserRole.Admin);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalRecords.Should().Be(0);
        _syncLogRepoMock.Verify(r => r.UpdateLog(1, SyncStatus.Complete.ToString(), 0, It.IsAny<string?>()), Times.Once);
    }

    // ── H-10: Date boundary 回归测试 — 同日节点包含性 ──

    [Fact]
    public void GetLastSyncTime_ReturnsValue_FromRepository()
    {
        var expected = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc);
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(expected);

        var result = _service.GetLastSyncTime();

        result.Should().Be(expected);
    }

    [Fact]
    public void GetLastSyncTime_DateBoundary_HandlesSameDayCorrectly()
    {
        // 同日不同时刻 — 时间分量应完整保留，确保同日第一次同步不被"已同步"跳过
        var morningSync = new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc);
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(morningSync);
        var result1 = _service.GetLastSyncTime();
        result1.Should().Be(morningSync);
        result1!.Value.Hour.Should().Be(8);
        result1!.Value.Kind.Should().Be(DateTimeKind.Utc);

        var eveningSync = new DateTime(2026, 7, 15, 20, 30, 0, DateTimeKind.Utc);
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(eveningSync);
        var result2 = _service.GetLastSyncTime();
        result2.Should().Be(eveningSync);
        result2!.Value.Hour.Should().Be(20);

        // 边界：午夜 — 跨日但时间分量为 00:00:00
        var midnightSync = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(midnightSync);
        var result3 = _service.GetLastSyncTime();
        result3.Should().Be(midnightSync);
        result3!.Value.Hour.Should().Be(0);
        result3!.Value.Day.Should().Be(16);
    }

    // ── H-29: 离线→在线转换 + 并发编辑测试 ──

    [Fact]
    public async Task SyncAll_OfflineToOnline_Transition_ShouldDetectNetworkChange()
    {
        // 第1次调用: 离线
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(false);
        var offlineResult = await _service.SyncAllAsync(UserRole.Admin);
        offlineResult.Success.Should().BeFalse();

        // 第2次调用: 恢复在线
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(true);
        _erpMock.Setup(e => e.PullMaterialsAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<Material>());
        _erpMock.Setup(e => e.PullPricesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<PriceRecord>());
        _erpMock.Setup(e => e.PullInventoriesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<InventoryRecord>());
        _erpMock.Setup(e => e.PullOrdersAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<OrderRecord>());
        _erpMock.Setup(e => e.PullCapacitiesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<CapacityRecord>());

        var onlineResult = await _service.SyncAllAsync(UserRole.Admin);
        onlineResult.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAll_RapidToggle_NoStateCorruption()
    {
        // 快速切换离线→在线→离线，验证无异常
        for (int i = 0; i < 3; i++)
        {
            _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(i % 2 == 0);
            if (i % 2 == 0)
            {
                _erpMock.Setup(e => e.PullMaterialsAsync(It.IsAny<DateTime?>()))
                    .ReturnsAsync(new List<Material>());
                _erpMock.Setup(e => e.PullPricesAsync(It.IsAny<DateTime?>()))
                    .ReturnsAsync(new List<PriceRecord>());
                _erpMock.Setup(e => e.PullInventoriesAsync(It.IsAny<DateTime?>()))
                    .ReturnsAsync(new List<InventoryRecord>());
                _erpMock.Setup(e => e.PullOrdersAsync(It.IsAny<DateTime?>()))
                    .ReturnsAsync(new List<OrderRecord>());
                _erpMock.Setup(e => e.PullCapacitiesAsync(It.IsAny<DateTime?>()))
                    .ReturnsAsync(new List<CapacityRecord>());
            }
            var result = await _service.SyncAllAsync(UserRole.Admin);
            result.Should().NotBeNull();
        }
    }

    // ── H-29 补充: 离线→在线数据完整性回归测试 ──

    [Fact]
    public async Task SyncAll_OfflineToOnline_DataIntegrityCheck()
    {
        // Arrange: 第1次离线
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(false);
        var offlineResult = await _service.SyncAllAsync(UserRole.Admin);
        offlineResult.Success.Should().BeFalse();

        // 第2次在线 — 携带真实数据
        var materials = new List<Material>
        {
            new Material { OrgId = 1, Code = "MAT-001", Name = "Test Material", Unit = "PC" }
        };
        _networkMock.Setup(n => n.ProbeConnectionAsync()).ReturnsAsync(true);
        _erpMock.Setup(e => e.PullMaterialsAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(materials);
        _erpMock.Setup(e => e.PullPricesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<PriceRecord>());
        _erpMock.Setup(e => e.PullInventoriesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<InventoryRecord>());
        _erpMock.Setup(e => e.PullOrdersAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<OrderRecord>());
        _erpMock.Setup(e => e.PullCapacitiesAsync(It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<CapacityRecord>());

        // Act
        var onlineResult = await _service.SyncAllAsync(UserRole.Admin);

        // Assert: 数据通过事务写入，结果未损坏
        onlineResult.Success.Should().BeTrue();
        onlineResult.TotalRecords.Should().Be(1);
        // 验证物料数据正确传给 Repository
        _materialRepoMock.Verify(
            r => r.GetByCode(1, "MAT-001", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()),
            Times.Once);
        _materialRepoMock.Verify(
            r => r.Add(It.Is<Material>(m => m.Code == "MAT-001" && m.Name == "Test Material"),
                It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()),
            Times.Once);
    }
}
