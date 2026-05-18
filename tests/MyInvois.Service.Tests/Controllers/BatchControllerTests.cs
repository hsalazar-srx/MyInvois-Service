using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Api.Controllers;
using MyInvois.Api.Models;
using MyInvois.Service.Models;
using MyInvois.Service.Services;

namespace MyInvois.Service.Tests.Controllers;

[Trait("Category", "Unit")]
public class BatchControllerTests
{
    private readonly Mock<IInvoiceProcessor> _processorMock;
    private readonly BatchController _controller;

    public BatchControllerTests()
    {
        _processorMock = new Mock<IInvoiceProcessor>();
        _controller = new BatchController(
            _processorMock.Object,
            new Mock<ILogger<BatchController>>().Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task ProcessRange_ValidRequest_Returns200WithBatchSummary()
    {
        // Arrange
        var batchResult = new BatchResult
        {
            BatchId = Guid.NewGuid().ToString(),
            BatchType = "DateRange:2026-05-01:2026-05-18",
            TotalInvoices = 5,
            SuccessCount = 4,
            FailedCount = 1,
            SkippedCount = 0,
            StartedAt = DateTime.UtcNow.AddSeconds(-30),
            CompletedAt = DateTime.UtcNow,
        };

        _processorMock
            .Setup(p => p.ProcessDateRangeBatch(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(batchResult);

        var request = new ProcessRangeRequest { FromDate = "2026-05-01", ToDate = "2026-05-18" };

        // Act
        var result = await _controller.ProcessRange(request);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<BatchProcessResponse>().Subject;
        response.TotalInvoices.Should().Be(5);
        response.SuccessCount.Should().Be(4);
        response.FailedCount.Should().Be(1);
        response.FromDate.Should().Be("2026-05-01");
        response.ToDate.Should().Be("2026-05-18");
    }

    [Theory]
    [InlineData("not-a-date", "2026-05-18", "fromDate")]
    [InlineData("2026-05-01", "not-a-date", "toDate")]
    public async Task ProcessRange_InvalidDateFormat_Returns400(string from, string to, string field)
    {
        var result = await _controller.ProcessRange(
            new ProcessRangeRequest { FromDate = from, ToDate = to });

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value!.ToString().Should().Contain(field);
    }

    [Fact]
    public async Task ProcessRange_FromAfterTo_Returns400()
    {
        var result = await _controller.ProcessRange(
            new ProcessRangeRequest { FromDate = "2026-05-18", ToDate = "2026-05-01" });

        result.Should().BeOfType<BadRequestObjectResult>();
        _processorMock.Verify(p => p.ProcessDateRangeBatch(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRange_RangeExceeds93Days_Returns400()
    {
        var result = await _controller.ProcessRange(
            new ProcessRangeRequest { FromDate = "2026-01-01", ToDate = "2026-12-31" });

        result.Should().BeOfType<BadRequestObjectResult>();
        _processorMock.Verify(p => p.ProcessDateRangeBatch(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessRange_DelegatesToProcessorWithCorrectDateRange()
    {
        _processorMock
            .Setup(p => p.ProcessDateRangeBatch(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchResult { CompletedAt = DateTime.UtcNow });

        await _controller.ProcessRange(
            new ProcessRangeRequest { FromDate = "2026-05-01", ToDate = "2026-05-18" });

        _processorMock.Verify(p => p.ProcessDateRangeBatch(
            new DateTime(2026, 5, 1, 0, 0, 0),
            new DateTime(2026, 5, 18, 23, 59, 59, 999).AddTicks(9999),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
