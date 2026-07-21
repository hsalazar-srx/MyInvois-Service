using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Api.Controllers;
using MyInvois.Api.Models;
using MyInvois.Service.DataAccess;

namespace MyInvois.Service.Tests.Controllers;

[Trait("Category", "Unit")]
public class InvoicesControllerTests
{
    private readonly Mock<IInvoiceDataSource> _dataSourceMock;
    private readonly InvoicesController _controller;

    public InvoicesControllerTests()
    {
        _dataSourceMock = new Mock<IInvoiceDataSource>();
        _controller = new InvoicesController(
            _dataSourceMock.Object,
            new Mock<ILogger<InvoicesController>>().Object);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInvoices_InvalidFromDate_Returns400()
    {
        var result = await _controller.GetInvoices("not-a-date", "2026-01-31");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInvoices_FromDateAfterToDate_Returns400()
    {
        var result = await _controller.GetInvoices("2026-02-01", "2026-01-01");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInvoices_InvalidType_Returns400()
    {
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>());

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31", "INVALID");

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── MapToDto amount negation ─────────────────────────────────────────────

    [Fact]
    public async Task GetInvoices_ArInvoice_AmountsRemainPositive()
    {
        // AR regular invoice (ESTRCD=10): ESCUAM is positive, sign multiplier = +1, amounts unchanged.
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "CUS001", InvoiceNo = "SI-001",
                    AccountingDate = 20260115, InvoiceDate = 20260115,
                    Currency = "MYR", InvoiceAmount = 1060m, GstAmount = 60m,
                    InvoiceType = "AR", CompanyCode = "100", TransCode = "10"
                }
            });

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InvoiceListResponse>().Subject;
        var dto = response.Items[0];

        dto.TotalInclTax.Should().Be(1060m);
        dto.TaxAmount.Should().Be(60m);
        dto.AmountExclTax.Should().Be(1000m);
    }

    [Fact]
    public async Task GetInvoices_ArCreditNote_AmountsNegated()
    {
        // AR credit note (ESTRCD=20): ESCUAM is positive in FSLEDG but must arrive at the portal
        // as negative so invoiceBadge() detects totalInclTax < 0 and renders the "AR-CN" badge.
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "CUS001", InvoiceNo = "CN-001",
                    AccountingDate = 20260115, InvoiceDate = 20260115,
                    Currency = "MYR", InvoiceAmount = 530m, GstAmount = 30m,
                    InvoiceType = "AR", CompanyCode = "100", TransCode = "20"
                }
            });

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InvoiceListResponse>().Subject;
        var dto = response.Items[0];

        dto.TotalInclTax.Should().Be(-530m, "AR-CN: positive ESCUAM negated so portal badge works");
        dto.TaxAmount.Should().Be(-30m);
        dto.AmountExclTax.Should().Be(-500m);
    }

    [Fact]
    public async Task GetInvoices_ApInvoice_AmountsRemainPositive()
    {
        // AP regular invoice: epcuam*-1 in DB query → arrives positive; no further sign change.
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "SUP001", InvoiceNo = "AP-001",
                    AccountingDate = 20260115,
                    Currency = "USD", InvoiceAmount = 1000m, GstAmount = 0m,
                    InvoiceType = "AP", CompanyCode = "100", TransCode = null
                }
            });

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InvoiceListResponse>().Subject;
        var dto = response.Items[0];

        dto.TotalInclTax.Should().Be(1000m);
        dto.TaxAmount.Should().Be(0m);
        dto.AmountExclTax.Should().Be(1000m);
    }

    [Fact]
    public async Task GetInvoices_ApCreditNote_NegativeAmountPreserved()
    {
        // AP credit note: epcuam*-1 in DB query → arrives negative; no further change at controller.
        // The portal's invoiceBadge() already sees totalInclTax < 0 and renders "AP-CN".
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new()
                {
                    PartyId = "SUP001", InvoiceNo = "AP-CN-001",
                    AccountingDate = 20260115,
                    Currency = "USD", InvoiceAmount = -200m, GstAmount = 0m,
                    InvoiceType = "AP", CompanyCode = "100", TransCode = null
                }
            });

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InvoiceListResponse>().Subject;
        var dto = response.Items[0];

        dto.TotalInclTax.Should().Be(-200m, "AP-CN: negative already from DB query, no controller change");
        dto.AmountExclTax.Should().Be(-200m);
    }

    // ── Type filter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInvoices_FilterByAr_ReturnsOnlyArRecords()
    {
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RawInvoiceRecord>
            {
                new() { PartyId = "CUS001", InvoiceNo = "SI-001", AccountingDate = 20260115,
                        Currency = "MYR", InvoiceAmount = 100m, GstAmount = 0m,
                        InvoiceType = "AR", CompanyCode = "100" },
                new() { PartyId = "SUP001", InvoiceNo = "AP-001", AccountingDate = 20260115,
                        Currency = "MYR", InvoiceAmount = 200m, GstAmount = 0m,
                        InvoiceType = "AP", CompanyCode = "100" }
            });

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31", "AR");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<InvoiceListResponse>().Subject;
        response.Items.Should().HaveCount(1);
        response.Items[0].Type.Should().Be("AR");
    }

    [Fact]
    public async Task GetInvoices_Db2Throws_Returns502()
    {
        _dataSourceMock
            .Setup(x => x.GetInvoicesByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB2 connection failed"));

        var result = await _controller.GetInvoices("2026-01-01", "2026-01-31");

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(502);
    }
}
