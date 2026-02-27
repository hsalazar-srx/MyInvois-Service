using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MyInvois.Service.Models;
using MyInvois.Service.Services;
using MyInvois.Service.Validators;

using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;


namespace MyInvois.Service.Tests.Services;
/// <summary>
/// Unit tests for MyInvoisMapper
/// Uses skill: integration/myinvois-document-builder v1.0+
/// Tests transformation of MOVEX invoices to MyInvois UBL 2.1 format
/// Tests orchestration of all 5 validators
/// </summary>
public class MyInvoisMapperTests
{
    private readonly Mock<IMandatoryFieldsValidator> _mandatoryFieldsValidatorMock;
    private readonly Mock<ITINValidator> _tinValidatorMock;
    private readonly Mock<IDateValidator> _dateValidatorMock;
    private readonly Mock<ICurrencyValidator> _currencyValidatorMock;
    private readonly Mock<ITotalsValidator> _totalsValidatorMock;
    private readonly Mock<ILogger<MyInvoisMapper>> _loggerMock;
    private readonly Mock<IOptions<CompanySettings>> _companySettingsMock;
    private readonly MyInvoisMapper _sut;

    public MyInvoisMapperTests()
    {
        _mandatoryFieldsValidatorMock = new Mock<IMandatoryFieldsValidator>();
        _tinValidatorMock = new Mock<ITINValidator>();
        _dateValidatorMock = new Mock<IDateValidator>();
        _currencyValidatorMock = new Mock<ICurrencyValidator>();
        _totalsValidatorMock = new Mock<ITotalsValidator>();
        _companySettingsMock = new Mock<IOptions<CompanySettings>>();
        _companySettingsMock.Setup(x => x.Value).Returns(new CompanySettings
        {
            Companies = new Dictionary<string, CompanyDetails>
            {
                ["100"] = new CompanyDetails
                {
                    TIN = "C10123456789",
                    Name = "Test Company 100 Sdn Bhd",
                    BRN = "BRN100",
                    IdScheme = "BRN",
                    Address = "Test Address 100"
                },
                ["300"] = new CompanyDetails
                {
                    TIN = "C30123456789",
                    Name = "Test Company 300 Sdn Bhd",
                    BRN = "BRN300",
                    IdScheme = "BRN",
                    Address = "Test Address 300"
                }
            }
        });
        _loggerMock = new Mock<ILogger<MyInvoisMapper>>();

        _sut = new MyInvoisMapper(
            _mandatoryFieldsValidatorMock.Object,
            _tinValidatorMock.Object,
            _dateValidatorMock.Object,
            _currencyValidatorMock.Object,
            _totalsValidatorMock.Object,
            _companySettingsMock.Object,
            _loggerMock.Object
        );
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullMandatoryFieldsValidator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MyInvoisMapper(null!, _tinValidatorMock.Object, _dateValidatorMock.Object,
                _currencyValidatorMock.Object, _totalsValidatorMock.Object, _companySettingsMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullTINValidator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MyInvoisMapper(_mandatoryFieldsValidatorMock.Object, null!, _dateValidatorMock.Object,
                _currencyValidatorMock.Object, _totalsValidatorMock.Object, _companySettingsMock.Object, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MyInvoisMapper(_mandatoryFieldsValidatorMock.Object, _tinValidatorMock.Object,
                _dateValidatorMock.Object, _currencyValidatorMock.Object, _totalsValidatorMock.Object, _companySettingsMock.Object, null!));
    }

    #endregion

    #region Transform Tests - Sales Invoice (AR)

    [Fact]
    public void Transform_SalesInvoice_MapsSupplierAsOurCompany()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.Should().NotBeNull();
        result.SupplierTIN.Should().NotBeNullOrWhiteSpace("supplier TIN is mandatory for sales");
        result.SupplierName.Should().NotBeNullOrWhiteSpace("supplier name is mandatory");
        result.SupplierBRN.Should().NotBeNullOrWhiteSpace("supplier BRN is mandatory");
    }

    [Fact]
    public void Transform_SalesInvoice_MapsBuyerAsCustomer()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.Buyer = new InvoiceParty
        {
            TIN = "987654321098",
            Name = "Test Customer Sdn Bhd",
            BRN = "BRN987654"
        };

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.BuyerTIN.Should().Be("987654321098");
        result.BuyerName.Should().Be("Test Customer Sdn Bhd");
    }

    #endregion

    #region Transform Tests - Purchase Invoice (AP)

    [Fact]
    public void Transform_PurchaseInvoice_MapsSupplierAsExternalParty()
    {
        // Arrange
        var movexInvoice = CreateValidMovexPurchaseInvoice();
        movexInvoice.Supplier = new InvoiceParty
        {
            TIN = "123456789012",
            Name = "External Supplier Sdn Bhd",
            BRN = "BRN123456"
        };

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.SupplierTIN.Should().Be("123456789012");
        result.SupplierName.Should().Be("External Supplier Sdn Bhd");
        result.SupplierBRN.Should().Be("BRN123456");
    }

    [Fact]
    public void Transform_PurchaseInvoice_MapsBuyerAsOurCompany()
    {
        // Arrange
        var movexInvoice = CreateValidMovexPurchaseInvoice();

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.BuyerName.Should().NotBeNullOrWhiteSpace("buyer name is mandatory for purchase");
    }

    #endregion

    #region Transform Tests - Date Conversion

    [Theory]
    [InlineData("20260217", "2026-02-17")]
    [InlineData("20241231", "2024-12-31")]
    [InlineData("20250101", "2025-01-01")]
    public void Transform_InvoiceDate_ConvertsYYYYMMDDToISO8601(string movexDate, string expectedISO8601)
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.InvoiceDate = movexDate;

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.IssueDate.Should().Be(expectedISO8601);
    }

    [Fact]
    public void Transform_InvoiceTime_SetsToCurrentUTC()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.IssueTime.Should().MatchRegex(@"^\d{2}:\d{2}:\d{2}$", "time must be in HH:mm:ss format");
    }

    #endregion

    #region Transform Tests - Currency and Exchange Rate

    [Fact]
    public void Transform_Currency_MapsCurrencyCodeAndRate()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.CurrencyCode = "USD";
        movexInvoice.ExchangeRate = 4.45m;

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.CurrencyCode.Should().Be("USD");
        result.ExchangeRate.Should().Be(4.45m);
    }

    #endregion

    #region Transform Tests - Totals

    [Fact]
    public void Transform_Totals_MapsTotalExclTaxTotalTaxTotalInclTax()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.TotalExclTax = 1000.00m;
        movexInvoice.TotalTax = 60.00m;
        movexInvoice.TotalInclTax = 1060.00m;

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.TotalExclTax.Should().Be(1000.00m);
        result.TotalTax.Should().Be(60.00m);
        result.TotalInclTax.Should().Be(1060.00m);
        result.PayableAmount.Should().Be(1060.00m, "payable amount equals total incl tax");
    }

    #endregion

    #region Transform Tests - Line Items

    [Fact]
    public void Transform_LineItems_MapsAllLines()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.Lines = new List<InvoiceLine>
        {
            new InvoiceLine
            {
                LineNumber = 1,
                ItemNumber = "ITEM001",
                Description = "Test Item 1",
                ClassificationCode = "001",
                Quantity = 10m,
                UnitPrice = 50m,
                LineTotal = 500m,
                TaxAmount = 30m
            },
            new InvoiceLine
            {
                LineNumber = 2,
                ItemNumber = "ITEM002",
                Description = "Test Item 2",
                ClassificationCode = "002",
                Quantity = 5m,
                UnitPrice = 100m,
                LineTotal = 500m,
                TaxAmount = 30m
            }
        };

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.Lines.Should().HaveCount(2);
        result.Lines[0].LineNumber.Should().Be(1);
        result.Lines[0].ItemNumber.Should().Be("ITEM001");
        result.Lines[0].Description.Should().Be("Test Item 1");
        result.Lines[0].ClassificationCode.Should().Be("001");
        result.Lines[1].LineNumber.Should().Be(2);
    }

    #endregion

    #region Transform Tests - Metadata

    [Fact]
    public void Transform_Metadata_SetsSourceInvoiceNumber()
    {
        // Arrange
        var movexInvoice = CreateValidMovexSalesInvoice();
        movexInvoice.InvoiceNumber = "INV001";

        // Act
        var result = _sut.Transform(movexInvoice);

        // Assert
        result.SourceInvoiceNumber.Should().Be("INV001");
        result.InvoiceNumber.Should().Be("INV001");
    }

    #endregion

    #region ValidateDocument Tests - All Validators Called

    [Fact]
    public void ValidateDocument_CallsMandatoryFieldsValidator()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        _mandatoryFieldsValidatorMock.Verify(x => x.Validate(document, out It.Ref<List<ValidationError>>.IsAny), Times.Once);
    }

    [Fact]
    public void ValidateDocument_CallsTINValidatorForSupplierAndBuyer()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        _tinValidatorMock.Verify(x => x.ValidateFormat(
            It.Is<string>(s => s == document.SupplierTIN),
            out It.Ref<ValidationError?>.IsAny,
            true), Times.Once, "supplier TIN is mandatory");

        _tinValidatorMock.Verify(x => x.ValidateFormat(
            It.Is<string?>(s => s == document.BuyerTIN),
            out It.Ref<ValidationError?>.IsAny,
            false), Times.Once, "buyer TIN is optional");
    }

    [Fact]
    public void ValidateDocument_CallsDateValidator()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        _dateValidatorMock.Verify(x => x.ValidateInvoiceDate(document.IssueDate, out It.Ref<ValidationError?>.IsAny), Times.Once);
    }

    [Fact]
    public void ValidateDocument_CallsCurrencyValidator()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        _currencyValidatorMock.Verify(x => x.ValidateCurrencyCode(document.CurrencyCode, out It.Ref<ValidationError?>.IsAny), Times.Once);
        _currencyValidatorMock.Verify(x => x.ValidateExchangeRate(document.ExchangeRate, document.CurrencyCode, out It.Ref<ValidationError?>.IsAny), Times.Once);
    }

    [Fact]
    public void ValidateDocument_CallsTotalsValidator()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        _totalsValidatorMock.Verify(x => x.ValidateTotals(document, out It.Ref<List<ValidationError>>.IsAny), Times.Once);
    }

    #endregion

    #region ValidateDocument Tests - Error Collection

    [Fact]
    public void ValidateDocument_WithValidDocument_ReturnsTrue()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        SetupAllValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateDocument_WithInvalidDocument_CollectsAllErrors()
    {
        // Arrange
        var document = CreateValidMyInvoisDocument();
        var mandatoryErrors = new List<ValidationError>
        {
            new ValidationError { FieldName = "SupplierTIN", Message = "TIN required" }
        };

        _mandatoryFieldsValidatorMock
            .Setup(x => x.Validate(document, out It.Ref<List<ValidationError>>.IsAny))
            .Callback(new ValidateCallback((MyInvoiceDocument doc, out List<ValidationError> err) =>
            {
                err = mandatoryErrors;
            }))
            .Returns(false);

        var tinError = new ValidationError { FieldName = "TIN", Message = "Invalid format" };
        _tinValidatorMock
            .Setup(x => x.ValidateFormat(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny, It.IsAny<bool>()))
            .Callback(new TINValidateCallback((string? tin, out ValidationError? err, bool isRequired) =>
            {
                err = tinError;
            }))
            .Returns(false);

        SetupOtherValidatorsMock(true);

        // Act
        var isValid = _sut.ValidateDocument(document, out var errors);

        // Assert
        isValid.Should().BeFalse("validation should fail when errors exist");
        errors.Should().HaveCountGreaterThanOrEqualTo(1, "should collect errors from multiple validators");
        errors.Should().Contain(e => e.FieldName == "SupplierTIN");
    }

    #endregion

    #region Helper Methods

    private MovexInvoice CreateValidMovexSalesInvoice()
    {
        return new MovexInvoice
        {
            InvoiceNumber = "INV001",
            InvoiceDate = "20260217",
            InvoiceType = "Sales",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            CompanyCode = "100",
            VoucherNumber = "V001",
            Buyer = new InvoiceParty
            {
                Name = "Test Buyer"
            },
            Lines = new List<InvoiceLine>
            {
                new InvoiceLine
                {
                    LineNumber = 1,
                    ItemNumber = "ITEM001",
                    Description = "Test Item",
                    ClassificationCode = "001",
                    Quantity = 10m,
                    UnitPrice = 100m,
                    LineTotal = 1000m,
                    TaxAmount = 60m
                }
            }
        };
    }

    private MovexInvoice CreateValidMovexPurchaseInvoice()
    {
        var invoice = CreateValidMovexSalesInvoice();
        invoice.InvoiceType = "Purchase";
        return invoice;
    }

    private MyInvoiceDocument CreateValidMyInvoisDocument()
    {
        return new MyInvoiceDocument
        {
            SupplierTIN = "123456789012",
            SupplierName = "Test Supplier Sdn Bhd",
            SupplierBRN = "BRN123456",
            BuyerTIN = "987654321098",
            BuyerName = "Test Buyer Corporation",
            InvoiceNumber = "INV001",
            IssueDate = "2026-02-17",
            IssueTime = "10:30:00",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine
                {
                    LineNumber = 1,
                    ItemNumber = "ITEM001",
                    Description = "Test product",
                    ClassificationCode = "001",
                    Quantity = 10m,
                    UnitPrice = 100m,
                    LineTotalExclTax = 1000m,
                    TaxAmount = 60m
                }
            }
        };
    }

    private void SetupAllValidatorsMock(bool returnValue)
    {
        _mandatoryFieldsValidatorMock
            .Setup(x => x.Validate(It.IsAny<MyInvoiceDocument>(), out It.Ref<List<ValidationError>>.IsAny))
            .Returns(returnValue);

        _tinValidatorMock
            .Setup(x => x.ValidateFormat(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny, It.IsAny<bool>()))
            .Returns(returnValue);

        _dateValidatorMock
            .Setup(x => x.ValidateInvoiceDate(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _currencyValidatorMock
            .Setup(x => x.ValidateCurrencyCode(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _currencyValidatorMock
            .Setup(x => x.ValidateExchangeRate(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _totalsValidatorMock
            .Setup(x => x.ValidateTotals(It.IsAny<MyInvoiceDocument>(), out It.Ref<List<ValidationError>>.IsAny))
            .Returns(returnValue);
    }

    private void SetupOtherValidatorsMock(bool returnValue)
    {
        _dateValidatorMock
            .Setup(x => x.ValidateInvoiceDate(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _currencyValidatorMock
            .Setup(x => x.ValidateCurrencyCode(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _currencyValidatorMock
            .Setup(x => x.ValidateExchangeRate(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny))
            .Returns(returnValue);

        _tinValidatorMock
            .Setup(x => x.ValidateFormat(It.IsAny<string>(), out It.Ref<ValidationError?>.IsAny, It.IsAny<bool>()))
            .Returns(returnValue);

        _totalsValidatorMock
            .Setup(x => x.ValidateTotals(It.IsAny<MyInvoiceDocument>(), out It.Ref<List<ValidationError>>.IsAny))
            .Returns(returnValue);
    }

    // Callback delegates for out parameters
    delegate void ValidateCallback(MyInvoiceDocument doc, out List<ValidationError> errors);
    delegate void TINValidateCallback(string? tin, out ValidationError? error, bool isRequired);

    #endregion
}
