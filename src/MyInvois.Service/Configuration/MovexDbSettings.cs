namespace MyInvois.Service.Configuration;

// Uses skill: architecture/configuration-management v1.0+

/// <summary>
/// MOVEX Database Configuration (DB2 on AS/400)
/// Maps to appsettings.json["MovexDb"]
/// Replaces MovexApiSettings.cs (ADR-013: REST API → DB2 Direct Access)
/// </summary>
public class MovexDbSettings
{
    /// <summary>
    /// DB2 connection string (stored in User Secrets per WORKSPACE_RULES.md)
    /// Format: "Server=hostname;Database=dbname;UserID=user;Password=pass;"
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Data source strategy: "DirectQuery" or "StoredProcedure"
    /// Controls which IInvoiceDataSource implementation is registered in DI
    /// </summary>
    public string DataSourceStrategy { get; set; } = "DirectQuery";

    /// <summary>
    /// Schema for company 100 (e.g., "mvxcdta")
    /// Used in SQL queries: {SchemaCmp100}.fpledg, {SchemaCmp100}.fsledg
    /// </summary>
    public string SchemaCmp100 { get; set; } = "mvxcdta";

    /// <summary>
    /// Schema for company 300 (e.g., "mvxc300")
    /// Used in SQL queries: {SchemaCmp300}.fpledg, {SchemaCmp300}.fsledg
    /// </summary>
    public string SchemaCmp300 { get; set; } = "mvxc300";

    /// <summary>
    /// Active company codes to process (e.g., ["100", "300"])
    /// </summary>
    public List<string> ActiveCompanyCodes { get; set; } = new() { "100", "300" };

    /// <summary>
    /// SQL command timeout in seconds
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum connection pool size for DB2 connections
    /// </summary>
    public int MaxPoolSize { get; set; } = 10;

    /// <summary>
    /// Party data source strategy: "Placeholder", "MovexMaster", or "CustomLookup"
    /// Controls which IPartyDataProvider implementation is registered in DI
    /// </summary>
    public string PartyDataSource { get; set; } = "Placeholder";

    /// <summary>
    /// Stored procedure name for AP invoices (used when DataSourceStrategy = "StoredProcedure")
    /// </summary>
    public string ApInvoiceStoredProc { get; set; } = string.Empty;

    /// <summary>
    /// Stored procedure name for AR invoices (used when DataSourceStrategy = "StoredProcedure")
    /// </summary>
    public string ArInvoiceStoredProc { get; set; } = string.Empty;

    /// <summary>
    /// AR division filter — FSLEDG.ESDIVI (e.g., "L")
    /// </summary>
    public string ArDivision { get; set; } = "L";

    /// <summary>
    /// AR transaction code filter — FSLEDG.ESTRCD (e.g., "10")
    /// </summary>
    public string ArTransCode { get; set; } = "10";

    /// <summary>
    /// AR customer status filter — OCUSMA.OKSTAT (e.g., "20" = active)
    /// </summary>
    public string ArCustomerStatus { get; set; } = "20";

    /// <summary>
    /// AR minimum year filter — FSLEDG.ESYEA4 > ArMinYear
    /// </summary>
    public int ArMinYear { get; set; } = int.Parse(DateTime.Now.AddYears(-1).ToString("yyyy"));

    /// <summary>
    /// CIDMAS column name for Supplier TIN (Tax Identification Number).
    /// Leave empty until Finance team confirms column mapping (e.g., "IDCFC1", "IDCORG").
    /// When empty, MovexMasterPartyDataProvider returns NULL for supplier TIN.
    /// </summary>
    public string SupplierTinColumn { get; set; } = string.Empty;

    /// <summary>
    /// CIDMAS column name for Supplier BRN (Business Registration Number).
    /// Leave empty until Finance team confirms column mapping.
    /// </summary>
    public string SupplierBrnColumn { get; set; } = string.Empty;

    /// <summary>
    /// OCUSMA column name for Customer TIN (Tax Identification Number).
    /// Leave empty until Finance team confirms column mapping (e.g., "OKCFC1", "OKCORG").
    /// When empty, MovexMasterPartyDataProvider returns NULL for customer TIN.
    /// </summary>
    public string CustomerTinColumn { get; set; } = string.Empty;

    /// <summary>
    /// OCUSMA column name for Customer BRN (Business Registration Number).
    /// Leave empty until Finance team confirms column mapping.
    /// </summary>
    public string CustomerBrnColumn { get; set; } = string.Empty;
}
