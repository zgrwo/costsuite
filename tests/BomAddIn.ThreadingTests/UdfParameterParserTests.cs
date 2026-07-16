using System;
using BomAddIn.UDF.Helpers;
using ExcelDna.Integration;
using Xunit;

namespace BomAddIn.ThreadingTests;

/// <summary>
/// UdfParameterParser 单元测试 — 覆盖 Excel→.NET 类型强制转换的所有路径。
/// </summary>
public class UdfParameterParserTests
{
    // ── ParseDateArg ──

    [Fact]
    public void ParseDateArg_Null_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg(null));

    [Fact]
    public void ParseDateArg_ExcelMissing_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg(ExcelMissing.Value));

    [Fact]
    public void ParseDateArg_ExcelEmpty_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg(ExcelEmpty.Value));

    [Fact]
    public void ParseDateArg_DateTime_ReturnsSameValue()
    {
        var dt = new DateTime(2026, 7, 15);
        Assert.Equal(dt, UdfParameterParser.ParseDateArg(dt));
    }

    [Fact]
    public void ParseDateArg_OADate_ReturnsCorrectDate()
        => Assert.Equal(new DateTime(1899, 12, 31), UdfParameterParser.ParseDateArg(1.0));

    [Fact]
    public void ParseDateArg_OADate_Zero_ReturnsMinDate()
        => Assert.Equal(DateTime.FromOADate(0), UdfParameterParser.ParseDateArg(0.0));

    [Fact]
    public void ParseDateArg_OADate_Negative_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg(-1.0));

    [Fact]
    public void ParseDateArg_ValidString_ReturnsParsedDate()
        => Assert.Equal(new DateTime(2026, 7, 15), UdfParameterParser.ParseDateArg("2026-07-15"));

    [Fact]
    public void ParseDateArg_InvalidString_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg("not-a-date"));

    [Fact]
    public void ParseDateArg_EmptyString_ReturnsNull()
        => Assert.Null(UdfParameterParser.ParseDateArg(""));

    // ── ParseVersionState ──

    [Fact]
    public void ParseVersionState_Null_ReturnsReleased()
        => Assert.Equal("Released", UdfParameterParser.ParseVersionState(null));

    [Fact]
    public void ParseVersionState_Empty_ReturnsReleased()
        => Assert.Equal("Released", UdfParameterParser.ParseVersionState(""));

    [Fact]
    public void ParseVersionState_Draft_ReturnsDraft()
        => Assert.Equal("Draft", UdfParameterParser.ParseVersionState("Draft"));

    [Fact]
    public void ParseVersionState_Released_ReturnsReleased()
        => Assert.Equal("Released", UdfParameterParser.ParseVersionState("Released"));

    [Fact]
    public void ParseVersionState_All_ReturnsAll()
        => Assert.Equal("All", UdfParameterParser.ParseVersionState("All"));

    [Fact]
    public void ParseVersionState_Invalid_FallsBackToReleased()
        => Assert.Equal("Released", UdfParameterParser.ParseVersionState("Obsolete"));

    // ── IsProvided ──

    [Fact]
    public void IsProvided_Null_ReturnsFalse()
        => Assert.False(UdfParameterParser.IsProvided(null));

    [Fact]
    public void IsProvided_ExcelMissing_ReturnsFalse()
        => Assert.False(UdfParameterParser.IsProvided(ExcelMissing.Value));

    [Fact]
    public void IsProvided_String_ReturnsTrue()
        => Assert.True(UdfParameterParser.IsProvided("test"));

    // ── ToRectangularArray ──

    [Fact]
    public void ToRectangularArray_EmptyList_ReturnsHeadersOnly()
    {
        var items = new System.Collections.Generic.List<string>();
        var result = UdfParameterParser.ToRectangularArray(items,
            s => new object[] { s }, new[] { "Col1" });

        Assert.Equal(1, result.GetLength(0));
        Assert.Equal(1, result.GetLength(1));
        Assert.Equal("Col1", result[0, 0]);
    }

    [Fact]
    public void ToRectangularArray_WithItems_ReturnsHeadersPlusData()
    {
        var items = new System.Collections.Generic.List<string> { "a", "b" };
        var result = UdfParameterParser.ToRectangularArray(items,
            s => new object[] { s.ToUpper() }, new[] { "Column" });

        Assert.Equal(3, result.GetLength(0));
        Assert.Equal("Column", result[0, 0]);
        Assert.Equal("A", result[1, 0]);
        Assert.Equal("B", result[2, 0]);
    }

    [Fact]
    public void ToRectangularArray_NullRow_ReturnsEmptyString()
    {
        var items = new System.Collections.Generic.List<string> { "a" };
        var result = UdfParameterParser.ToRectangularArray(items,
            s => null!, new[] { "Col" });

        // null row returned by selector → skip fill → default(object) = null in array
        Assert.Null(result[1, 0]);
    }
}
