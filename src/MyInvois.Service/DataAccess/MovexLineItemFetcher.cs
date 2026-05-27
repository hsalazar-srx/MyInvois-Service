namespace MyInvois.Service.DataAccess;

using System.Data.Odbc;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess.Dtos;

/// <summary>
/// Fetches AR and AP invoice line items from MOVEX DB2 tables and attaches them to
/// the supplied header records. Extracted from DirectQueryDataSource to keep SQL query
/// orchestration separate from line-item retrieval.
/// </summary>
public sealed class MovexLineItemFetcher
{
    private readonly MovexDbSettings _settings;
    private readonly ILogger<MovexLineItemFetcher> _logger;

    public MovexLineItemFetcher(
        IOptions<MovexDbSettings>           settings,
        ILogger<MovexLineItemFetcher>       logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger   = logger          ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dispatches AR and AP line-item fetches for a mixed list of headers.
    /// </summary>
    public async Task FetchAndAttachAsync(
        OdbcConnection            connection,
        string                    schema,
        List<RawInvoiceRecord>    headers,
        CancellationToken         cancellationToken)
    {
        var arHeaders = headers.Where(h => h.InvoiceType == "AR").ToList();
        var apHeaders = headers.Where(h => h.InvoiceType == "AP").ToList();

        if (arHeaders.Count > 0)
            await FetchArLineItemsAsync(connection, schema, arHeaders, cancellationToken);

        if (apHeaders.Count > 0)
            await FetchApLineItemsAsync(connection, schema, apHeaders, cancellationToken);
    }

    // ── AR ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch AR (sales) invoice line items via FSLEDG→OINVOH→ODLINE→OOLINE.
    /// Key: FSLEDG.ESVONO = OINVOH.UHVONO (voucher number links ledger to invoice header).
    /// OINVOH.UHIVNO = ODLINE.UBIVNO (internal invoice number links header to delivery lines).
    /// </summary>
    private async Task FetchArLineItemsAsync(
        OdbcConnection         connection,
        string                 schema,
        List<RawInvoiceRecord> headers,
        CancellationToken      cancellationToken)
    {
        try
        {
            var invoiceKeys = headers
                .Where(h => !string.IsNullOrWhiteSpace(h.VoucherNumber))
                .Select(h => (Cino: h.InvoiceNo, Vono: h.VoucherNumber))
                .Distinct()
                .ToList();

            if (invoiceKeys.Count == 0)
            {
                _logger.LogWarning("No AR headers have VoucherNumber (ESVONO) set — AR line items cannot be fetched.");
                return;
            }

            var companyCode = int.Parse(headers[0].CompanyCode);
            const int batchSize = 100;

            for (var i = 0; i < invoiceKeys.Count; i += batchSize)
            {
                var batch = invoiceKeys.Skip(i).Take(batchSize).ToList();
                var sql   = BuildArLineItemsSql(schema, batch.Count);

                var parameters = new DynamicParameters();
                parameters.Add("pCono", companyCode);
                for (var j = 0; j < batch.Count; j++)
                {
                    // ESVONO is DECIMAL in DB2 — pass as long
                    if (long.TryParse(batch[j].Vono, out var vono))
                        parameters.Add($"p{j}", vono);
                    else
                        parameters.Add($"p{j}", batch[j].Vono);
                }

                var lineItems = (await connection.QueryAsync<ArLineItemDto>(
                    new CommandDefinition(sql, parameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var group in lineItems.GroupBy(l => l.InvoiceNo))
                {
                    var header = headers.FirstOrDefault(h => h.InvoiceNo == group.Key);
                    if (header != null)
                        header.Lines = group.Select(l => l.ToLineRecord()).ToList();
                }
            }

            _logger.LogInformation(
                "Fetched AR line items for {Count} invoices from OINVOH→ODLINE in schema {Schema}",
                invoiceKeys.Count, schema);
        }
        catch (OdbcException ex)
        {
            _logger.LogError(ex,
                "Failed to fetch AR line items from OINVOH→ODLINE for schema {Schema}. " +
                "SQLSTATE: {SqlState}, NativeError: {NativeError}",
                schema, ex.Errors[0]?.SQLState, ex.Errors[0]?.NativeError);
        }
    }

    // ── AP ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch AP (purchase) invoice line items from FGINLI + MPLINE + MITMAS + FGINAE.
    /// AP invoices are keyed by (SUNO, SINO) — unlike AR which uses a single invoice number.
    /// </summary>
    private async Task FetchApLineItemsAsync(
        OdbcConnection         connection,
        string                 schema,
        List<RawInvoiceRecord> headers,
        CancellationToken      cancellationToken)
    {
        try
        {
            const int batchSize = 100;

            // Build unique (SUNO, SINO) tuples — INYR excluded because FGINLI.F5INYR
            // can differ from FPLEDG voucher year (year-end invoice timing).
            var invoiceKeys = headers
                .Select(h => (Supplier: h.PartyId, Invoice: h.InvoiceNo))
                .Distinct()
                .ToList();

            var companyCode = int.Parse(headers[0].CompanyCode);

            for (var i = 0; i < invoiceKeys.Count; i += batchSize)
            {
                var batch = invoiceKeys.Skip(i).Take(batchSize).ToList();
                var sql   = BuildApLineItemsSql(schema, batch.Count);

                var parameters = new DynamicParameters();
                parameters.Add("pCono", companyCode);

                var paramIndex = 0;
                foreach (var key in batch)
                {
                    parameters.Add($"p{paramIndex++}", key.Supplier);
                    parameters.Add($"p{paramIndex++}", key.Invoice);
                }

                var lineItems = (await connection.QueryAsync<ApLineItemDto>(
                    new CommandDefinition(sql, parameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var group in lineItems.GroupBy(l => (l.SupplierId, l.SupplierInvoiceNo)))
                {
                    var header = headers.FirstOrDefault(h =>
                        h.PartyId == group.Key.SupplierId && h.InvoiceNo == group.Key.SupplierInvoiceNo);
                    if (header != null)
                        header.Lines = group.Select(l => l.ToLineRecord()).ToList();
                }
            }

            _logger.LogInformation(
                "Fetched AP line items for {Count} invoices from FGINLI in schema {Schema}",
                invoiceKeys.Count, schema);
        }
        catch (OdbcException ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch AP line items from FGINLI for schema {Schema}. " +
                "Continuing without AP line items.", schema);
        }
    }

    // ── SQL builders ───────────────────────────────────────────────────────────

    /// <summary>
    /// Build SQL for AR line items.
    /// Path: FSLEDG → OINVOH (ESVONO=UHVONO) → ODLINE (UHIVNO=UBIVNO) → OOLINE
    ///
    /// Coverage: 117/122 (96%) of 2026 AR invoices. 5 misses are credit notes / year-end adjustments.
    /// Classification code (LHDN table) is NOT stored in MOVEX — defaults to '022' in ToLineRecord().
    /// Parameters: CONO first, then one ESVONO per invoice.
    /// </summary>
    private static string BuildArLineItemsSql(string schema, int batchSize)
    {
        var placeholders = string.Join(",", Enumerable.Range(0, batchSize).Select(_ => "?"));
        return $@"SELECT
            TRIM(f.ESCINO) AS InvoiceNo,
            ROW_NUMBER() OVER (PARTITION BY f.ESCINO ORDER BY dl.UBPONR, dl.UBPOSX) AS LineNumber,
            TRIM(dl.UBITNO) AS ItemNumber,
            COALESCE(TRIM(ol.OBITDS), TRIM(dl.UBITNO), '') AS Description,
            dl.UBIVQT AS Quantity,
            COALESCE(TRIM(dl.UBSPUN), 'EA') AS UnitOfMeasure,
            dl.UBSAPR AS UnitPrice,
            dl.UBLNAM AS LineTotal,
            COALESCE(TRIM(ol.OBVTCD), '') AS TaxCode,
            0 AS TaxAmount
        FROM {schema}.FSLEDG f
        JOIN {schema}.OINVOH oh
            ON f.ESCONO = oh.UHCONO
            AND f.ESVONO = oh.UHVONO
        JOIN {schema}.ODLINE dl
            ON oh.UHCONO = dl.UBCONO
            AND oh.UHIVNO = dl.UBIVNO
        LEFT JOIN {schema}.OOLINE ol
            ON dl.UBCONO = ol.OBCONO
            AND TRIM(dl.UBORNO) = TRIM(ol.OBORNO)
            AND dl.UBPONR = ol.OBPONR
            AND dl.UBPOSX = ol.OBPOSX
        WHERE f.ESCONO = ?
          AND f.ESVONO IN ({placeholders})
        ORDER BY f.ESCINO, dl.UBPONR, dl.UBPOSX";
    }

    /// <summary>
    /// Build SQL for AP line items from FGINLI + MPLINE.
    /// FGINLI: line-level qty/price/net amount and VAT code.
    /// MPLINE: item number, description, and U/M via PO reference.
    /// FGINAE: AP invoices are zero-rated for Malaysian SST — TaxAmount is always 0.
    ///
    /// Validated against: src/Database/FGINLI_FGINAE_AP_LineItems_Validation.sql (Query 0e)
    /// </summary>
    private static string BuildApLineItemsSql(string schema, int batchSize)
    {
        // AP invoices matched by (SUNO, SINO) only — INYR in FGINLI can differ from
        // the GL voucher year in FPLEDG (e.g. invoice entered in Dec but voucher posted Jan).
        var valueTuples = string.Join(",", Enumerable.Range(0, batchSize).Select(_ => "(?, ?)"));
        return $@"SELECT
            TRIM(li.F5SUNO) AS SupplierId,
            TRIM(li.F5SINO) AS SupplierInvoiceNo,
            li.F5INYR AS InvoiceYear,
            ROW_NUMBER() OVER (
                PARTITION BY li.F5SUNO, li.F5SINO
                ORDER BY li.F5PUNO, li.F5PNLI
            ) AS LineNumber,
            TRIM(COALESCE(po.IBITNO, '')) AS ItemNumber,
            TRIM(COALESCE(po.IBPITD, 'Purchase Line')) AS Description,
            '022' AS ClassificationCode,
            li.F5IVQT AS Quantity,
            COALESCE(TRIM(po.IBPUUN), 'EA') AS UnitOfMeasure,
            CASE WHEN li.F5IVQT <> 0
                 THEN li.F5IVNA / li.F5IVQT
                 ELSE li.F5IVOC
            END AS UnitPrice,
            li.F5IVNA AS LineTotal,
            COALESCE(TRIM(li.F5VTCD), '') AS TaxCode,
            COALESCE(vat.VatAmount, 0) AS TaxAmount
        FROM {schema}.FGINLI li
        LEFT JOIN {schema}.MPLINE po
            ON li.F5CONO = po.IBCONO AND li.F5PUNO = po.IBPUNO AND li.F5PNLI = po.IBPNLI
        LEFT JOIN LATERAL (
            -- All EPTRCD=10 AP invoices are zero-rated for Malaysian SST (confirmed 2026-05-14):
            -- FPLEDG.EPVTAM = 0 on every supplier invoice row. FGINAE contains no VAT entry
            -- type rows (F9INIT=12) for EPTRCD=10 invoices — only goods cost (10), freight (11),
            -- and variance (18) entries exist. TaxAmount is always 0 for this transaction scope.
            SELECT 0 AS VatAmount FROM SYSIBM.SYSDUMMY1
        ) vat ON 1=1
        WHERE li.F5CONO = ? AND li.F5DIVI = 'L'
          AND (li.F5SUNO, li.F5SINO) IN (VALUES {valueTuples})
        ORDER BY li.F5SUNO, li.F5SINO, li.F5PUNO, li.F5PNLI";
    }
}
