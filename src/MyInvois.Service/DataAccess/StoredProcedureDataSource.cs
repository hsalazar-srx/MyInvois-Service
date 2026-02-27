namespace MyInvois.Service.DataAccess;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;

// Uses skill: integration/movex-db2-data-source v1.0+

/// <summary>
/// Stored procedure implementation of IInvoiceDataSource.
/// Calls configured DB2 stored procedures on AS/400 for invoice data retrieval.
/// Stored procedure names are configured in MovexDbSettings.ApInvoiceStoredProc / ArInvoiceStoredProc.
///
/// This is a stub implementation — stored procedures are to be developed by the DBA team.
/// </summary>
public class StoredProcedureDataSource : IInvoiceDataSource
{
    private readonly MovexDbSettings _settings;
    private readonly ILogger<StoredProcedureDataSource> _logger;

    public StoredProcedureDataSource(
        IOptions<MovexDbSettings> settings,
        ILogger<StoredProcedureDataSource> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<RawInvoiceRecord>> GetPendingInvoicesAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        // TODO: Implement when stored procedures are available
        // 1. Open DB2 connection
        // 2. Call _settings.ApInvoiceStoredProc with @fromDate parameter
        // 3. Call _settings.ArInvoiceStoredProc with @fromDate parameter
        // 4. Map header result sets to RawInvoiceRecord
        // 5. Query line items for each header from OINVOL + MITMAS:
        //    DBA team: stored procs should return line items as a second result set,
        //    or a separate line item stored proc should be created.
        //    Line items map to RawInvoiceLineRecord and are assigned to RawInvoiceRecord.Lines

        ValidateStoredProcConfig();

        _logger.LogInformation("Fetching pending invoices via stored procedures. FromDate: {FromDate}, AP Proc: {ApProc}, AR Proc: {ArProc}",
            fromDate.ToString("yyyyMMdd"), _settings.ApInvoiceStoredProc, _settings.ArInvoiceStoredProc);

        throw new NotImplementedException("Stored procedure data source awaiting DBA team deliverable");
    }

    public async Task<RawInvoiceRecord?> GetInvoiceByIdAsync(string invoiceNumber, string invoiceType, CancellationToken cancellationToken = default)
    {
        ValidateStoredProcConfig();

        _logger.LogInformation("Fetching invoice by ID via stored procedure. InvoiceNo: {InvoiceNo}, Type: {InvoiceType}",
            invoiceNumber, invoiceType);

        throw new NotImplementedException("Stored procedure data source awaiting DBA team deliverable");
    }

    public async Task<List<RawInvoiceRecord>> GetInvoicesByDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        ValidateStoredProcConfig();

        _logger.LogInformation("Fetching invoices by date range via stored procedure. From: {FromDate}, To: {ToDate}",
            fromDate.ToString("yyyyMMdd"), toDate.ToString("yyyyMMdd"));

        throw new NotImplementedException("Stored procedure data source awaiting DBA team deliverable");
    }

    private void ValidateStoredProcConfig()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApInvoiceStoredProc))
            throw new InvalidOperationException("MovexDb:ApInvoiceStoredProc is not configured. StoredProcedure strategy requires stored procedure names.");

        if (string.IsNullOrWhiteSpace(_settings.ArInvoiceStoredProc))
            throw new InvalidOperationException("MovexDb:ArInvoiceStoredProc is not configured. StoredProcedure strategy requires stored procedure names.");
    }
}
