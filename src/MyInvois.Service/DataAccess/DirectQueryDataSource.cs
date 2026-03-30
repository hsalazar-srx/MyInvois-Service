namespace MyInvois.Service.DataAccess;

using System.Data.Odbc;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;

// Uses skill: integration/movex-db2-data-source v1.0+

/// <summary>
/// Direct SQL query implementation of IInvoiceDataSource.
/// Queries MOVEX ledger tables (fpledg, fsledg, fgledg) on IBM DB2/AS400.
/// SQL patterns from src/Database/AP_AR_Invoices_CMP100_CMP300.sql
///
/// NuGet dependencies:
/// - Dapper (lightweight result mapping)
/// - System.Data.Odbc (DB2 connectivity via ODBC driver)
///
/// Company isolation: Only companies listed in ActiveCompanyCodes are queried.
/// Production (CMP100) and testing (CMP300) are separated by environment config:
///   appsettings.Production.json → ActiveCompanyCodes: ["100"]
///   appsettings.Development.json → ActiveCompanyCodes: ["300"]
/// </summary>
public class DirectQueryDataSource : IInvoiceDataSource
{
    private readonly MovexDbSettings _settings;
    private readonly ILogger<DirectQueryDataSource> _logger;
    private readonly Dictionary<string, string> _companySchemas;

    public DirectQueryDataSource(
        IOptions<MovexDbSettings> settings,
        ILogger<DirectQueryDataSource> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Build schema lookup from settings — extensible without hardcoded switch
        _companySchemas = new Dictionary<string, string>
        {
            ["100"] = _settings.SchemaCmp100,
            ["300"] = _settings.SchemaCmp300
        };
    }

    public async Task<List<RawInvoiceRecord>> GetPendingInvoicesAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching pending invoices from DB2. FromDate: {FromDate}, Companies: {Companies}",
            fromDate.ToString("yyyyMMdd"), string.Join(",", _settings.ActiveCompanyCodes));

        // DB2 i5/OS requires positional parameters (?) not named parameters (@)
        // eptrcd = 10: Supplier Invoice only — excludes payments (20), write-offs (30), adjustments (40), FX (50), reversals (90)
        var apWhere = "p.epacdt BETWEEN ? AND ? AND p.eptrcd = 50 AND p.epdivi = 'L' AND (s.idcscd IS NULL OR TRIM(s.idcscd) <> 'MY')";// AND p.eptrcd = 10";
       //var arWhere = "f.ESRGDT >= ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND f.ESCHNO = 0 AND o.OKSTAT = ? AND f.ESYEA4 > ?";
        var arWhere = "f.ESRGDT >= ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND o.OKSTAT = ? AND f.ESYEA4 > ?";

        var apParams = new DynamicParameters();
        apParams.Add("p0", ToMovexDate(fromDate));

        var arParams = new DynamicParameters();
        arParams.Add("p0", ToMovexDate(fromDate));
        arParams.Add("p1", _settings.ArDivision);
        arParams.Add("p2", _settings.ArTransCode);
        arParams.Add("p3", _settings.ArCustomerStatus);
        arParams.Add("p4", _settings.ArMinYear);

        return await QueryAllCompaniesAsync(apWhere, arWhere, apParams, arParams, cancellationToken);
    }

    public async Task<RawInvoiceRecord?> GetInvoiceByIdAsync(string invoiceNumber, string invoiceType, CancellationToken cancellationToken = default)
    {
        if (invoiceType != "AP" && invoiceType != "AR")
            throw new ArgumentException($"Invalid invoice type: {invoiceType}. Must be 'AP' or 'AR'.", nameof(invoiceType));

        _logger.LogInformation("Fetching invoice by ID from DB2. InvoiceNo: {InvoiceNo}, Type: {InvoiceType}",
            invoiceNumber, invoiceType);

        foreach (var companyCode in _settings.ActiveCompanyCodes)
        {
            var schema = GetSchemaForCompany(companyCode);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            RawInvoiceRecord? record = null;

            if (invoiceType == "AP")
            {
                var sql = BuildApHeaderSql(schema, "TRIM(p.epsino) = ?");// AND p.eptrcd = 10");
                var parameters = new DynamicParameters();
                parameters.Add("p0", invoiceNumber);

                record = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .FirstOrDefault();

                if (record != null)
                {
                    record.InvoiceType = "AP";
                    record.CompanyCode = companyCode;
                }
            }
            else
            {
                var sql = BuildArHeaderSql(schema, "TRIM(f.ESCINO) = ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND o.OKSTAT = ?");
                var parameters = new DynamicParameters();
                parameters.Add("p0", invoiceNumber);
                parameters.Add("p1", _settings.ArDivision);
                parameters.Add("p2", _settings.ArTransCode);
                parameters.Add("p3", _settings.ArCustomerStatus);

                record = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .FirstOrDefault();

                if (record != null)
                {
                    record.InvoiceType = "AR";
                    record.CompanyCode = companyCode;
                }
            }

            if (record != null)
            {
                await FetchLineItemsAsync(connection, schema, new List<RawInvoiceRecord> { record }, cancellationToken);
                return record;
            }
        }

        return null;
    }

    public async Task<List<RawInvoiceRecord>> GetInvoicesByDateRangeAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching invoices by date range from DB2. From: {FromDate}, To: {ToDate}",
            fromDate.ToString("yyyyMMdd"), toDate.ToString("yyyyMMdd"));

        // DB2 i5/OS requires positional parameters (?) not named parameters (@)
        // eptrcd = 10: Supplier Invoice only — excludes payments (20), write-offs (30), adjustments (40), FX (50), reversals (90)
        var apWhere = "p.epacdt BETWEEN ? AND ? AND p.eptrcd = 50 AND p.epdivi = 'L' AND (s.idcscd IS NULL OR TRIM(s.idcscd) <> 'MY')";// AND p.eptrcd = 10";
        var arWhere = "f.ESRGDT BETWEEN ? AND ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND o.OKSTAT = ? AND f.ESYEA4 > ?";

        var apParams = new DynamicParameters();
        apParams.Add("p0", ToMovexDate(fromDate));
        apParams.Add("p1", ToMovexDate(toDate));

        var arParams = new DynamicParameters();
        arParams.Add("p0", ToMovexDate(fromDate));
        arParams.Add("p1", ToMovexDate(toDate));
        arParams.Add("p2", _settings.ArDivision);
        arParams.Add("p3", _settings.ArTransCode);
        arParams.Add("p4", _settings.ArCustomerStatus);
        arParams.Add("p5", _settings.ArMinYear);

        return await QueryAllCompaniesAsync(apWhere, arWhere, apParams, arParams, cancellationToken);
    }

    /// <summary>
    /// Get the schema name for a company code.
    /// CMP100 → mvxcdta, CMP300 → mvxc300 (configurable via MovexDbSettings)
    /// </summary>
    private string GetSchemaForCompany(string companyCode)
    {
        if (_companySchemas.TryGetValue(companyCode, out var schema))
            return schema;

        throw new ArgumentException($"Unknown company code: {companyCode}. Configure schema in MovexDbSettings.");
    }

    private OdbcConnection CreateConnection() => new(_settings.ConnectionString);

    private static int ToMovexDate(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private async Task<List<RawInvoiceRecord>> QueryAllCompaniesAsync(
        string apWhereClause, string arWhereClause, DynamicParameters apParameters, DynamicParameters arParameters, CancellationToken cancellationToken)
    {
        var allRecords = new List<RawInvoiceRecord>();

        foreach (var companyCode in _settings.ActiveCompanyCodes)
        {
            var schema = GetSchemaForCompany(companyCode);

            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                // Query AP (Purchase) invoices
                var apSql = BuildApHeaderSql(schema, apWhereClause);
                var apRecords = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(apSql, apParameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var r in apRecords)
                {
                    r.InvoiceType = "AP";
                    r.CompanyCode = companyCode;
                }

                // Query AR (Sales) invoices — no GstAmount column in fsledg
                var arSql = BuildArHeaderSql(schema, arWhereClause);
                var arRecords = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(arSql, arParameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var r in arRecords)
                {
                    r.InvoiceType = "AR";
                    r.CompanyCode = companyCode;
                }

                var companyRecords = apRecords.Concat(arRecords).ToList();

                // Fetch line items for all headers in this company
                if (companyRecords.Count > 0)
                {
                    await FetchLineItemsAsync(connection, schema, companyRecords, cancellationToken);
                }

                allRecords.AddRange(companyRecords);

                _logger.LogInformation("Company {CompanyCode}: {ApCount} AP + {ArCount} AR invoices fetched",
                    companyCode, apRecords.Count, arRecords.Count);
            }
            catch (OdbcException ex)
            {
                _logger.LogError(ex, "DB2 query failed for company {CompanyCode}. SQLSTATE: {SqlState}, NativeError: {NativeError}",
                    companyCode, ex.Errors[0]?.SQLState, ex.Errors[0]?.NativeError);
                throw;
            }
        }

        _logger.LogInformation("Total invoices fetched: {Total} across {CompanyCount} companies",
            allRecords.Count, _settings.ActiveCompanyCodes.Count);

        return allRecords;
    }

    private static string BuildApHeaderSql(string schema, string whereClause) =>
        $@"SELECT DISTINCT
            TRIM(p.epsuno) AS PartyId,
            TRIM(p.epsino) AS InvoiceNo,
            p.epacdt AS AccountingDate,
            TRIM(p.epvono) AS VoucherNumber,
            p.epyea4 AS VoucherYear,
            TRIM(p.epcucd) AS Currency,
            p.eparat AS FxRate,
            p.epcuam AS InvoiceAmount,
            p.epvtam AS GstAmount,
            TRIM(g.egait1) AS GlCode
        FROM {schema}.fpledg p
        LEFT JOIN (
            SELECT 
                egcono,
                egdivi,
                egyea4,
                egvono,
                MIN(egait1) AS egait1
            FROM {schema}.fgledg
            WHERE TRIM(egait1) NOT IN ('769')   -- exclude rows with AP control account 769
            GROUP BY 
                egcono,
                egdivi,
                egyea4,
                egvono
        ) g ON p.epcono = g.egcono
            AND p.epdivi = g.egdivi
            AND p.epyea4 = g.egyea4
            AND p.epvono = g.egvono
        LEFT JOIN {schema}.cidmas s
            ON p.epsuno = s.idsuno
        WHERE {whereClause}";

    private static string BuildArHeaderSql(string schema, string whereClause) =>
        $@"SELECT
            TRIM(f.ESCUNO) AS PartyId,
            TRIM(f.ESCINO) AS InvoiceNo,
            f.ESRGDT AS InvoiceEntryDate,
            f.ESCHNO AS ChangeVersion,
            TRIM(f.ESCUCD) AS Currency,
            f.ESCUAM AS InvoiceAmount,
            TRIM(f.ESDIVI) AS Division,
            TRIM(f.ESTRCD) AS TransCode,
            TRIM(o.OKSTAT) AS CustomerStatus,
            TRIM(o.OKCUNM) AS CustomerName,
            TRIM(o.OKCUA1) AS MasterAddress1,
            TRIM(o.OKCUA2) AS MasterAddress2,
            TRIM(o.OKCUA3) AS MasterAddress3,
            TRIM(o.OKCUA4) AS MasterAddress4,
            TRIM(a.OPCUNM) AS InvoiceeName,
            TRIM(a.OPCUA1) AS Address1,
            TRIM(a.OPCUA2) AS Address2,
            TRIM(a.OPCUA3) AS Address3,
            TRIM(a.OPCUA4) AS Address4,
            TRIM(a.OPPONO) AS PostCode
        FROM {schema}.FSLEDG f
        JOIN {schema}.OCUSMA o
            ON f.ESCONO = o.OKCONO AND f.ESCUNO = o.OKCUNO
        LEFT JOIN {schema}.OCUSAD a
            ON a.OPCONO = f.ESCONO AND a.OPCUNO = f.ESCUNO AND a.OPADID = 'INV01'
        WHERE {whereClause}";

    /// <summary>
    /// Build SQL for AR (sales) invoice line items from OINVOL + MITMAS.
    /// </summary>
    private static string BuildArLineItemsSql(string schema, int batchSize)
    {
        var placeholders = string.Join(",", Enumerable.Range(0, batchSize).Select(i => "?"));
        return $@"SELECT
            TRIM(ol.OIIVNO) AS InvoiceNo,
            ol.OILVNO AS LineNumber,
            TRIM(ol.OILITNO) AS ItemNumber,
            TRIM(ol.OILITDS) AS Description,
            COALESCE(TRIM(im.ITCL), '000') AS ClassificationCode,
            ol.OILQA AS Quantity,
            COALESCE(TRIM(ol.OILUN), 'EA') AS UnitOfMeasure,
            ol.OILSA AS UnitPrice,
            ol.OILQA * ol.OILSA AS LineTotal,
            --COALESCE(TRIM(ol.OILVTCD), '') AS TaxCode,
            --COALESCE(ol.OILVTRT, 0) AS TaxRate,
            COALESCE(ol.ONVTAM, 0) AS TaxAmount
        FROM {schema}.OINVOL ol
        LEFT JOIN {schema}.MITMAS im ON ol.OILITNO = im.ITNO
        WHERE TRIM(ol.OIIVNO) IN ({placeholders})
        ORDER BY ol.OIIVNO, ol.OILVNO";
    }

    /// <summary>
    /// Build SQL for AP (purchase) invoice line items from FGINLI + MPLINE.
    /// FGINLI provides line-level qty/price/net amount and VAT code.
    /// MPLINE provides item number, description, and U/M via PO reference.
    ///
    /// Tax note: Scanfil APAC AP invoices have zero VAT (FPLEDG.EPVTAM = 0).
    /// The FGINLI.F5VTCD field carries the M3 VAT code (e.g., "10") which maps
    /// to LHDN tax type "06" (Not Applicable) in the MyInvois mapper layer.
    /// FGINAE is not needed — it stores goods cost allocation, not separate VAT amounts.
    /// </summary>
    private static string BuildApLineItemsSql(string schema, int batchSize)
    {
        // Each AP invoice is identified by (SUNO, SINO, INYR) — 3 params per invoice
        var valueTuples = string.Join(",", Enumerable.Range(0, batchSize).Select(i => $"(?, ?, ?)"));
        return $@"SELECT
            TRIM(li.F5SUNO) AS SupplierId,
            TRIM(li.F5SINO) AS SupplierInvoiceNo,
            li.F5INYR AS InvoiceYear,
            ROW_NUMBER() OVER (
                PARTITION BY li.F5SUNO, li.F5SINO, li.F5INYR
                ORDER BY li.F5PUNO, li.F5PNLI
            ) AS LineNumber,
            TRIM(COALESCE(po.IBITNO, '')) AS ItemNumber,
            TRIM(COALESCE(po.IBPITD, 'Purchase Line')) AS Description,
            li.F5IVQT AS Quantity,
            COALESCE(TRIM(po.IBPUUN), 'EA') AS UnitOfMeasure,
            CASE WHEN li.F5IVQT <> 0
                 THEN li.F5IVNA / li.F5IVQT
                 ELSE li.F5IVOC
            END AS UnitPrice,
            li.F5IVNA AS LineTotal,
            COALESCE(TRIM(li.F5VTCD), '') AS TaxCode,
            0 AS TaxAmount
        FROM {schema}.FGINLI li
        LEFT JOIN {schema}.MPLINE po
            ON li.F5CONO = po.IBCONO AND li.F5PUNO = po.IBPUNO AND li.F5PNLI = po.IBPNLI
        WHERE li.F5CONO = ? AND li.F5DIVI = 'L'
          AND (li.F5SUNO, li.F5SINO, li.F5INYR) IN (VALUES {valueTuples})
        ORDER BY li.F5SUNO, li.F5SINO, li.F5PUNO, li.F5PNLI";
    }

    private async Task FetchLineItemsAsync(
        OdbcConnection connection, string schema, List<RawInvoiceRecord> headers, CancellationToken cancellationToken)
    {
        var arHeaders = headers.Where(h => h.InvoiceType == "AR").ToList();
        var apHeaders = headers.Where(h => h.InvoiceType == "AP").ToList();

        if (arHeaders.Count > 0)
            await FetchArLineItemsAsync(connection, schema, arHeaders, cancellationToken);

        if (apHeaders.Count > 0)
            await FetchApLineItemsAsync(connection, schema, apHeaders, cancellationToken);
    }

    /// <summary>
    /// Fetch AR (sales) invoice line items from OINVOL + MITMAS.
    /// </summary>
    private async Task FetchArLineItemsAsync(
        OdbcConnection connection, string schema, List<RawInvoiceRecord> headers, CancellationToken cancellationToken)
    {
        try
        {
            const int batchSize = 100;
            var invoiceNumbers = headers.Select(h => h.InvoiceNo).Distinct().ToList();

            for (var i = 0; i < invoiceNumbers.Count; i += batchSize)
            {
                var batch = invoiceNumbers.Skip(i).Take(batchSize).ToList();
                var sql = BuildArLineItemsSql(schema, batch.Count);

                var parameters = new DynamicParameters();
                for (var j = 0; j < batch.Count; j++)
                {
                    parameters.Add($"p{j}", batch[j]);
                }

                var lineItems = (await connection.QueryAsync<ArLineItemDto>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .ToList();

                var grouped = lineItems.GroupBy(l => l.InvoiceNo);
                foreach (var group in grouped)
                {
                    var header = headers.FirstOrDefault(h => h.InvoiceNo == group.Key);
                    if (header != null)
                    {
                        header.Lines = group.Select(l => l.ToLineRecord()).ToList();
                    }
                }
            }
        }
        catch (OdbcException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch AR line items from OINVOL for schema {Schema}. " +
                "Continuing without AR line items.", schema);
        }
    }

    /// <summary>
    /// Fetch AP (purchase) invoice line items from FGINLI + MPLINE.
    /// AP invoices are keyed by (SUNO, SINO, INYR) — unlike AR which uses a single invoice number.
    /// </summary>
    private async Task FetchApLineItemsAsync(
        OdbcConnection connection, string schema, List<RawInvoiceRecord> headers, CancellationToken cancellationToken)
    {
        try
        {
            const int batchSize = 100;

            // Build unique (SUNO, SINO, INYR) tuples for batching
            var invoiceKeys = headers
                .Select(h => (Supplier: h.PartyId, Invoice: h.InvoiceNo, Year: h.VoucherYear))
                .Distinct()
                .ToList();

            for (var i = 0; i < invoiceKeys.Count; i += batchSize)
            {
                var batch = invoiceKeys.Skip(i).Take(batchSize).ToList();
                var sql = BuildApLineItemsSql(schema, batch.Count);

                var parameters = new DynamicParameters();
                // First param is CONO
                var companyCode = int.Parse(headers[0].CompanyCode);
                parameters.Add("pCono", companyCode);

                // Each invoice key = 3 positional params (SUNO, SINO, INYR)
                var paramIndex = 0;
                foreach (var key in batch)
                {
                    parameters.Add($"p{paramIndex++}", key.Supplier);
                    parameters.Add($"p{paramIndex++}", key.Invoice);
                    parameters.Add($"p{paramIndex++}", key.Year);
                }

                var lineItems = (await connection.QueryAsync<ApLineItemDto>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken)))
                    .ToList();

                // Group by supplier + invoice no and assign to matching headers
                var grouped = lineItems.GroupBy(l => (l.SupplierId, l.SupplierInvoiceNo));
                foreach (var group in grouped)
                {
                    var header = headers.FirstOrDefault(h =>
                        h.PartyId == group.Key.SupplierId && h.InvoiceNo == group.Key.SupplierInvoiceNo);
                    if (header != null)
                    {
                        header.Lines = group.Select(l => l.ToLineRecord()).ToList();
                    }
                }
            }

            _logger.LogInformation("Fetched AP line items for {Count} invoices from FGINLI in schema {Schema}",
                invoiceKeys.Count, schema);
        }
        catch (OdbcException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch AP line items from FGINLI for schema {Schema}. " +
                "Continuing without AP line items.", schema);
        }
    }

    /// <summary>
    /// Internal DTO for AR line item query (OINVOL) — includes InvoiceNo for grouping.
    /// </summary>
    private class ArLineItemDto
    {
        public string InvoiceNo { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ItemNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClassificationCode { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string UnitOfMeasure { get; set; } = "EA";
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string TaxCode { get; set; } = string.Empty;
        public decimal TaxRate { get; set; }
        public decimal TaxAmount { get; set; }

        public RawInvoiceLineRecord ToLineRecord() => new()
        {
            LineNumber = LineNumber,
            ItemNumber = ItemNumber,
            Description = Description,
            ClassificationCode = ClassificationCode,
            Quantity = Quantity,
            UnitOfMeasure = UnitOfMeasure,
            UnitPrice = UnitPrice,
            LineTotal = LineTotal,
            TaxCode = TaxCode,
            TaxRate = TaxRate,
            TaxAmount = TaxAmount
        };
    }

    /// <summary>
    /// Internal DTO for AP line item query (FGINLI + MPLINE).
    /// Uses composite key (SupplierId + SupplierInvoiceNo) for grouping.
    /// </summary>
    private class ApLineItemDto
    {
        public string SupplierId { get; set; } = string.Empty;
        public string SupplierInvoiceNo { get; set; } = string.Empty;
        public int InvoiceYear { get; set; }
        public int LineNumber { get; set; }
        public string ItemNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string UnitOfMeasure { get; set; } = "EA";
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string TaxCode { get; set; } = string.Empty;
        public decimal TaxAmount { get; set; }

        public RawInvoiceLineRecord ToLineRecord() => new()
        {
            LineNumber = LineNumber,
            ItemNumber = ItemNumber,
            Description = Description,
            ClassificationCode = string.Empty, // LHDN classification assigned in mapper layer, not from M3
            Quantity = Quantity,
            UnitOfMeasure = UnitOfMeasure,
            UnitPrice = UnitPrice,
            LineTotal = LineTotal,
            TaxCode = TaxCode,
            TaxRate = 0, // AP invoices have zero VAT for Scanfil APAC
            TaxAmount = TaxAmount
        };
    }
}
