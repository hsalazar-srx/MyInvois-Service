using FluentAssertions;
using MyInvois.Service.Models;
using MyInvois.Service.Validators;

namespace MyInvois.Service.Tests.Validators;
/// <summary>
/// Unit tests for TotalsValidator
/// Uses skill: integration/myinvois-validator v1.0+
/// Tests invoice totals mathematical consistency with ±1 cent rounding tolerance
/// </summary>
public class TotalsValidatorTests
{
    private readonly TotalsValidator _sut;

    public TotalsValidatorTests()
    {
        _sut = new TotalsValidator();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Succeeds()
    {
        // Arrange & Act
        var validator = new TotalsValidator();

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region ValidateTotals Tests - Correct Totals

    [Fact]
    public void ValidateTotals_WithCorrectTotals_ReturnsTrue()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine
                {
                    LineTotalExclTax = 500.00m,
                    TaxAmount = 30.00m
                },
                new MyInvoiceLine
                {
                    LineTotalExclTax = 500.00m,
                    TaxAmount = 30.00m
                }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTotals_WithSingleLine_ReturnsTrue()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 100.00m,
            TotalTax = 6.00m,
            TotalInclTax = 106.00m,
            PayableAmount = 106.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine
                {
                    LineTotalExclTax = 100.00m,
                    TaxAmount = 6.00m
                }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    #endregion

    #region ValidateTotals Tests - Incorrect TotalExclTax

    [Fact]
    public void ValidateTotals_TotalExclTaxMismatch_ReturnsFalse()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 999.00m, // Should be 1000.00
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "TotalExclTax");
        errors[0].Message.Should().Contain("1000.00").And.Contain("999.00");
    }

    #endregion

    #region ValidateTotals Tests - Incorrect TotalTax

    [Fact]
    public void ValidateTotals_TotalTaxMismatch_ReturnsFalse()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.00m,
            TotalTax = 50.00m, // Should be 60.00
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "TotalTax");
        errors[0].Message.Should().Contain("60.00").And.Contain("50.00");
    }

    #endregion

    #region ValidateTotals Tests - Incorrect TotalInclTax

    [Fact]
    public void ValidateTotals_TotalInclTaxMismatch_ReturnsFalse()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1050.00m, // Should be 1060.00
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "TotalInclTax");
        errors[0].Message.Should().Contain("1060.00").And.Contain("1050.00");
    }

    #endregion

    #region ValidateTotals Tests - Incorrect PayableAmount

    [Fact]
    public void ValidateTotals_PayableAmountMismatch_ReturnsFalse()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1050.00m, // Should be 1060.00
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.FieldName == "PayableAmount");
        errors[0].Message.Should().Contain("1060.00").And.Contain("1050.00");
    }

    #endregion

    #region ValidateTotals Tests - Rounding Tolerance

    [Fact]
    public void ValidateTotals_RoundingTolerance_Within1Cent_ReturnsTrue()
    {
        // Arrange - 0.01 cent difference (within tolerance)
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.01m, // 1 cent more than sum
            TotalTax = 60.00m,
            TotalInclTax = 1060.01m,
            PayableAmount = 1060.01m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeTrue("because difference is within ±1 cent tolerance");
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTotals_RoundingTolerance_Exactly1Cent_ReturnsTrue()
    {
        // Arrange - exactly 1 cent difference (at tolerance boundary)
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.00m,
            TotalTax = 60.01m, // Exactly 1 cent more
            TotalInclTax = 1060.01m,
            PayableAmount = 1060.01m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeTrue("because difference is exactly at ±1 cent tolerance");
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTotals_RoundingTolerance_Over1Cent_ReturnsFalse()
    {
        // Arrange - 2 cents difference (exceeds tolerance)
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 1000.02m, // 2 cents more (exceeds tolerance)
            TotalTax = 60.00m,
            TotalInclTax = 1060.02m,
            PayableAmount = 1060.02m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse("because difference exceeds ±1 cent tolerance");
        errors.Should().ContainSingle(e => e.FieldName == "TotalExclTax");
    }

    #endregion

    #region ValidateTotals Tests - Multiple Errors

    [Fact]
    public void ValidateTotals_MultipleErrors_CollectsAll()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 999.00m, // Wrong
            TotalTax = 50.00m, // Wrong
            TotalInclTax = 1050.00m, // Wrong
            PayableAmount = 1050.00m, // Wrong
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().HaveCount(e => e >= 3, "validator should collect multiple errors");
        //errors.Count.Should().BeGreaterOrEqualTo(3, "validator should collect multiple errors");
        errors.Should().Contain(e => e.FieldName == "TotalExclTax");
        errors.Should().Contain(e => e.FieldName == "TotalTax");
        errors.Should().Contain(e => e.FieldName == "TotalInclTax");
    }

    #endregion

    #region ValidateTotals Tests - Edge Cases

    [Fact]
    public void ValidateTotals_WithNoLines_ReturnsFalse()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 100.00m,
            TotalTax = 6.00m,
            TotalInclTax = 106.00m,
            PayableAmount = 106.00m,
            Lines = new List<MyInvoiceLine>()
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().ContainSingle(e => e.Message.Contains("line item"));
    }

    #endregion

    #region Error Properties Tests

    [Fact]
    public void ValidateTotals_InvalidTotal_ErrorHasCorrectProperties()
    {
        // Arrange
        var document = new MyInvoiceDocument
        {
            TotalExclTax = 999.00m,
            TotalTax = 60.00m,
            TotalInclTax = 1060.00m,
            PayableAmount = 1060.00m,
            Lines = new List<MyInvoiceLine>
            {
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m },
                new MyInvoiceLine { LineTotalExclTax = 500.00m, TaxAmount = 30.00m }
            }
        };

        // Act
        var isValid = _sut.ValidateTotals(document, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().NotBeEmpty();
        var error = errors[0];
        error.FieldName.Should().NotBeNullOrWhiteSpace();
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Severity.Should().Be("Error");
        error.ViolatedRule.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
