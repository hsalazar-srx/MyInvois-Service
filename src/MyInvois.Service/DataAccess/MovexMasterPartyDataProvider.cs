namespace MyInvois.Service.DataAccess;

using System.Data.Odbc;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyInvois.Service.Configuration;

// Uses skill: integration/movex-db2-data-source v1.0+

/// <summary>
/// Queries MOVEX master tables for party details via Dapper+ODBC.
/// - Supplier data: CIDMAS (supplier master) — Name, Address, TIN, BRN
/// - Customer data: OCUSMA (customer master) — Name, Address, TIN, BRN
///
/// TIN/BRN column mapping: Configured via MovexDbSettings.SupplierTinColumn / CustomerTinColumn.
/// Defaults to null when columns are not yet mapped (Finance to provide column names).
/// When configured, the SQL dynamically selects the specified column.
///
/// ADR-013 Known Gap #1: TIN/BRN column mappings pending Finance team confirmation.
/// Name and address fields use standard M3 columns (confirmed).
/// </summary>
public class MovexMasterPartyDataProvider : IPartyDataProvider
{
    private readonly MovexDbSettings _settings;
    private readonly ForeignPartyDefaultsSettings _foreignDefaults;
    private readonly ILogger<MovexMasterPartyDataProvider> _logger;
    private readonly Dictionary<string, string> _companySchemas;

    public MovexMasterPartyDataProvider(
        IOptions<MovexDbSettings> settings,
        IOptions<ForeignPartyDefaultsSettings> foreignDefaults,
        ILogger<MovexMasterPartyDataProvider> logger)
    {
        _settings        = settings?.Value        ?? throw new ArgumentNullException(nameof(settings));
        _foreignDefaults = foreignDefaults?.Value ?? throw new ArgumentNullException(nameof(foreignDefaults));
        _logger          = logger                 ?? throw new ArgumentNullException(nameof(logger));

        _companySchemas = new Dictionary<string, string>
        {
            ["100"] = _settings.SchemaCmp100,
            ["300"] = _settings.SchemaCmp300
        };
    }

    public async Task<PartyDetails?> GetSupplierDetailsAsync(string supplierId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching supplier details from CIDMAS. SupplierId: {SupplierId}", supplierId);

        foreach (var companyCode in _settings.ActiveCompanyCodes)
        {
            if (!_companySchemas.TryGetValue(companyCode, out var schema))
                continue;

            try
            {
                await using var connection = new OdbcConnection(_settings.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                // DB2 for i uses positional ? parameters — use DynamicParameters with p0, p1 keys
                var sql = BuildSupplierSql(schema);
                var parameters = new DynamicParameters();
                parameters.Add("p0", supplierId);
                var result = await connection.QueryFirstOrDefaultAsync<SupplierDto>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));

                if (result != null)
                {
                    var address = BuildAddress(result.Address1, result.Address2, result.Address3, result.PostalCode, result.CountryCode);

                    return new PartyDetails
                    {
                        PartyId = supplierId,
                        Name = result.Name?.Trim() ?? $"Supplier {supplierId}",
                        TIN = result.TIN?.Trim(),
                        BRN = result.BRN?.Trim(),
                        Address = address,
                        CountryCode = result.CountryCode?.Trim(),
                        IdScheme = "BRN"
                    };
                }
            }
            catch (OdbcException ex)
            {
                _logger.LogError(ex, "CIDMAS query failed for supplier {SupplierId} in company {CompanyCode}. SQLSTATE: {SqlState}",
                    supplierId, companyCode, ex.Errors[0]?.SQLState);
                throw;
            }
        }

        _logger.LogWarning("Supplier {SupplierId} not found in CIDMAS across any active company", supplierId);
        return null;
    }

    public async Task<PartyDetails?> GetCustomerDetailsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching customer details from OCUSMA. CustomerId: {CustomerId}", customerId);

        foreach (var companyCode in _settings.ActiveCompanyCodes)
        {
            if (!_companySchemas.TryGetValue(companyCode, out var schema))
                continue;

            try
            {
                await using var connection = new OdbcConnection(_settings.ConnectionString);
                await connection.OpenAsync(cancellationToken);

                var sql = BuildCustomerSql(schema);
                var parameters = new DynamicParameters();
                parameters.Add("p0", customerId);

                var result = await connection.QueryFirstOrDefaultAsync<CustomerDto>(
                    new CommandDefinition(sql, parameters, commandTimeout: _settings.CommandTimeoutSeconds, cancellationToken: cancellationToken));

                if (result != null)
                {
                    var address = BuildAddress(result.Address1, result.Address2, result.Address3, result.PostalCode, result.CountryCode);

                    return new PartyDetails
                    {
                        PartyId = customerId,
                        Name = result.Name?.Trim() ?? $"Customer {customerId}",
                        TIN = result.TIN?.Trim(),
                        BRN = result.BRN?.Trim(),
                        Address = address,
                        CountryCode = result.CountryCode?.Trim(),
                        IdScheme = "BRN"
                    };
                }
            }
            catch (OdbcException ex)
            {
                _logger.LogError(ex, "OCUSMA query failed for customer {CustomerId} in company {CompanyCode}. SQLSTATE: {SqlState}",
                    customerId, companyCode, ex.Errors[0]?.SQLState);
                throw;
            }
        }

        _logger.LogWarning("Customer {CustomerId} not found in OCUSMA across any active company", customerId);
        return null;
    }

    #region SQL Builders

    private string BuildSupplierSql(string schema)
    {
        // CIDMAS available columns (confirmed against MVXCDTA schema):
        // IDSUNM (name), IDCSCD (country), IDVRNO (VAT/BRN), IDCORG/IDCOR2 (org numbers)
        // NOTE: IDADR1-3 and IDPONO do NOT exist in this M3 installation.
        // Address fields are not available in CIDMAS — supplier address sourced elsewhere if needed.
        //
        // Supplier TIN: foreign import suppliers have no Malaysian TIN — default to EI00000000030.
        var tinSelect = string.IsNullOrWhiteSpace(_settings.SupplierTinColumn)
            ? $"'{_foreignDefaults.SupplierTIN}' AS TIN"
            : $"COALESCE(NULLIF(TRIM(s.{_settings.SupplierTinColumn}),''), '{_foreignDefaults.SupplierTIN}') AS TIN";

        // Supplier BRN: use IDVRNO (VAT reg no) if no column configured; fall back to 'NA'.
        var brnSelect = string.IsNullOrWhiteSpace(_settings.SupplierBrnColumn)
            ? $"COALESCE(NULLIF(TRIM(s.IDVRNO),''), '{_foreignDefaults.SupplierBRN}') AS BRN"
            : $"COALESCE(NULLIF(TRIM(s.{_settings.SupplierBrnColumn}),''), '{_foreignDefaults.SupplierBRN}') AS BRN";

        return $@"SELECT
            TRIM(s.IDSUNM) AS Name,
            {tinSelect},
            {brnSelect},
            CAST(NULL AS VARCHAR(100)) AS Address1,
            CAST(NULL AS VARCHAR(100)) AS Address2,
            CAST(NULL AS VARCHAR(100)) AS Address3,
            CAST(NULL AS VARCHAR(20)) AS PostalCode,
            TRIM(s.IDCSCD) AS CountryCode
        FROM {schema}.CIDMAS s
        WHERE TRIM(s.IDSUNO) = ?";
    }

    private string BuildCustomerSql(string schema)
    {
        // Standard OCUSMA columns: OKCUNO (ID), OKCUNM (name), OKCUA1-4 (address)
        //
        // Customer TIN: AU/overseas customers have no Malaysian TIN — default to EI00000000020.
        var tinSelect = string.IsNullOrWhiteSpace(_settings.CustomerTinColumn)
            ? $"'{_foreignDefaults.BuyerTIN}' AS TIN"
            : $"COALESCE(NULLIF(TRIM(c.{_settings.CustomerTinColumn}),''), '{_foreignDefaults.BuyerTIN}') AS TIN";

        // Customer BRN: OKVRNO holds ABN (AU), VAT reg no (overseas), or company reg (others).
        // Finance confirmed: single field used for all customer types.
        var brnSelect = string.IsNullOrWhiteSpace(_settings.CustomerBrnColumn)
            ? $"'{_foreignDefaults.BuyerBRN}' AS BRN"
            : $"COALESCE(NULLIF(TRIM(c.{_settings.CustomerBrnColumn}),''), '{_foreignDefaults.BuyerBRN}') AS BRN";

        return $@"SELECT
            TRIM(c.OKCUNM) AS Name,
            {tinSelect},
            {brnSelect},
            TRIM(c.OKCUA1) AS Address1,
            TRIM(c.OKCUA2) AS Address2,
            TRIM(c.OKCUA3) AS Address3,
            TRIM(c.OKPONO) AS PostalCode,
            TRIM(c.OKCSCD) AS CountryCode
        FROM {schema}.OCUSMA c
        WHERE TRIM(c.OKCUNO) = ?";
    }

    #endregion

    #region Helpers

    private static string? BuildAddress(string? line1, string? line2, string? line3, string? postalCode, string? countryCode)
    {
        var parts = new[] { line1, line2, line3 }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();

        if (parts.Count == 0)
            return null;

        var address = string.Join(", ", parts);

        if (!string.IsNullOrWhiteSpace(postalCode))
            address += $" {postalCode.Trim()}";

        if (!string.IsNullOrWhiteSpace(countryCode))
            address += $" {countryCode.Trim()}";

        return address;
    }

    #endregion

    #region Internal DTOs

    private class SupplierDto
    {
        public string? Name { get; set; }
        public string? TIN { get; set; }
        public string? BRN { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    private class CustomerDto
    {
        public string? Name { get; set; }
        public string? TIN { get; set; }
        public string? BRN { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }
        public string? PostalCode { get; set; }
        public string? CountryCode { get; set; }
    }

    #endregion
}
