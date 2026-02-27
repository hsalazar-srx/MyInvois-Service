using FluentAssertions;
using MyInvois.Service.Validators;

namespace MyInvois.Service.Tests.Validators;
/// <summary>
/// Unit tests for DateValidator
/// Uses skill: integration/myinvois-validator v1.0+
/// Tests date/time validation per MyInvois LHDNM spec
/// Requirements: ISO 8601 format, no placeholders, no future dates
/// </summary>
public class DateValidatorTests
{
    private readonly DateValidator _sut;

    public DateValidatorTests()
    {
        _sut = new DateValidator();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Succeeds()
    {
        // Arrange & Act
        var validator = new DateValidator();

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region ValidateInvoiceDate Tests - Valid Dates

    [Theory]
    [InlineData("2026-02-17")]
    [InlineData("2024-12-31")]
    [InlineData("2025-01-01")]
    public void ValidateInvoiceDate_WithValidISO8601Date_ReturnsTrue(string date)
    {
        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateInvoiceDate_WithTodayDate_ReturnsTrue()
    {
        // Arrange
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Act
        var isValid = _sut.ValidateInvoiceDate(today, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateInvoiceDate_WithLeapYearDate_ReturnsTrue()
    {
        // Arrange - Feb 29, 2024 (leap year)
        var date = "2024-02-29";

        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    #endregion

    #region ValidateInvoiceDate Tests - Null/Empty

    [Fact]
    public void ValidateInvoiceDate_WithNull_ReturnsFalse()
    {
        // Arrange
        string? date = null;

        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Contain("required");
    }

    [Fact]
    public void ValidateInvoiceDate_WithEmpty_ReturnsFalse()
    {
        // Arrange
        var date = string.Empty;

        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
    }

    #endregion

    #region ValidateInvoiceDate Tests - Placeholders

    [Theory]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("0000-00-00")]
    [InlineData("00000000")]
    public void ValidateInvoiceDate_WithPlaceholder_ReturnsFalse(string date)
    {
        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Match(m => m.Contains("placeholder") || m.Contains("not allowed"));
    }

    #endregion

    #region ValidateInvoiceDate Tests - Invalid Format

    [Theory]
    [InlineData("2026/02/17")]
    [InlineData("17-02-2026")]
    [InlineData("02-17-2026")]
    [InlineData("20260217")]
    [InlineData("2026-2-17")]
    public void ValidateInvoiceDate_WithInvalidFormat_ReturnsFalse(string date)
    {
        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Match(m => m.Contains("ISO 8601") || m.Contains("yyyy-MM-dd"));
    }

    #endregion

    #region ValidateInvoiceDate Tests - Invalid Dates

    [Theory]
    [InlineData("2023-02-29")] // Not a leap year
    [InlineData("2026-13-01")] // Invalid month
    [InlineData("2026-00-01")] // Invalid month
    [InlineData("2026-02-30")] // Invalid day for February
    [InlineData("2026-04-31")] // Invalid day for April
    public void ValidateInvoiceDate_WithInvalidDate_ReturnsFalse(string date)
    {
        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Match(m => m.Contains("valid date") || m.Contains("invalid") || m.Contains("ISO 8601"));
    }

    #endregion

    #region ValidateInvoiceDate Tests - Future Dates

    [Fact]
    public void ValidateInvoiceDate_WithFutureDate_ReturnsFalse()
    {
        // Arrange - tomorrow's date
        var futureDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");

        // Act
        var isValid = _sut.ValidateInvoiceDate(futureDate, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Contain("future");
    }

    [Fact]
    public void ValidateInvoiceDate_WithFarFutureDate_ReturnsFalse()
    {
        // Arrange
        var futureDate = "2030-01-01";

        // Act
        var isValid = _sut.ValidateInvoiceDate(futureDate, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().Contain("future");
    }

    #endregion

    #region ValidateInvoiceTime Tests - Valid Times

    [Theory]
    [InlineData("10:30:00")]
    [InlineData("00:00:00")]
    [InlineData("23:59:59")]
    [InlineData("12:00:00")]
    public void ValidateInvoiceTime_WithValidTime_ReturnsTrue(string time)
    {
        // Act
        var isValid = _sut.ValidateInvoiceTime(time, out var error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    #endregion

    #region ValidateInvoiceTime Tests - Null/Empty

    [Fact]
    public void ValidateInvoiceTime_WithNull_ReturnsFalse()
    {
        // Arrange
        string? time = null;

        // Act
        var isValid = _sut.ValidateInvoiceTime(time, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueTime");
        error.Message.Should().Contain("required");
    }

    [Fact]
    public void ValidateInvoiceTime_WithEmpty_ReturnsFalse()
    {
        // Arrange
        var time = string.Empty;

        // Act
        var isValid = _sut.ValidateInvoiceTime(time, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueTime");
    }

    #endregion

    #region ValidateInvoiceTime Tests - Invalid Format

    [Theory]
    [InlineData("10:30")]
    [InlineData("10-30-00")]
    [InlineData("103000")]
    [InlineData("10:30:00.123")]
    [InlineData("25:00:00")]
    [InlineData("10:60:00")]
    [InlineData("10:30:60")]
    public void ValidateInvoiceTime_WithInvalidTime_ReturnsFalse(string time)
    {
        // Act
        var isValid = _sut.ValidateInvoiceTime(time, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueTime");
        error.Message.Should().Match(m => m.Contains("HH:mm:ss") || m.Contains("invalid"));
    }

    #endregion

    #region Error Properties Tests

    [Fact]
    public void ValidateInvoiceDate_InvalidDate_ErrorHasCorrectProperties()
    {
        // Arrange
        var date = "N/A";

        // Act
        var isValid = _sut.ValidateInvoiceDate(date, out var error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.FieldName.Should().Be("IssueDate");
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Severity.Should().Be("Error");
        error.ViolatedRule.Should().NotBeNullOrWhiteSpace();
    }

    #endregion
}
