using System;
using BomAddIn.Core.Services;
using BomAddIn.UDF;
using BomAddIn.UDF.Functions;
using ExcelDna.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BomAddIn.UnitTests;

/// <summary>
/// Unit tests for SystemFunctions: SYNCSTATUS UDF.
/// </summary>
[Collection("UDF")]
public class SystemFunctionsTests : IDisposable
{
    private readonly Mock<ISyncService> _syncServiceMock = new();
    private readonly ServiceProvider _provider;

    public SystemFunctionsTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_syncServiceMock.Object);
        _provider = services.BuildServiceProvider();
        Container.Initialize(_provider);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public void SyncStatus_NeverSynced_ReturnsNeverMessage()
    {
        // Arrange
        _syncServiceMock
            .Setup(s => s.GetLastSyncTime())
            .Returns((DateTime?)null);

        // Act
        var result = SystemFunctions.SyncStatus();

        // Assert
        result.Should().Be("Never synced");
    }

    [Fact]
    public void SyncStatus_RecentSync_ReturnsMinutesAgo()
    {
        // Arrange — a sync that just happened (now)
        _syncServiceMock
            .Setup(s => s.GetLastSyncTime())
            .Returns(DateTime.UtcNow);

        // Act
        var result = SystemFunctions.SyncStatus();

        // Assert — age < 1 hour => "Synced {minutes}m ago", essentially 0 minutes
        var resultStr = result.Should().BeAssignableTo<string>().Which;
        resultStr.Should().StartWith("Synced ");
        resultStr.Should().EndWith("m ago");
    }

    [Fact]
    public void SyncStatus_HoursAgo_ReturnsHoursAgo()
    {
        // Arrange — a sync from 5 hours ago (1 <= hours < 24 => hours format)
        _syncServiceMock
            .Setup(s => s.GetLastSyncTime())
            .Returns(DateTime.UtcNow.AddHours(-5));

        // Act
        var result = SystemFunctions.SyncStatus();

        // Assert — "Synced {hours}h ago"
        var resultStr = result.Should().BeAssignableTo<string>().Which;
        resultStr.Should().StartWith("Synced ");
        resultStr.Should().EndWith("h ago");
    }

    [Fact]
    public void SyncStatus_OldSync_ReturnsDateFormatted()
    {
        // Arrange — a sync from 7 days ago (>= 24 hours => date format)
        var syncTime = new DateTime(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc);
        _syncServiceMock
            .Setup(s => s.GetLastSyncTime())
            .Returns(syncTime);

        // Act
        var result = SystemFunctions.SyncStatus();

        // Assert — "Synced yyyy-MM-dd HH:mm"
        result.Should().Be("Synced 2026-07-10 08:30");
    }

    [Fact]
    public void SyncStatus_Exception_ReturnsExcelErrorValue()
    {
        // Arrange
        _syncServiceMock
            .Setup(s => s.GetLastSyncTime())
            .Throws(new InvalidOperationException("Service unavailable"));

        // Act
        var result = SystemFunctions.SyncStatus();

        // Assert
        result.Should().Be(ExcelError.ExcelErrorValue);
    }
}
