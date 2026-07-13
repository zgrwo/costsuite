using System;
using System.Collections.Generic;
using System.Linq;
using BomAddIn.Core.Models;
using BomAddIn.Core.Services;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class VarianceServiceTests
{
    private readonly Mock<IVarianceCalculator> _calcMock = new();
    private readonly Mock<IAlertEvaluator> _alertMock = new();
    private readonly Mock<IPriceRecordRepository> _priceRepoMock = new();
    private readonly VarianceService _service;

    public VarianceServiceTests()
    {
        // C-2 fix: 默认返回空字典，避免 NRE
        _priceRepoMock
            .Setup(r => r.GetByMaterialIdsAndDate(It.IsAny<IEnumerable<long>>(), It.IsAny<DateTime>()))
            .Returns(new Dictionary<long, PriceRecord>());
        _service = new VarianceService(_calcMock.Object, _alertMock.Object, _priceRepoMock.Object);
    }

    private static BomExpandedNode Node(string code, long matId, double qty, int level) =>
        new() { ItemCode = code, MaterialId = matId, Quantity = qty, Level = level,
                 Description = "Desc", Unit = "pcs" };

    [Fact]
    public void RunFullAnalysis_ReturnsStructureVariances()
    {
        var bomA = new List<BomExpandedNode> { Node("A", 1, 1, 0) };
        var bomB = new List<BomExpandedNode> { Node("A", 1, 2, 0) };
        _calcMock.Setup(c => c.CompareBomVersions(bomA, bomB)).Returns(new List<VarianceResult> { new() });
        _alertMock.Setup(a => a.Evaluate(It.IsAny<IEnumerable<VarianceResult>>())).Returns(new List<Alert>());

        var result = _service.RunFullAnalysis(bomA, DateTime.Today, bomB, DateTime.Today);

        result.StructureVariances.Should().HaveCount(1);
    }

    [Fact]
    public void RunFullAnalysis_QueriesPrices_ForPriceVariances()
    {
        var bomA = new List<BomExpandedNode> { Node("A", 100, 1, 0) };
        var bomB = new List<BomExpandedNode> { Node("A", 100, 1, 0) };
        _calcMock.Setup(c => c.CompareBomVersions(bomA, bomB)).Returns(new List<VarianceResult>());
        _alertMock.Setup(a => a.Evaluate(It.IsAny<IEnumerable<VarianceResult>>())).Returns(new List<Alert>());

        // C-2 fix: 更新 Mock 为新的批量方法 GetByMaterialIdsAndDate
        var prices = new Dictionary<long, PriceRecord>
        {
            { 100, new PriceRecord { UnitPrice = 10.5m, EffectiveDate = DateTime.Today } }
        };
        _priceRepoMock.Setup(r => r.GetByMaterialIdsAndDate(It.IsAny<IEnumerable<long>>(), DateTime.Today))
            .Returns(prices);
        _calcMock.Setup(c => c.ComparePrices(100, 10.5m, It.IsAny<DateTime>(), It.IsAny<string>(), 10.5m, It.IsAny<DateTime>(), It.IsAny<string>()))
            .Returns(new List<VarianceResult>());

        _service.RunFullAnalysis(bomA, DateTime.Today, bomB, DateTime.Today);

        _priceRepoMock.Verify(r => r.GetByMaterialIdsAndDate(It.IsAny<IEnumerable<long>>(), DateTime.Today), Times.AtLeastOnce);
    }

    [Fact]
    public void RunFullAnalysis_EmptyBom_DoesNotCrash()
    {
        var empty = new List<BomExpandedNode>();
        _calcMock.Setup(c => c.CompareBomVersions(empty, empty)).Returns(new List<VarianceResult>());
        _alertMock.Setup(a => a.Evaluate(It.IsAny<IEnumerable<VarianceResult>>())).Returns(new List<Alert>());

        var result = _service.RunFullAnalysis(empty, DateTime.Today, empty, DateTime.Today);

        result.StructureVariances.Should().BeEmpty();
        result.PriceVariances.Should().BeEmpty();
        result.Alerts.Should().BeEmpty();
    }

    [Fact]
    public void RunFullAnalysis_GeneratesAlerts()
    {
        var bomA = new List<BomExpandedNode>();
        var bomB = new List<BomExpandedNode> { Node("X", 1, 1, 0) };
        var variances = new List<VarianceResult> { new() { ChangeType = VarianceChangeType.Added } };
        var alerts = new List<Alert> { new() { Message = "Test alert" } };

        _calcMock.Setup(c => c.CompareBomVersions(bomB, bomB)).Returns(variances);
        _alertMock.Setup(a => a.Evaluate(It.IsAny<IEnumerable<VarianceResult>>())).Returns(alerts);

        var result = _service.RunFullAnalysis(bomB, DateTime.Today, bomB, DateTime.Today);

        result.Alerts.Should().HaveCount(1);
    }

    [Fact]
    public void RunFullAnalysis_MultipleMaterials_PricesQueriedPerMaterial()
    {
        var bom = new List<BomExpandedNode> { Node("A", 1, 1, 0), Node("B", 2, 2, 1), Node("C", 3, 1, 2) };
        _calcMock.Setup(c => c.CompareBomVersions(bom, bom)).Returns(new List<VarianceResult>());
        _alertMock.Setup(a => a.Evaluate(It.IsAny<IEnumerable<VarianceResult>>())).Returns(new List<Alert>());

        _service.RunFullAnalysis(bom, DateTime.Today, bom, DateTime.Today);

        // C-2 fix: 每个日期调用一次批量方法（而非每个物料逐一查询）
        _priceRepoMock.Verify(r => r.GetByMaterialIdsAndDate(It.IsAny<IEnumerable<long>>(), DateTime.Today), Times.AtLeast(2));
    }
}
