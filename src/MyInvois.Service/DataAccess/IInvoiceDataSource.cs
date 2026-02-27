namespace MyInvois.Service.DataAccess;

// Uses skill: architecture/clean-architecture v1.2+

/// <summary>
/// Strategy interface for reading invoice data from MOVEX database (DB2 on AS/400).
/// Implementations: DirectQueryDataSource (SQL), StoredProcedureDataSource (stored procs).
/// Selected via configuration: MovexDbSettings.DataSourceStrategy
/// </summary>
public interface IInvoiceDataSource
{
    /// <summary>
    /// Get all pending invoices (AP + AR) from the specified accounting date onwards.
    /// Queries both fpledg (AP) and fsledg (AR) tables across active company schemas.
    /// </summary>
    Task<List<RawInvoiceRecord>> GetPendingInvoicesAsync(DateTime fromDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific invoice by number and type.
    /// </summary>
    Task<RawInvoiceRecord?> GetInvoiceByIdAsync(string invoiceNumber, string invoiceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get invoices within a date range (AP + AR) across active company schemas.
    /// </summary>
    Task<List<RawInvoiceRecord>> GetInvoicesByDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}
