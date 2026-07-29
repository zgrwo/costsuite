using System;
using System.Collections.Generic;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using BomAddIn.UDF;
using BomAddIn.UDF.Functions;
using ExcelDna.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// Unit tests for DataQueryFunctions: PRICELOOKUP, INVENTORYQTY, ORDERSTATUS UDFs.
/// </summary>
[Collection("UDF")]
public class DataQueryFunctionsTests : IDisposable
{
    // Default org ID used by all DataQueryFunctions
    private const long DefaultOrgId = 1;

    private readonly Mock<IMaterialRepository> _materialRepoMock = new();
    private readonly Mock<IPriceRecordRepository> _priceRepoMock = new();
    private readonly Mock<IInventoryRecordRepository> _inventoryRepoMock = new();
    private readonly Mock<IOrderRecordRepository> _orderRepoMock = new();
    private readonly ServiceProvider _provider;

    public DataQueryFunctionsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_materialRepoMock.Object);
        services.AddSingleton(_priceRepoMock.Object);
        services.AddSingleton(_inventoryRepoMock.Object);
        services.AddSingleton(_orderRepoMock.Object);
        _provider = services.BuildServiceProvider();
        Container.Initialize(_provider);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════
    // PRICELOOKUP
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PriceLookup_EmptyItemCode_ReturnsNA()
    {
        // Act
        var result = DataQueryFunctions.PriceLookup("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void PriceLookup_MaterialNotFound_ReturnsNA()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns((Material?)null);

        // Act
        var result = DataQueryFunctions.PriceLookup("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
        _priceRepoMock.Verify(r => r.GetLatestByMaterialId(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public void PriceLookup_NoPrice_ReturnsNA()
    {
        // Arrange
        var material = new Material { Id = 1, Code = "MAT-001", OrgId = DefaultOrgId };
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        _priceRepoMock
            .Setup(r => r.GetLatestByMaterialId(1))
            .Returns((PriceRecord?)null);

        // Act
        var result = DataQueryFunctions.PriceLookup("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void PriceLookup_WithPrice_ReturnsRoundedPrice()
    {
        // Arrange
        var material = new Material { Id = 42, Code = "MAT-001", OrgId = DefaultOrgId };
        var price = new PriceRecord { Id = 1, MaterialId = 42, UnitPrice = 123.456789m, Currency = "CNY" };

        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        _priceRepoMock
            .Setup(r => r.GetLatestByMaterialId(42))
            .Returns(price);

        // Act
        var result = DataQueryFunctions.PriceLookup("MAT-001");

        // Assert — Math.Round(123.456789, 4) = 123.4568
        result.Should().Be(Math.Round((double)price.UnitPrice, 4));
    }

    [Fact]
    public void PriceLookup_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = DataQueryFunctions.PriceLookup("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }

    // ═══════════════════════════════════════════════════════════════
    // INVENTORYQTY
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InventoryQty_EmptyItemCode_ReturnsNA()
    {
        // Act
        var result = DataQueryFunctions.InventoryQty("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void InventoryQty_MaterialNotFound_ReturnsNA()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns((Material?)null);

        // Act
        var result = DataQueryFunctions.InventoryQty("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
        _inventoryRepoMock.Verify(r => r.GetLatestByMaterialAndWarehouse(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void InventoryQty_NoMatch_ReturnsNA()
    {
        // Arrange
        var material = new Material { Id = 10, Code = "MAT-001", OrgId = DefaultOrgId };
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        // No inventory for warehouse "MAIN" — returns null
        _inventoryRepoMock
            .Setup(r => r.GetLatestByMaterialAndWarehouse(10, "MAIN"))
            .Returns((InventoryRecord?)null);

        // Act (default warehouse = "MAIN")
        var result = DataQueryFunctions.InventoryQty("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void InventoryQty_WithMatch_ReturnsQuantity()
    {
        // Arrange
        var material = new Material { Id = 10, Code = "MAT-001", OrgId = DefaultOrgId };
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        _inventoryRepoMock
            .Setup(r => r.GetLatestByMaterialAndWarehouse(10, "MAIN"))
            .Returns(new InventoryRecord { MaterialId = 10, WarehouseId = "MAIN", Quantity = 250, SnapshotDate = DateTime.Today });

        // Act
        var result = DataQueryFunctions.InventoryQty("MAT-001");

        // Assert
        result.Should().Be(250.0);
    }

    [Fact]
    public void InventoryQty_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = DataQueryFunctions.InventoryQty("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }

    // ═══════════════════════════════════════════════════════════════
    // ORDERSTATUS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void OrderStatus_EmptyItemCode_ReturnsNA()
    {
        // Act
        var result = DataQueryFunctions.OrderStatus("");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void OrderStatus_MaterialNotFound_ReturnsNA()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns((Material?)null);

        // Act
        var result = DataQueryFunctions.OrderStatus("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
        _orderRepoMock.Verify(r => r.GetByMaterialDue(It.IsAny<long>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Fact]
    public void OrderStatus_NoOrders_ReturnsNA()
    {
        // Arrange
        var material = new Material { Id = 20, Code = "MAT-001", OrgId = DefaultOrgId };
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        _orderRepoMock
            .Setup(r => r.GetByMaterialDue(20, null))
            .Returns(new List<OrderRecord>());

        // Act
        var result = DataQueryFunctions.OrderStatus("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorNA);
    }

    [Fact]
    public void OrderStatus_WithOrders_ReturnsSum()
    {
        // Arrange
        var material = new Material { Id = 20, Code = "MAT-001", OrgId = DefaultOrgId };
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Returns(material);
        _orderRepoMock
            .Setup(r => r.GetByMaterialDue(20, null))
            .Returns(new List<OrderRecord>
            {
                new() { MaterialId = 20, OrderQty = 100, DueDate = DateTime.Today.AddDays(7) },
                new() { MaterialId = 20, OrderQty = 50, DueDate = DateTime.Today.AddDays(14) },
            });

        // Act
        var result = DataQueryFunctions.OrderStatus("MAT-001");

        // Assert — Sum of OrderQty = 150
        result.Should().Be(150.0);
    }

    [Fact]
    public void OrderStatus_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _materialRepoMock
            .Setup(r => r.GetByCode(DefaultOrgId, "MAT-001"))
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = DataQueryFunctions.OrderStatus("MAT-001");

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }
}
