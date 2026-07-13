using System.Collections.Generic;
using System.Data;
using System.Linq;
using BomAddIn.Core.Services;
using BomAddIn.Data.Connection;
using BomAddIn.Data.Repositories;
using BomAddIn.Infrastructure.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

public class BomExcelImporterTests
{
    private readonly Mock<IMaterialRepository> _materialRepoMock = new();
    private readonly Mock<IBomNodeRepository> _bomNodeRepoMock = new();
    private readonly Mock<IDbConnectionFactory> _connectionFactoryMock = new();
    private readonly Mock<IDbConnection> _connectionMock = new();
    private readonly Mock<IDbTransaction> _transactionMock = new();
    private readonly Mock<IAuthorizationService> _authzMock = new();
    private BomExcelImporter _importer = null!;

    public BomExcelImporterTests()
    {
        _connectionFactoryMock.Setup(f => f.CreateConnection()).Returns(_connectionMock.Object);
        _connectionMock.Setup(c => c.BeginTransaction()).Returns(_transactionMock.Object);
        _importer = new BomExcelImporter(_materialRepoMock.Object, _bomNodeRepoMock.Object,
            _connectionFactoryMock.Object, _authzMock.Object);
    }

    // ── Column Mapping ──

    [Fact]
    public void DetectColumnMapping_ChineseHeaders()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "物料编码", "物料名称", "数量", "单位" });
        mapping.Should().ContainKey("ItemCode");
        mapping.Should().ContainKey("Name");
        mapping.Should().ContainKey("Quantity");
        mapping.Should().ContainKey("Unit");
    }

    [Fact]
    public void DetectColumnMapping_EnglishHeaders()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "Item Code", "Quantity", "UOM" });
        mapping["ItemCode"].Should().Be("Item Code");
        mapping["Quantity"].Should().Be("Quantity");
        mapping["Unit"].Should().Be("UOM");
    }

    [Fact]
    public void DetectColumnMapping_MixedChineseEnglish()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "物料编码", "Description", "用量", "Unit" });
        mapping["ItemCode"].Should().Be("物料编码");
        mapping["Name"].Should().Be("Description");
        mapping["Quantity"].Should().Be("用量");
        mapping["Unit"].Should().Be("Unit");
    }

    [Fact]
    public void DetectColumnMapping_EmptyHeaders_ReturnsEmpty()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "未知列A", "未知列B" });
        mapping.Should().BeEmpty();
    }

    [Fact]
    public void DetectColumnMapping_Aliases()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "料号", "描述", "Qty", "计量单位", "规格型号" });
        mapping["ItemCode"].Should().Be("料号");
        mapping["Name"].Should().Be("描述");
        mapping["Quantity"].Should().Be("Qty");
        mapping["Unit"].Should().Be("计量单位");
        mapping["Spec"].Should().Be("规格型号");
    }

    [Fact]
    public void DetectColumnMapping_CaseInsensitive()
    {
        var mapping = _importer.DetectColumnMapping(new[] { "ITEM CODE", "name", "qty" });
        mapping.Should().ContainKey("ItemCode");
        mapping.Should().ContainKey("Name");
        mapping.Should().ContainKey("Quantity");
    }

    // ── ImportMaterials ──

    [Fact]
    public void ImportMaterials_MissingItemCodeColumn_ReturnsFail()
    {
        var table = new DataTable();
        table.Columns.Add("名称");
        table.Rows.Add("Test");

        var result = _importer.ImportMaterials(table, 1);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("物料编码"));
    }

    [Fact]
    public void ImportMaterials_EmptyCode_Skipped()
    {
        var table = new DataTable();
        table.Columns.Add("物料编码");
        table.Columns.Add("物料名称");
        table.Rows.Add("", "Test");

        var result = _importer.ImportMaterials(table, 1);
        result.Warnings.Should().Contain(w => w.Contains("物料编码为空"));
    }

    [Fact]
    public void ImportMaterials_DuplicateCode_Skipped()
    {
        _materialRepoMock.Setup(r => r.GetByCode(1, "MAT-001", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(new Material { Id = 1 });

        var table = new DataTable();
        table.Columns.Add("物料编码");
        table.Columns.Add("物料名称");
        table.Rows.Add("MAT-001", "Test");

        var result = _importer.ImportMaterials(table, 1);
        result.Warnings.Should().Contain(w => w.Contains("已存在"));
    }

    [Fact]
    public void ImportMaterials_ValidRow_Added()
    {
        _materialRepoMock.Setup(r => r.GetByCode(1, "MAT-NEW", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns((Material?)null);

        var table = new DataTable();
        table.Columns.Add("物料编码");
        table.Columns.Add("物料名称");
        table.Columns.Add("规格");
        table.Columns.Add("单位");
        table.Columns.Add("类别");
        table.Rows.Add("MAT-NEW", "New Material", "Spec-01", "kg", "RawMaterial");

        var result = _importer.ImportMaterials(table, 1);
        result.SuccessCount.Should().Be(1);
        _materialRepoMock.Verify(r => r.Add(It.Is<Material>(m => m.Code == "MAT-NEW"),
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }

    // ── ImportBomStructures ──

    [Fact]
    public void ImportBomStructures_MissingParentCode_ReturnsFail()
    {
        var table = new DataTable();
        table.Columns.Add("物料编码");
        table.Columns.Add("数量");
        table.Rows.Add("CHILD", "2");

        var result = _importer.ImportBomStructures(table, 1);
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("父项编码"));
    }

    [Fact]
    public void ImportBomStructures_ParentNotFound_Error()
    {
        // R2-16: 新增 GetByCodes mock — 父物料不在批量查询结果中，触发回退到 GetByCode
        _materialRepoMock.Setup(r => r.GetByCodes(1,
                It.IsAny<HashSet<string>>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(new Dictionary<string, Material>
            {
                ["CHILD"] = new Material { Id = 2, Code = "CHILD" }
            });
        _materialRepoMock.Setup(r => r.GetByCode(1, "PARENT", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns((Material?)null);
        _materialRepoMock.Setup(r => r.GetByCode(1, "CHILD", It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(new Material { Id = 2, Code = "CHILD" });

        var table = new DataTable();
        table.Columns.Add("父项编码");
        table.Columns.Add("物料编码");
        table.Columns.Add("数量");
        table.Rows.Add("PARENT", "CHILD", "3");

        var result = _importer.ImportBomStructures(table, 1);
        result.Errors.Should().Contain(e => e.Contains("父物料"));
    }

    [Fact]
    public void ImportBomStructures_InvalidQuantity_Error()
    {
        _materialRepoMock.Setup(r => r.GetByCodes(1,
                It.IsAny<HashSet<string>>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(new Dictionary<string, Material>
            {
                ["PARENT"] = new Material { Id = 1, Code = "PARENT" },
                ["CHILD"] = new Material { Id = 2, Code = "CHILD" }
            });

        var table = new DataTable();
        table.Columns.Add("父项编码");
        table.Columns.Add("物料编码");
        table.Columns.Add("数量");
        table.Rows.Add("PARENT", "CHILD", "abc");

        var result = _importer.ImportBomStructures(table, 1);
        result.Errors.Should().Contain(e => e.Contains("数量"));
    }

    [Fact]
    public void ImportBomStructures_ValidRow_Added()
    {
        _materialRepoMock.Setup(r => r.GetByCodes(1,
                It.IsAny<HashSet<string>>(), It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()))
            .Returns(new Dictionary<string, Material>
            {
                ["P1"] = new Material { Id = 10, Code = "P1" },
                ["C1"] = new Material { Id = 20, Code = "C1" }
            });

        var table = new DataTable();
        table.Columns.Add("父项编码");
        table.Columns.Add("物料编码");
        table.Columns.Add("数量");
        table.Columns.Add("位号");
        table.Rows.Add("P1", "C1", "5", "R1");

        var result = _importer.ImportBomStructures(table, 1);
        result.SuccessCount.Should().Be(1);
        _bomNodeRepoMock.Verify(r => r.Add(It.Is<BomNode>(n =>
            n.ParentMaterialId == 10 && n.ChildMaterialId == 20 && n.Quantity == 5),
            It.IsAny<IDbConnection>(), It.IsAny<IDbTransaction>()), Times.Once);
    }
}
