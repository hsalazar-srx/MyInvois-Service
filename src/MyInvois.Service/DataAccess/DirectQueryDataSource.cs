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
/// Company isolation: Only companies listed in ActiveCompanyCodes are queried.
/// Production (CMP100) and testing (CMP300) are separated by environment config:
///   appsettings.Production.json → ActiveCompanyCodes: ["100"]
///   appsettings.Development.json → ActiveCompanyCodes: ["300"]
///
/// Line-item retrieval is delegated to <see cref="MovexLineItemFetcher"/>.
/// </summary>
public class DirectQueryDataSource : IInvoiceDataSource
{
    private readonly MovexDbSettings _settings;
    private readonly ILogger<DirectQueryDataSource> _logger;
    private readonly MovexLineItemFetcher _lineItemFetcher;
    private readonly Dictionary<string, string> _companySchemas;

    public DirectQueryDataSource(
        IOptions<MovexDbSettings>       settings,
        ILogger<DirectQueryDataSource>  logger,
        MovexLineItemFetcher            lineItemFetcher)
    {
        _settings        = settings?.Value   ?? throw new ArgumentNullException(nameof(settings));
        _logger          = logger            ?? throw new ArgumentNullException(nameof(logger));
        _lineItemFetcher = lineItemFetcher   ?? throw new ArgumentNullException(nameof(lineItemFetcher));

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
        // eptrcd = 40: Supplier Invoice in this installation — confirmed via GROUP BY: 40=invoice, 50=payment (non-standard codes)
        // BUG FIX (Sprint 7): was BETWEEN ? AND ? but only 1 param supplied — changed to >= ? (no upper bound for "pending")
        // BUG FIX (Sprint 8): eptrcd was incorrectly set to 50 — corrected after confirming 50=payment, 40=invoice in this installation
        // BUG FIX (Sprint 9): was epacdt (payment/accounting date) — changed to epivdt (supplier invoice date) per LHDN 7-day rule
        // BUG FIX (Sprint 9): eptrcd=10 returned no data — confirmed via SYSCOLUMNS that this installation uses 40=invoice, 50=payment
        // Country filter (idcscd <> 'MY') pending clarification: may need to include domestic suppliers — see Finance email thread
        var apWhere = "p.epivdt >= ? AND p.eptrcd = 40 AND p.epdivi = 'L' AND (s.idcscd IS NULL OR TRIM(s.idcscd) <> 'MY')";
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
                var sql        = BuildApHeaderSql(schema, "TRIM(p.epsino) = ? AND p.eptrcd = 40");
                var parameters = new DynamicParameters();
                parameters.Add("p0", invoiceNumber);

                record = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(sql, parameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .FirstOrDefault();

                if (record != null)
                {
                    record.InvoiceType = "AP";
                    record.CompanyCode = companyCode;
                }
            }
            else
            {
                var sql        = BuildArHeaderSql(schema, "TRIM(f.ESCINO) = ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND o.OKSTAT = ?");
                var parameters = new DynamicParameters();
                parameters.Add("p0", invoiceNumber);
                parameters.Add("p1", _settings.ArDivision);
                parameters.Add("p2", _settings.ArTransCode);
                parameters.Add("p3", _settings.ArCustomerStatus);

                record = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(sql, parameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .FirstOrDefault();

                if (record != null)
                {
                    record.InvoiceType = "AR";
                    record.CompanyCode = companyCode;
                }
            }

            if (record != null)
            {
                await _lineItemFetcher.FetchAndAttachAsync(
                    connection, schema, new List<RawInvoiceRecord> { record }, cancellationToken);
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
        // eptrcd = 40: Supplier Invoice in this installation (confirmed: 40=invoice, 50=payment)
        var apWhere = "p.epivdt BETWEEN ? AND ? AND p.eptrcd = 40 AND p.epdivi = 'L' AND (s.idcscd IS NULL OR TRIM(s.idcscd) <> 'MY')";
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

    // ── Private helpers ──────────────────────────────────────────────────────

    private string GetSchemaForCompany(string companyCode)
    {
        if (_companySchemas.TryGetValue(companyCode, out var schema))
            return schema;

        throw new ArgumentException($"Unknown company code: {companyCode}. Configure schema in MovexDbSettings.");
    }

    private OdbcConnection CreateConnection() => new(_settings.ConnectionString);

    private static int ToMovexDate(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private async Task<List<RawInvoiceRecord>> QueryAllCompaniesAsync(
        string apWhereClause, string arWhereClause,
        DynamicParameters apParameters, DynamicParameters arParameters,
        CancellationToken cancellationToken)
    {
        var allRecords = new List<RawInvoiceRecord>();

        foreach (var companyCode in _settings.ActiveCompanyCodes)
        {
            var schema = GetSchemaForCompany(companyCode);

            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);

                var apSql     = BuildApHeaderSql(schema, apWhereClause);
                var apRecords = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(apSql, apParameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var r in apRecords)
                {
                    r.InvoiceType = "AP";
                    r.CompanyCode = companyCode;
                }

                var arSql     = BuildArHeaderSql(schema, arWhereClause);
                var arRecords = (await connection.QueryAsync<RawInvoiceRecord>(
                    new CommandDefinition(arSql, arParameters,
                        commandTimeout: _settings.CommandTimeoutSeconds,
                        cancellationToken: cancellationToken)))
                    .ToList();

                foreach (var r in arRecords)
                {
                    r.InvoiceType = "AR";
                    r.CompanyCode = companyCode;
                }

                var companyRecords = apRecords.Concat(arRecords).ToList();

                if (companyRecords.Count > 0)
                    await _lineItemFetcher.FetchAndAttachAsync(connection, schema, companyRecords, cancellationToken);

                allRecords.AddRange(companyRecords);

                _logger.LogInformation("Company {CompanyCode}: {ApCount} AP + {ArCount} AR invoices fetched",
                    companyCode, apRecords.Count, arRecords.Count);
            }
            catch (OdbcException ex)
            {
                _logger.LogError(ex,
                    "DB2 query failed for company {CompanyCode}. SQLSTATE: {SqlState}, NativeError: {NativeError}",
                    companyCode, ex.Errors[0]?.SQLState, ex.Errors[0]?.NativeError);
                throw;
            }
        }

        _logger.LogInformation("Total invoices fetched: {Total} across {CompanyCount} companies",
            allRecords.Count, _settings.ActiveCompanyCodes.Count);

        return allRecords;
    }

    // ── Header SQL builders ──────────────────────────────────────────────────

    private static string BuildApHeaderSql(string schema, string whereClause) =>
        $@"SELECT DISTINCT
            TRIM(p.epsuno) AS PartyId,
            TRIM(p.epsino) AS InvoiceNo,
            p.epacdt AS AccountingDate,
            p.epivdt AS InvoiceDate,
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
            TRIM(f.ESPYNO) AS PayerNo,
            TRIM(CHAR(f.ESVONO)) AS VoucherNumber,
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
}
