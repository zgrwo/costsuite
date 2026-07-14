using System;
using System.Threading.Tasks;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Data.Sync;
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
    private readonly SyncService _service;

    public SyncServiceTests()
    {
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
        // C-4 fix: GetLastSyncTime 已委托给 ISyncLogRepository
        _syncLogRepoMock.Setup(r => r.GetLastSyncCompletedAt()).Returns(() => null);

        var result = _service.GetLastSyncTime();

        _syncLogRepoMock.Verify(r => r.GetLastSyncCompletedAt(), Times.Once);
        result.Should().BeNull();
    }
}
