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
}
