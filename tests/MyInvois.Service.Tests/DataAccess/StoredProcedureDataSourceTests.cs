namespace MyInvois.Service.Tests.DataAccess;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;

/// <summary>
/// Unit tests for StoredProcedureDataSource.
/// Tests config validation and method contracts.
/// </summary>
public class StoredProcedureDataSourceTests
{
    private readonly Mock<ILogger<StoredProcedureDataSource>> _loggerMock;

    public StoredProcedureDataSourceTests()
    {
        _loggerMock = new Mock<ILogger<StoredProcedureDataSource>>();
    }

    [Fact]
    public async Task GetPendingInvoicesAsync_WithMissingApProc_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new MovexDbSettings
        {
            DataSourceStrategy = "StoredProcedure",
            ApInvoiceStoredProc = "",   // missing
            ArInvoiceStoredProc = "GET_AR_INVOICES"
        };
        var sut = new StoredProcedureDataSource(Options.Create(settings), _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetPendingInvoicesAsync(DateTime.Today));
    }

    [Fact]
    public async Task GetPendingInvoicesAsync_WithMissingArProc_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new MovexDbSettings
        {
            DataSourceStrategy = "StoredProcedure",
            ApInvoiceStoredProc = "GET_AP_INVOICES",
            ArInvoiceStoredProc = ""   // missing
        };
        var sut = new StoredProcedureDataSource(Options.Create(settings), _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.GetPendingInvoicesAsync(DateTime.Today));
    }

    [Fact]
    public async Task GetPendingInvoicesAsync_WithValidConfig_ThrowsNotImplementedException()
    {
        // Arrange — stored procs not yet available
        var settings = new MovexDbSettings
        {
            DataSourceStrategy = "StoredProcedure",
            ApInvoiceStoredProc = "GET_AP_INVOICES",
            ArInvoiceStoredProc = "GET_AR_INVOICES"
        };
        var sut = new StoredProcedureDataSource(Options.Create(settings), _loggerMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(() =>
            sut.GetPendingInvoicesAsync(DateTime.Today));
    }
}
