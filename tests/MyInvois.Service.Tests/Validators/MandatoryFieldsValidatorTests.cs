using FluentAssertions;
using MyInvois.Service.Models;
using MyInvois.Service.Validators;

namespace MyInvois.Service.Tests.Validators;
/// <summary>
/// Unit tests for MandatoryFieldsValidator
/// Uses skill: integration/myinvois-validator v1.0+
/// Tests all 20+ mandatory field validations per MyInvois LHDNM spec
/// </summary>
public class MandatoryFieldsValidatorTests
{
    private readonly MandatoryFieldsValidator _sut;

    public MandatoryFieldsValidatorTests()
    {
        _sut = new MandatoryFieldsValidator();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Succeeds()
    {
        // Arrange & Act
        var validator = new MandatoryFieldsValidator();

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region Supplier Validation Tests

    [Fact]
    public void Validate_SupplierTINMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.SupplierTIN = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "SupplierTIN" && e.Message.Contains("required"));
    }

    [Fact]
    public void Validate_SupplierNameMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.SupplierName = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "SupplierName" && e.Message.Contains("required"));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(500)]
    public void Validate_SupplierNameTooLong_ReturnsError(int length)
    {
        // Arrange
        var document = CreateValidDocument();
        document.SupplierName = new string('A', length);

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "SupplierName" && e.Message.Contains("300"));
    }

    [Fact]
    public void Validate_SupplierBRNMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.SupplierBRN = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "SupplierBRN" && e.Message.Contains("required"));
    }

    #endregion

    #region Buyer Validation Tests

    [Fact]
    public void Validate_BuyerNameMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.BuyerName = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "BuyerName" && e.Message.Contains("required"));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(400)]
    public void Validate_BuyerNameTooLong_ReturnsError(int length)
    {
        // Arrange
        var document = CreateValidDocument();
        document.BuyerName = new string('B', length);

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "BuyerName" && e.Message.Contains("300"));
    }

    #endregion

    #region Invoice Header Validation Tests

    [Fact]
    public void Validate_InvoiceNumberMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.InvoiceNumber = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "InvoiceNumber" && e.Message.Contains("required"));
    }

    [Theory]
    [InlineData(51)]
    [InlineData(100)]
    public void Validate_InvoiceNumberTooLong_ReturnsError(int length)
    {
        // Arrange
        var document = CreateValidDocument();
        document.InvoiceNumber = new string('1', length);

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "InvoiceNumber" && e.Message.Contains("50"));
    }

    [Fact]
    public void Validate_IssueDateMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.IssueDate = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "IssueDate" && e.Message.Contains("required"));
    }

    [Fact]
    public void Validate_CurrencyCodeMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.CurrencyCode = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "CurrencyCode" && e.Message.Contains("required"));
    }

    #endregion

    #region Line Item Validation Tests

    [Fact]
    public void Validate_LineDescriptionMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.Lines[0].Description = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName.Contains("Description") && e.Message.Contains("required"));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(500)]
    public void Validate_LineDescriptionTooLong_ReturnsError(int length)
    {
        // Arrange
        var document = CreateValidDocument();
        document.Lines[0].Description = new string('D', length);

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName.Contains("Description") && e.Message.Contains("300"));
    }

    [Fact]
    public void Validate_LineClassificationCodeMissing_ReturnsError()
    {
        // Arrange
        var document = CreateValidDocument();
        document.Lines[0].ClassificationCode = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName.Contains("ClassificationCode") && e.Message.Contains("required"));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("ABCD")]
    public void Validate_LineClassificationCodeInvalidLength_ReturnsError(string code)
    {
        // Arrange
        var document = CreateValidDocument();
        document.Lines[0].ClassificationCode = code;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName.Contains("ClassificationCode") && e.Message.Contains("3 characters"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_LineQuantityInvalid_ReturnsError(decimal quantity)
    {
        // Arrange
        var document = CreateValidDocument();
        document.Lines[0].Quantity = quantity;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName.Contains("Quantity") && e.Message.Contains("greater than 0"));
    }

    #endregion

    #region Non-Fail-Fast Tests

    [Fact]
    public void Validate_MultipleErrors_CollectsAll()
    {
        // Arrange
        var document = CreateValidDocument();
        document.SupplierTIN = string.Empty;
        document.SupplierName = string.Empty;
        document.BuyerName = string.Empty;
        document.InvoiceNumber = string.Empty;
        document.Lines[0].Description = string.Empty;

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().HaveCount(e => e >= 5);
        errors.Should().Contain(e => e.FieldName == "SupplierTIN");
        errors.Should().Contain(e => e.FieldName == "SupplierName");
        errors.Should().Contain(e => e.FieldName == "BuyerName");
        errors.Should().Contain(e => e.FieldName == "InvoiceNumber");
        errors.Should().Contain(e => e.FieldName.Contains("Description"));
    }

    #endregion

    #region Valid Document Tests

    [Fact]
    public void Validate_ValidDocument_ReturnsTrue()
    {
        // Arrange
        var document = CreateValidDocument();

        // Act
        var isValid = _sut.Validate(document, out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private MyInvoiceDocument CreateValidDocument()
    {
        return new MyInvoiceDocument
        {
            // Supplier
            SupplierTIN = "123456789012",
            SupplierName = "Test Supplier Sdn Bhd",
            SupplierBRN = "BRN123456",

            // Buyer
            BuyerTIN = "987654321098",
            BuyerName = "Test Buyer Corporation",

            // Invoice header
            InvoiceNumber = "INV001",
            IssueDate = "2026-02-17",
            IssueTime = "10:30:00",
            CurrencyCode = "MYR",
            ExchangeRate = 1.0m,

            // Totals
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,

            // Line items
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
                    TaxAmount = 60m,
                    LineTotalInclTax = 1060m
                }
            }
        };
    }

    #endregion
}
