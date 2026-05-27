namespace MyInvois.Service.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Models;

// Uses skill: architecture/clean-architecture v1.2+
// Uses skill: integration/movex-db2-data-source v1.0+

/// <summary>
/// MovexInvoiceReader - Facade that combines IInvoiceDataSource + IPartyDataProvider
/// to produce enriched MovexInvoice DTOs.
///
/// Data flow:
///   MOVEX DB2/AS400 → IInvoiceDataSource → RawInvoiceRecord[]
///   RawInvoiceRecord + IPartyDataProvider → MovexInvoice DTOs
///
/// Replaces previous REST API design (ADR-013).
/// The IMovexInvoiceReader interface is unchanged — InvoiceProcessor is unaffected.
/// </summary>
public interface IMovexInvoiceReader
{
    /// <summary>
    /// Get all pending invoices (sales and purchase) from the specified date onwards.
    /// </summary>
    Task<List<MovexInvoice>> GetPendingInvoices(DateTime fromDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific invoice by number.
    /// </summary>
    Task<MovexInvoice?> GetInvoiceById(string invoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get invoice headers for a given date range.
    /// </summary>
    Task<List<MovexInvoice>> GetInvoicesByDateRange(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}

public class MovexInvoiceReader : IMovexInvoiceReader
{
    private readonly IInvoiceDataSource _dataSource;
    private readonly IPartyDataProvider _partyProvider;
    private readonly ForeignPartyDefaultsSettings _foreignDefaults;
    private readonly ILogger<MovexInvoiceReader> _logger;

    public MovexInvoiceReader(
        IInvoiceDataSource dataSource,
        IPartyDataProvider partyProvider,
        IOptions<ForeignPartyDefaultsSettings> foreignDefaults,
        ILogger<MovexInvoiceReader> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _partyProvider = partyProvider ?? throw new ArgumentNullException(nameof(partyProvider));
        _foreignDefaults = foreignDefaults?.Value ?? new ForeignPartyDefaultsSettings();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MovexInvoice>> GetPendingInvoices(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching pending invoices from MOVEX database. FromDate: {FromDate}", fromDate);

        var rawRecords = await _dataSource.GetPendingInvoicesAsync(fromDate, cancellationToken);

        _logger.LogInformation("Retrieved {Count} raw invoice records from DB2", rawRecords.Count);

        return await EnrichRecordsAsync(rawRecords, cancellationToken);
    }

    public async Task<MovexInvoice?> GetInvoiceById(string invoiceNumber, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching invoice {InvoiceNumber} from MOVEX database", invoiceNumber);

        // Try AP first, then AR
        var raw = await _dataSource.GetInvoiceByIdAsync(invoiceNumber, "AP", cancellationToken)
                  ?? await _dataSource.GetInvoiceByIdAsync(invoiceNumber, "AR", cancellationToken);

        if (raw == null)
        {
            _logger.LogWarning("Invoice {InvoiceNumber} not found in MOVEX database", invoiceNumber);
            return null;
        }

        var enriched = await EnrichRecordAsync(raw, cancellationToken);
        return enriched;
    }

    public async Task<List<MovexInvoice>> GetInvoicesByDateRange(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching invoices from MOVEX database. DateRange: {FromDate} to {ToDate}", fromDate, toDate);

        var rawRecords = await _dataSource.GetInvoicesByDateRangeAsync(fromDate, toDate, cancellationToken);

        _logger.LogInformation("Retrieved {Count} raw invoice records from DB2", rawRecords.Count);

        return await EnrichRecordsAsync(rawRecords, cancellationToken);
    }

    /// <summary>
    /// Enrich a list of raw records with party data and map to MovexInvoice DTOs.
    /// </summary>
    private async Task<List<MovexInvoice>> EnrichRecordsAsync(List<RawInvoiceRecord> records, CancellationToken cancellationToken)
    {
        var results = new List<MovexInvoice>(records.Count);
        foreach (var record in records)
        {
            var enriched = await EnrichRecordAsync(record, cancellationToken);
            results.Add(enriched);
        }
        return results;
    }

    /// <summary>
    /// Map a single RawInvoiceRecord to MovexInvoice with party enrichment.
    /// </summary>
    private async Task<MovexInvoice> EnrichRecordAsync(RawInvoiceRecord raw, CancellationToken cancellationToken)
    {
        // Determine party type and fetch details
        PartyDetails? partyDetails = raw.InvoiceType == "AP"
            ? await _partyProvider.GetSupplierDetailsAsync(raw.PartyId, cancellationToken)
            : await _partyProvider.GetCustomerDetailsAsync(raw.PartyId, cancellationToken);

        var invoiceDate = raw.InvoiceDate?.ToString() ?? raw.AccountingDate.ToString();

        var invoice = new MovexInvoice
        {
            InvoiceNumber = raw.InvoiceNo,
            InvoiceDate = invoiceDate,
            InvoiceType = raw.InvoiceType == "AP" ? "Purchase" : "Sales",
            CurrencyCode = raw.Currency.Trim(),
            ExchangeRate = raw.FxRate,
            TotalInclTax = raw.InvoiceAmount,
            TotalTax = raw.GstAmount,
            TotalExclTax = raw.InvoiceAmount - raw.GstAmount,
            CompanyCode = raw.CompanyCode,
            VoucherNumber = raw.VoucherNumber,
            Status = "Pending"
        };

        // Enrich party data with foreign party fallbacks
        if (partyDetails != null)
        {
            var party = CreateInvoiceParty(partyDetails, raw.InvoiceType);

            if (raw.InvoiceType == "AP")
                invoice.Supplier = party;
            else
                invoice.Buyer = party;
        }

        // Map line items from raw record (OINVOL + MITMAS)
        invoice.Lines = raw.Lines.Select(line => new InvoiceLine
        {
            LineNumber = line.LineNumber,
            ItemNumber = line.ItemNumber,
            Description = line.Description,
            ClassificationCode = line.ClassificationCode,
            Quantity = line.Quantity,
            UnitOfMeasure = line.UnitOfMeasure,
            UnitPrice = line.UnitPrice,
            LineTotal = line.LineTotal,
            TaxCode = line.TaxCode,
            TaxRate = line.TaxRate,
            TaxAmount = line.TaxAmount
        }).ToList();

        if (invoice.Lines.Count == 0)
        {
            _logger.LogWarning("Invoice {InvoiceNumber} has no line items", raw.InvoiceNo);
        }

        // Recalculate header totals from line items for both AP and AR.
        //
        // AP: fpledg.epvtam (GstAmount) is often 0 even when GST exists — the
        //     actual tax is in FGINAE (summed via LATERAL join into line TaxAmounts).
        //
        // AR: FSLEDG.ESCUAM is the full invoice amount across all deliveries.
        //     ODLINE lines (fetched via OINVOH.UHVONO) cover one voucher/delivery,
        //     so the header total must be recalculated from the lines actually fetched.
        //
        // LineTotal can be negative (credit notes/debit notes) — use ABS to normalize.
        if (invoice.Lines.Count > 0)
        {
            var lineExcl = invoice.Lines.Sum(l => Math.Abs(l.LineTotal));
            var lineTax = invoice.Lines.Sum(l => l.TaxAmount);
            invoice.TotalExclTax = lineExcl;
            invoice.TotalTax = lineTax;
            invoice.TotalInclTax = lineExcl + lineTax;

            // Normalize individual line totals to positive
            foreach (var line in invoice.Lines)
                line.LineTotal = Math.Abs(line.LineTotal);
        }

        return invoice;
    }

    /// <summary>
    /// Create InvoiceParty with foreign party fallback logic.
    /// Foreign parties (CountryCode != "MY") get generic MyInvois EI TINs.
    /// Domestic parties use TIN/BRN from MOVEX master data.
    /// </summary>
    private InvoiceParty CreateInvoiceParty(PartyDetails details, string invoiceType)
    {
        bool isForeign = string.IsNullOrWhiteSpace(details.CountryCode)
                         || !details.CountryCode.Equals("MY", StringComparison.OrdinalIgnoreCase);

        string? tin, brn;

        if (isForeign)
        {
            if (invoiceType == "AP")
            {
                tin = _foreignDefaults.SupplierTIN;
                brn = _foreignDefaults.SupplierBRN;
            }
            else
            {
                tin = _foreignDefaults.BuyerTIN;
                brn = _foreignDefaults.BuyerBRN;
            }

            _logger.LogInformation(
                "Foreign party {PartyId} (Country: {Country}): using default TIN={TIN}, BRN={BRN}",
                details.PartyId, details.CountryCode ?? "NULL", tin, brn);
        }
        else
        {
            tin = details.TIN;
            brn = details.BRN;

            if (string.IsNullOrWhiteSpace(tin))
            {
                _logger.LogWarning(
                    "Domestic party {PartyId} has no TIN. Configure MovexDbSettings TIN column mappings.",
                    details.PartyId);
            }
        }

        return new InvoiceParty
        {
            TIN = tin,
            Name = details.Name,
            BRN = brn,
            Address = details.Address,
            ContactPerson = details.ContactPerson,
            IdScheme = details.IdScheme
        };
    }
}
