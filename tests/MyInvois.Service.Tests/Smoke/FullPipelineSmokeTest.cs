using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MyInvois.Service.Configuration;
using MyInvois.Service.DataAccess;
using MyInvois.Service.Services;
using MyInvois.Service.Validators;
using System.Net;
using System.Text.Json;
using Xunit.Abstractions;

namespace MyInvois.Service.Tests.Smoke;

/// <summary>
/// Full pipeline smoke test — uses REAL DB2, REAL party data provider, REAL validators.
/// Mocks: MyInvois HTTP API, AuditLogger (no SQL Server required).
///
/// Purpose: Demonstrate to stakeholders that real MOVEX invoices flow through the pipeline.
/// Shows validation results and identifies remaining gaps (TIN/BRN columns, XAdES signing).
///
/// Run: dotnet test --filter "Category=Smoke" --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Smoke")]
[Trait("Category", "RequiresDb2")]
public class FullPipelineSmokeTest
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public FullPipelineSmokeTest(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));

        // Load configuration from appsettings.json + User Secrets
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<FullPipelineSmokeTest>(optional: true)
            .Build();
    }

    [Fact(DisplayName = "Diagnostic: Verify MOVEX Schema Connection")]
    public async Task DiagnosticTest_ConfirmMvxcdtaSchema()
    {
        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        _output.WriteLine($"Configured Schema: {movexDbSettings.SchemaCmp100}");
        _output.WriteLine($"Active Companies: {string.Join(", ", movexDbSettings.ActiveCompanyCodes)}");
        _output.WriteLine($"Connection String: {(string.IsNullOrEmpty(movexDbSettings.ConnectionString) ? "NOT SET" : "SET (masked)")}");
        _output.WriteLine("");

        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());

        try
        {
            // Test with current month's date range
            var now = DateTime.UtcNow;
            var fromDate = new DateTime(now.Year, now.Month, 1);
            var toDate = fromDate.AddMonths(1).AddSeconds(-1);
            
            _output.WriteLine($"Querying invoices from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}...");

            var result = await dataSource.GetPendingInvoicesAsync(fromDate, CancellationToken.None);
            _output.WriteLine($"✅ Schema '{movexDbSettings.SchemaCmp100}' connected successfully");
            _output.WriteLine($"📊 Retrieved {result.Count} invoices");

            if (result.Count > 0)
            {
                _output.WriteLine($"   Sample invoice: {result.FirstOrDefault()?.InvoiceNo}");
            }

            result.Count.Should().BeGreaterThanOrEqualTo(0);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Failed to query {movexDbSettings.SchemaCmp100}");
            _output.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
                _output.WriteLine($"Inner Error: {ex.InnerException.Message}");
            throw;
        }
    }

    [Fact(DisplayName = "Diagnostic: AR line items from OINVOL")]
    public async Task DiagnosticTest_ArLineItems_Oinvol()
    {
        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        var schema = movexDbSettings.SchemaCmp100;

        // First get a sample AR invoice number from FSLEDG
        var arWhere = $"f.ESRGDT BETWEEN ? AND ? AND f.ESDIVI = ? AND f.ESTRCD = ? AND o.OKSTAT = ? AND f.ESYEA4 > ?";

        await using var connection = new System.Data.Odbc.OdbcConnection(movexDbSettings.ConnectionString);
        await connection.OpenAsync();
        _output.WriteLine("Connected to DB2");

        // Get a few AR invoice numbers
        using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = $"SELECT TRIM(f.ESCINO) FROM {schema}.FSLEDG f JOIN {schema}.OCUSMA o ON f.ESCONO=o.OKCONO AND f.ESCUNO=o.OKCUNO WHERE f.ESRGDT BETWEEN 20260101 AND 20260401 AND f.ESDIVI='L' AND f.ESTRCD=20 AND o.OKSTAT=20 AND f.ESYEA4>0 FETCH FIRST 5 ROWS ONLY";
        var arInvoiceNos = new List<string>();
        using (var r = await cmd1.ExecuteReaderAsync()) { while (await r.ReadAsync()) arInvoiceNos.Add(r.GetString(0)); }
        _output.WriteLine($"Sample AR invoice numbers from FSLEDG: {string.Join(", ", arInvoiceNos)}");

        if (arInvoiceNos.Count == 0) { _output.WriteLine("No AR invoices found — skip"); return; }

        // Try OINVOL direct match
        var testNo = arInvoiceNos[0];
        using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = $"SELECT TRIM(OIIVNO) FROM {schema}.OINVOL WHERE TRIM(OIIVNO) = ? FETCH FIRST 3 ROWS ONLY";
        cmd2.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", testNo));
        var oinvolMatches = new List<string>();
        try
        {
            using var r2 = await cmd2.ExecuteReaderAsync();
            while (await r2.ReadAsync()) oinvolMatches.Add(r2.GetString(0));
            _output.WriteLine($"OINVOL match for '{testNo}': {(oinvolMatches.Count > 0 ? string.Join(", ", oinvolMatches) : "NO MATCH")}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL query error: {ex.Message}"); }

        // Search for AR line item tables — look for tables with invoice number + item/qty/amount columns
        // AR invoice number from FSLEDG is like '009705157' — search tables with similar field
        using var cmd3 = connection.CreateCommand();
        cmd3.CommandText = $@"SELECT DISTINCT c.TABLE_NAME
            FROM QSYS2.SYSCOLUMNS c
            WHERE c.TABLE_SCHEMA='{schema.ToUpperInvariant()}'
              AND c.COLUMN_NAME LIKE '%IVNO%'
            ORDER BY c.TABLE_NAME
            FETCH FIRST 20 ROWS ONLY";
        try
        {
            using var r3 = await cmd3.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await r3.ReadAsync()) tables.Add(r3.GetString(0));
            _output.WriteLine($"Tables with IVNO column: {string.Join(", ", tables)}");
        }
        catch (Exception ex) { _output.WriteLine($"Schema search error: {ex.Message}"); }

        // Also check OINVOL actual column with ONIVNO - what do those values look like?
        using var cmd4 = connection.CreateCommand();
        cmd4.CommandText = $"SELECT DISTINCT ONIVNO FROM {schema}.OINVOL FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r4 = await cmd4.ExecuteReaderAsync();
            var vals = new List<string>();
            while (await r4.ReadAsync()) vals.Add(r4[0]?.ToString() ?? "null");
            _output.WriteLine($"Sample OINVOL.ONIVNO values: {string.Join(", ", vals)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL ONIVNO error: {ex.Message}"); }

        // Search tables starting with 'OI' (Order Invoicing) that may hold AR line items
        using var cmdOi = connection.CreateCommand();
        cmdOi.CommandText = $"SELECT TABLE_NAME FROM QSYS2.SYSTABLES WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME LIKE 'OI%' ORDER BY TABLE_NAME FETCH FIRST 30 ROWS ONLY";
        try
        {
            using var rOi = await cmdOi.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await rOi.ReadAsync()) tables.Add(rOi.GetString(0));
            _output.WriteLine($"Tables starting with OI: {string.Join(", ", tables)}");
        }
        catch (Exception ex) { _output.WriteLine($"OI table list error: {ex.Message}"); }

        // Find tables with a column that matches FSLEDG.ESCINO format (invoice number + qty/price)
        // Try OINVIP: UIIVNO is invoice number — check if it matches and has qty/price via joined tables
        // Also try directly: FSLEDG → OINVIP → OINVDA (order/item details)
        // First, check what UIIVNO values look like in OINVIP vs FSLEDG ESCINO
        using var cmdOINVIP = connection.CreateCommand();
        cmdOINVIP.CommandText = $"SELECT DISTINCT UIIVNO, UIORNO FROM {schema}.OINVIP WHERE UICONO=100 FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVIP.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]}");
            _output.WriteLine($"OINVIP samples: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVIP sample error: {ex.Message}"); }

        // Check OINVLL: HHIVNO values
        using var cmdOINVLL = connection.CreateCommand();
        cmdOINVLL.CommandText = $"SELECT DISTINCT HHIVNO, HHORNO FROM {schema}.OINVLL WHERE HHCONO=100 FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVLL.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]}");
            _output.WriteLine($"OINVLL samples: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVLL sample error: {ex.Message}"); }

        // Now check: does any AR invoice from FSLEDG match OINVIP.UIIVNO?
        if (arInvoiceNos.Count > 0)
        {
            using var cmdMatch = connection.CreateCommand();
            // FSLEDG.ESCINO is trimmed string like '009705157'
            // OINVIP.UIIVNO is DECIMAL — try converting
            cmdMatch.CommandText = $"SELECT COUNT(*) FROM {schema}.OINVIP WHERE UICONO=100 AND TRIM(CHAR(UIIVNO)) IN ('{string.Join("','", arInvoiceNos)}')";
            try
            {
                var cnt = (await cmdMatch.ExecuteScalarAsync())?.ToString();
                _output.WriteLine($"OINVIP matches for AR invoice numbers (string cast): {cnt}");
            }
            catch (Exception ex) { _output.WriteLine($"OINVIP match error: {ex.Message}"); }
        }

        // FSLEDG full columns — find internal invoice number link
        using var cmdFSLEDG = connection.CreateCommand();
        cmdFSLEDG.CommandText = $"SELECT COLUMN_NAME FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='FSLEDG' ORDER BY ORDINAL_POSITION FETCH FIRST 40 ROWS ONLY";
        try
        {
            using var r = await cmdFSLEDG.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await r.ReadAsync()) cols.Add(r.GetString(0));
            _output.WriteLine($"FSLEDG columns: {string.Join(", ", cols)}");
        }
        catch (Exception ex) { _output.WriteLine($"FSLEDG columns error: {ex.Message}"); }

        // Sample FSLEDG row for one of the AR invoice numbers — see all field values
        if (arInvoiceNos.Count > 0)
        {
            using var cmdFSRow = connection.CreateCommand();
            cmdFSRow.CommandText = $"SELECT ESBJNO, ESVONO, ESCINO, ESCUNO, ESRGDT, ESYEA4 FROM {schema}.FSLEDG WHERE TRIM(ESCINO)=? AND ESCONO=100 FETCH FIRST 1 ROW ONLY";
            cmdFSRow.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                using var r = await cmdFSRow.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    _output.WriteLine($"FSLEDG row: BJNO={r[0]} VONO={r[1]} CINO={r[2]} CUNO={r[3]} RGDT={r[4]} YEA4={r[5]}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG row error: {ex.Message}"); }
        }

        // OINVIP full columns
        using var cmdOINVIPcols = connection.CreateCommand();
        cmdOINVIPcols.CommandText = $"SELECT COLUMN_NAME FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='OINVIP' ORDER BY ORDINAL_POSITION FETCH FIRST 40 ROWS ONLY";
        try
        {
            using var r = await cmdOINVIPcols.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await r.ReadAsync()) cols.Add(r.GetString(0));
            _output.WriteLine($"OINVIP columns: {string.Join(", ", cols)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVIP columns error: {ex.Message}"); }

        // Check types of FSLEDG.ESJRNO and OINVIP.UIBJNO
        using var cmdTypes = connection.CreateCommand();
        cmdTypes.CommandText = $@"SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION
            FROM QSYS2.SYSCOLUMNS
            WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}'
              AND ((TABLE_NAME='FSLEDG' AND COLUMN_NAME IN ('ESJRNO','ESVONO','ESJSNO'))
                OR (TABLE_NAME='OINVIP' AND COLUMN_NAME IN ('UIBJNO','UIIVNO','UIORNO')))
            ORDER BY TABLE_NAME, COLUMN_NAME";
        try
        {
            using var r = await cmdTypes.ExecuteReaderAsync();
            while (await r.ReadAsync())
                _output.WriteLine($"  {r[0]}.{r[1]}: {r[2]}({r[3]}/{r[4]})");
        }
        catch (Exception ex) { _output.WriteLine($"Type check error: {ex.Message}"); }

        // Get actual values for FSLEDG fields
        if (arInvoiceNos.Count > 0)
        {
            using var cmdFSRow2 = connection.CreateCommand();
            cmdFSRow2.CommandText = $"SELECT ESJRNO, ESVONO, ESJSNO, ESTRCD, ESCINO FROM {schema}.FSLEDG WHERE TRIM(ESCINO)=? AND ESCONO=100 FETCH FIRST 1 ROW ONLY";
            cmdFSRow2.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                using var r = await cmdFSRow2.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    _output.WriteLine($"FSLEDG sample: JRNO={r[0]} VONO={r[1]} JSNO={r[2]} TRCD={r[3]} CINO={r[4]}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG sample error: {ex.Message}"); }

            // Try FSLEDG→OINVIP via CHAR(ESJRNO) = UIBJNO
            using var cmdLink2 = connection.CreateCommand();
            cmdLink2.CommandText = $@"SELECT f.ESJRNO, i.UIBJNO, i.UIIVNO, i.UIORNO
                FROM {schema}.FSLEDG f
                LEFT JOIN {schema}.OINVIP i ON f.ESCONO=i.UICONO AND TRIM(CHAR(f.ESJRNO))=TRIM(i.UIBJNO)
                WHERE TRIM(f.ESCINO)=? AND f.ESCONO=100
                FETCH FIRST 3 ROWS ONLY";
            cmdLink2.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                using var r = await cmdLink2.ExecuteReaderAsync();
                var rows = new List<string>();
                while (await r.ReadAsync()) rows.Add($"JRNO={r[0]} BJNO={r[1]} IVNO={r[2]} ORNO={r[3]}");
                _output.WriteLine($"FSLEDG→OINVIP (char cast): {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG→OINVIP cast join error: {ex.Message}"); }
        }

        // Explore FGLEDG — link between AR accounting and OI order invoicing
        // FGLEDG is the order invoicing GL bridge; EGJRNO may be the BJNO that links to OINVIP
        using var cmdFGLEDG = connection.CreateCommand();
        cmdFGLEDG.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='FGLEDG' ORDER BY ORDINAL_POSITION FETCH FIRST 40 ROWS ONLY";
        try
        {
            using var r = await cmdFGLEDG.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await r.ReadAsync()) cols.Add($"{r[0]}({r[1]})");
            _output.WriteLine($"FGLEDG columns: {string.Join(", ", cols)}");
        }
        catch (Exception ex) { _output.WriteLine($"FGLEDG columns error: {ex.Message}"); }

        // Try: FSLEDG.ESVONO → FGLEDG.EGVONO → FGLEDG.EGJRNO = OINVIP.UIBJNO
        if (arInvoiceNos.Count > 0)
        {
            using var cmdFGLink = connection.CreateCommand();
            cmdFGLink.CommandText = $@"SELECT g.EGJRNO, g.EGVONO, g.EGFEID, g.EGFNCN, i.UIIVNO, i.UIORNO
                FROM {schema}.FSLEDG f
                JOIN {schema}.FGLEDG g ON f.ESCONO=g.EGCONO AND f.ESVONO=g.EGVONO
                LEFT JOIN {schema}.OINVIP i ON g.EGCONO=i.UICONO AND TRIM(CHAR(g.EGJRNO))=TRIM(i.UIBJNO)
                WHERE TRIM(f.ESCINO)=? AND f.ESCONO=100
                FETCH FIRST 5 ROWS ONLY";
            cmdFGLink.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                using var r = await cmdFGLink.ExecuteReaderAsync();
                var rows = new List<string>();
                while (await r.ReadAsync()) rows.Add($"JRNO={r[0]} VONO={r[1]} FEID={r[2]} FNCN={r[3]} IVNO={r[4]} ORNO={r[5]}");
                _output.WriteLine($"FSLEDG→FGLEDG→OINVIP: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
            }
            catch (Exception ex) { _output.WriteLine($"FGLEDG link error: {ex.Message}"); }
        }

        // Sample OINVIP.UIBJNO values to understand the format
        using var cmdUIBJNO = connection.CreateCommand();
        cmdUIBJNO.CommandText = $"SELECT UIBJNO, UIIVNO, UIORNO FROM {schema}.OINVIP WHERE UICONO=100 FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdUIBJNO.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"BJNO='{r[0]}' IVNO={r[1]} ORNO={r[2]}");
            _output.WriteLine($"OINVIP.UIBJNO format: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"UIBJNO sample error: {ex.Message}"); }

        // Try matching FGLEDG.EGJRNO=11121 to OINVIP directly
        using var cmdDirect = connection.CreateCommand();
        cmdDirect.CommandText = $"SELECT UIBJNO, UIIVNO, UIORNO FROM {schema}.OINVIP WHERE UICONO=100 AND UIBJNO='11121' FETCH FIRST 3 ROWS ONLY";
        try
        {
            using var r = await cmdDirect.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"BJNO='{r[0]}' IVNO={r[1]} ORNO={r[2]}");
            _output.WriteLine($"OINVIP match BJNO='11121': {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVIP direct match error: {ex.Message}"); }

        // ACUIVR — Invoice lines references — has IRIVNO + IRITNO (item) + IRIVAM
        // Check if it exists and what IRIVNO looks like
        using var cmdACUIVR = connection.CreateCommand();
        cmdACUIVR.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='ACUIVR' ORDER BY ORDINAL_POSITION FETCH FIRST 30 ROWS ONLY";
        try
        {
            using var r = await cmdACUIVR.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await r.ReadAsync()) cols.Add($"{r[0]}({r[1]})");
            _output.WriteLine($"ACUIVR columns: {string.Join(", ", cols)}");
        }
        catch (Exception ex) { _output.WriteLine($"ACUIVR not found: {ex.Message}"); }

        // Sample ACUIVR with invoice numbers
        using var cmdACUIVRsample = connection.CreateCommand();
        cmdACUIVRsample.CommandText = $"SELECT IRIVNO, IRITNO, IRIVAM, IRAIVR, IRAIVT FROM {schema}.ACUIVR WHERE IRCONO=100 FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdACUIVRsample.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ITNO='{r[1]}' AM={r[2]} REF={r[3]} TYPE={r[4]}");
            _output.WriteLine($"ACUIVR sample: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
        }
        catch (Exception ex) { _output.WriteLine($"ACUIVR sample error: {ex.Message}"); }

        // Does ACUIVR.IRIVNO match FSLEDG.ESCINO? FSLEDG ESCINO = '009705157' (string)
        // ACUIVR IRIVNO is likely DECIMAL
        if (arInvoiceNos.Count > 0)
        {
            using var cmdACUIVRmatch = connection.CreateCommand();
            // Try numeric match: FSLEDG.ESCINO='009705157' → IRIVNO=9705157
            cmdACUIVRmatch.CommandText = $"SELECT COUNT(*) FROM {schema}.ACUIVR WHERE IRCONO=100 AND IRIVNO=?";
            // Strip leading zeros for decimal match
            if (decimal.TryParse(arInvoiceNos[0], out var ivno))
                cmdACUIVRmatch.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", ivno));
            else
                cmdACUIVRmatch.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                var cnt = (await cmdACUIVRmatch.ExecuteScalarAsync())?.ToString();
                _output.WriteLine($"ACUIVR match for IRIVNO={arInvoiceNos[0]} (as decimal): {cnt}");
            }
            catch (Exception ex) { _output.WriteLine($"ACUIVR match error: {ex.Message}"); }
        }

        // Final check: link FSLEDG → OINVOL via PYNO + INYR, then to OOLINE
        // FSLEDG.ESPYNO = OINVOL.ONPYNO, FSLEDG.ESINYR = OINVOL.ONYEA4
        if (arInvoiceNos.Count > 0)
        {
            using var cmdPyno = connection.CreateCommand();
            cmdPyno.CommandText = $"SELECT ESPYNO, ESINYR, ESCUAM, ESVONO FROM {schema}.FSLEDG WHERE TRIM(ESCINO)=? AND ESCONO=100 FETCH FIRST 1 ROW ONLY";
            cmdPyno.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            string? pyno = null; decimal inyr = 0; decimal vono = 0;
            try
            {
                using var r = await cmdPyno.ExecuteReaderAsync();
                if (await r.ReadAsync()) { pyno = r[0]?.ToString()?.Trim(); inyr = r.GetDecimal(1); vono = r.GetDecimal(3); }
                _output.WriteLine($"FSLEDG: PYNO={pyno} INYR={inyr} VONO={vono}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG PYNO error: {ex.Message}"); }

            if (pyno != null)
            {
                // Does OINVOL have rows for this payer+year?
                using var cmdOinvolPyno = connection.CreateCommand();
                cmdOinvolPyno.CommandText = $"SELECT ONIVNO, ONORNO, ONDLIX, ONIVAM FROM {schema}.OINVOL WHERE ONCONO=100 AND TRIM(ONPYNO)=? AND ONYEA4=? FETCH FIRST 5 ROWS ONLY";
                cmdOinvolPyno.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", pyno));
                cmdOinvolPyno.Parameters.Add(new System.Data.Odbc.OdbcParameter("p1", inyr));
                try
                {
                    using var r = await cmdOinvolPyno.ExecuteReaderAsync();
                    var rows = new List<string>();
                    while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]} DLIX={r[2]} AM={r[3]}");
                    _output.WriteLine($"OINVOL by PYNO={pyno} INYR={inyr}: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
                }
                catch (Exception ex) { _output.WriteLine($"OINVOL pyno query error: {ex.Message}"); }
            }
        }

        // Check OINVOL year distribution
        using var cmdOINVOLyear = connection.CreateCommand();
        cmdOINVOLyear.CommandText = $"SELECT ONYEA4, COUNT(*) AS CNT FROM {schema}.OINVOL WHERE ONCONO=100 GROUP BY ONYEA4 ORDER BY ONYEA4 DESC FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVOLyear.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"{r[0]}:{r[1]}");
            _output.WriteLine($"OINVOL year distribution: {string.Join(", ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL year error: {ex.Message}"); }

        // Check OINVOL for data connected to a recent FSLEDG customer
        using var cmdOINVOLrecent = connection.CreateCommand();
        cmdOINVOLrecent.CommandText = $"SELECT ONIVNO, ONPYNO, ONYEA4, ONORNO FROM {schema}.OINVOL WHERE ONCONO=100 ORDER BY ONRGDT DESC FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVOLrecent.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} PYNO={r[1]} YR={r[2]} ORNO={r[3]}");
            _output.WriteLine($"OINVOL most recent rows: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL recent error: {ex.Message}"); }

        // Key question: do FSLEDG payers match OINVOL payers?
        using var cmdPynoCheck = connection.CreateCommand();
        cmdPynoCheck.CommandText = $@"SELECT DISTINCT TRIM(f.ESPYNO) AS FPYNO
            FROM {schema}.FSLEDG f
            WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20260401 AND f.ESTRCD=20
            FETCH FIRST 10 ROWS ONLY";
        try
        {
            using var r = await cmdPynoCheck.ExecuteReaderAsync();
            var pynos = new List<string>();
            while (await r.ReadAsync()) pynos.Add(r.GetString(0));
            _output.WriteLine($"FSLEDG 2026 PYNO values: {string.Join(", ", pynos)}");

            // Check which of these appear in OINVOL
            if (pynos.Count > 0)
            {
                var inClause = string.Join(",", pynos.Select(p => $"'{p}'"));
                using var cmdOINVOLpyno = connection.CreateCommand();
                cmdOINVOLpyno.CommandText = $"SELECT DISTINCT TRIM(ONPYNO) FROM {schema}.OINVOL WHERE ONCONO=100 AND ONYEA4=2026 AND TRIM(ONPYNO) IN ({inClause}) FETCH FIRST 10 ROWS ONLY";
                using var r2 = await cmdOINVOLpyno.ExecuteReaderAsync();
                var matches = new List<string>();
                while (await r2.ReadAsync()) matches.Add(r2.GetString(0));
                _output.WriteLine($"FSLEDG PYNOs that exist in OINVOL 2026: {(matches.Count > 0 ? string.Join(", ", matches) : "NONE")}");
            }
        }
        catch (Exception ex) { _output.WriteLine($"PYNO check error: {ex.Message}"); }

        // Confirm join: FSLEDG.ESPYNO + ESCUAM = OINVOL.ONPYNO + ONIVAM
        // (invoice amount should uniquely identify the match for a given payer+year)
        if (arInvoiceNos.Count > 0)
        {
            using var cmdAmtMatch = connection.CreateCommand();
            cmdAmtMatch.CommandText = $@"SELECT o.ONIVNO, o.ONORNO, o.ONDLIX, o.ONIVAM, o.ONYEA4
                FROM {schema}.FSLEDG f
                JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESPYNO)=TRIM(o.ONPYNO) AND f.ESCUAM=o.ONIVAM
                WHERE TRIM(f.ESCINO)=? AND f.ESCONO=100
                FETCH FIRST 5 ROWS ONLY";
            cmdAmtMatch.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            try
            {
                using var r = await cmdAmtMatch.ExecuteReaderAsync();
                var rows = new List<string>();
                while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]} DLIX={r[2]} AM={r[3]} YR={r[4]}");
                _output.WriteLine($"FSLEDG→OINVOL by PYNO+amount: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG→OINVOL amt match error: {ex.Message}"); }
        }

        // Compare amounts: FSLEDG.ESCUAM vs SUM(OINVOL.ONIVAM) by payer
        if (arInvoiceNos.Count > 0)
        {
            // Get FSLEDG amount and payer for our invoice
            using var cmdGetAmt = connection.CreateCommand();
            cmdGetAmt.CommandText = $"SELECT ESPYNO, ESCUAM, ESIVDT, ESINYR FROM {schema}.FSLEDG WHERE TRIM(ESCINO)=? AND ESCONO=100 FETCH FIRST 1 ROW ONLY";
            cmdGetAmt.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", arInvoiceNos[0]));
            string? pyno2 = null; decimal fsAmt = 0; decimal fsDate = 0; decimal fsInyr = 0;
            try
            {
                using var r = await cmdGetAmt.ExecuteReaderAsync();
                if (await r.ReadAsync()) { pyno2 = r[0]?.ToString()?.Trim(); fsAmt = r.GetDecimal(1); fsDate = r.GetDecimal(2); fsInyr = r.GetDecimal(3); }
                _output.WriteLine($"FSLEDG amount: PYNO={pyno2} AMT={fsAmt} DATE={fsDate} INYR={fsInyr}");
            }
            catch (Exception ex) { _output.WriteLine($"FSLEDG amt error: {ex.Message}"); }

            if (pyno2 != null)
            {
                // Check what OINVOL amounts look like for this payer
                using var cmdOINVOLamts = connection.CreateCommand();
                cmdOINVOLamts.CommandText = $"SELECT ONIVNO, ONIVAM, ONIVLA, ONIVAV, ONYEA4, ONRGDT FROM {schema}.OINVOL WHERE ONCONO=100 AND TRIM(ONPYNO)=? ORDER BY ONRGDT DESC FETCH FIRST 5 ROWS ONLY";
                cmdOINVOLamts.Parameters.Add(new System.Data.Odbc.OdbcParameter("p0", pyno2));
                try
                {
                    using var r = await cmdOINVOLamts.ExecuteReaderAsync();
                    var rows = new List<string>();
                    while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} IVAM={r[1]} IVLA={r[2]} IVAV={r[3]} YR={r[4]} DT={r[5]}");
                    _output.WriteLine($"OINVOL amounts for PYNO={pyno2}: {string.Join(" | ", rows)}");
                }
                catch (Exception ex) { _output.WriteLine($"OINVOL amounts error: {ex.Message}"); }
            }
        }

        // What % of FSLEDG AR invoices have corresponding OINVOL entries?
        using var cmdCoverage = connection.CreateCommand();
        cmdCoverage.CommandText = $@"SELECT
            COUNT(DISTINCT f.ESCINO) AS TOTAL_FSLEDG,
            COUNT(DISTINCT CASE WHEN o.ONCONO IS NOT NULL THEN f.ESCINO END) AS WITH_OINVOL
        FROM {schema}.FSLEDG f
        LEFT JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESPYNO)=TRIM(o.ONPYNO)
        WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20260401 AND f.ESTRCD=20";
        try
        {
            using var r = await cmdCoverage.ExecuteReaderAsync();
            if (await r.ReadAsync())
                _output.WriteLine($"FSLEDG→OINVOL coverage 2026: {r[0]} total FSLEDG invoices, {r[1]} have OINVOL rows (by payer match)");
        }
        catch (Exception ex) { _output.WriteLine($"Coverage check error: {ex.Message}"); }

        // Check payer 0383 year distribution in OINVOL
        using var cmdPyno0383 = connection.CreateCommand();
        cmdPyno0383.CommandText = $"SELECT ONYEA4, COUNT(*) FROM {schema}.OINVOL WHERE ONCONO=100 AND TRIM(ONPYNO)='0383' GROUP BY ONYEA4 ORDER BY ONYEA4 DESC FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdPyno0383.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"{r[0]}:{r[1]}");
            _output.WriteLine($"OINVOL PYNO=0383 year dist: {(rows.Count > 0 ? string.Join(", ", rows) : "no rows at all")}");
        }
        catch (Exception ex) { _output.WriteLine($"PYNO 0383 error: {ex.Message}"); }

        // Get a FSLEDG invoice that DOES have OINVOL coverage (from 679)
        using var cmdWithOINVOL = connection.CreateCommand();
        cmdWithOINVOL.CommandText = $@"SELECT TRIM(f.ESCINO), TRIM(f.ESPYNO), f.ESCUAM, o.ONIVNO, o.ONORNO
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESPYNO)=TRIM(o.ONPYNO)
            WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20260401 AND f.ESTRCD=20
            FETCH FIRST 3 ROWS ONLY";
        try
        {
            using var r = await cmdWithOINVOL.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"CINO={r[0]} PYNO={r[1]} FSAMT={r[2]} ONIVNO={r[3]} ORNO={r[4]}");
            _output.WriteLine($"FSLEDG with OINVOL match (2026): {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"With OINVOL error: {ex.Message}"); }

        // Validate the full AR line query path: FSLEDG.ESCINO → OINVOL (ESPYNO) → OOLINE → MITMAS
        using var cmdFullPath = connection.CreateCommand();
        // Use ODLINE (delivery lines) for exact delivery-specific items
        // OINVOL.ONORNO+ONDLIX → ODLINE.UBORNO+UBDLIX
        cmdFullPath.CommandText = $@"SELECT
            TRIM(f.ESCINO) AS InvoiceNo,
            ROW_NUMBER() OVER (PARTITION BY f.ESCINO ORDER BY dl.UBPONR, dl.UBPOSX) AS LineNumber,
            TRIM(dl.UBITNO) AS ItemNumber,
            TRIM(COALESCE(ol.OBITDS, dl.UBITNO)) AS Description,
            COALESCE(TRIM(im.MMITCL), '000') AS ClassificationCode,
            COALESCE(dl.UBDLQA, dl.UBIVQA, 0) AS Quantity,
            COALESCE(TRIM(ol.OBSPUN), 'EA') AS UnitOfMeasure,
            dl.UBSAPR AS UnitPrice,
            dl.UBLNAM AS LineTotal,
            COALESCE(TRIM(ol.OBVTCD), '') AS TaxCode,
            0 AS TaxAmount
        FROM {schema}.FSLEDG f
        JOIN {schema}.OINVOL ov ON f.ESCONO=ov.ONCONO AND TRIM(f.ESPYNO)=TRIM(ov.ONPYNO)
        JOIN {schema}.ODLINE dl ON ov.ONCONO=dl.UBCONO AND TRIM(ov.ONORNO)=TRIM(dl.UBORNO) AND ov.ONDLIX=dl.UBDLIX
        LEFT JOIN {schema}.OOLINE ol ON dl.UBCONO=ol.OBCONO AND TRIM(dl.UBORNO)=TRIM(ol.OBORNO) AND dl.UBPONR=ol.OBPONR AND dl.UBPOSX=ol.OBPOSX
        LEFT JOIN {schema}.MITMAS im ON dl.UBCONO=im.MMCONO AND TRIM(dl.UBITNO)=TRIM(im.MMITNO)
        WHERE f.ESCONO=100 AND TRIM(f.ESCINO) IN ('003662262')
        ORDER BY f.ESCINO, dl.UBPONR, dl.UBPOSX
        FETCH FIRST 10 ROWS ONLY";
        try
        {
            using var r = await cmdFullPath.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"CINO={r[0]} LINE={r[1]} ITNO={r[2]} QTY={r[5]} PRICE={r[7]} AMT={r[8]}");
            _output.WriteLine($"Full AR line path test: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
        }
        catch (Exception ex) { _output.WriteLine($"Full AR line path error: {ex.Message}"); }

        // Check OINVOL rows for invoice 003662262 — understand ONIVSQ/ONIVTP
        using var cmdOINVOLRows = connection.CreateCommand();
        cmdOINVOLRows.CommandText = $@"SELECT o.ONIVNO, o.ONYEA4, o.ONPYNO, o.ONIVSQ, o.ONIVTP, o.ONORNO, o.ONDLIX, o.ONIVAM
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESPYNO)=TRIM(o.ONPYNO)
            WHERE f.ESCONO=100 AND TRIM(f.ESCINO)='003662262'
            FETCH FIRST 10 ROWS ONLY";
        try
        {
            using var r = await cmdOINVOLRows.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} YR={r[1]} ONIVSQ={r[3]} ONIVTP={r[4]} ORNO={r[5]} DLIX={r[6]} AM={r[7]}");
            _output.WriteLine($"OINVOL rows for 003662262: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL rows error: {ex.Message}"); }

        // Check ODLINE rows for order 0007009826
        using var cmdODLINERows = connection.CreateCommand();
        cmdODLINERows.CommandText = $"SELECT UBORNO, UBDLIX, UBPONR, UBPOSX, UBITNO, UBSAPR, UBLNAM, UBDLQA FROM {schema}.ODLINE WHERE UBCONO=100 AND TRIM(UBORNO)='0007009826' FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdODLINERows.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"ORNO={r[0]} DLIX={r[1]} PONR={r[2]} POSX={r[3]} ITNO={r[4]} SAPR={r[5]} LNAM={r[6]} DLQA={r[7]}");
            _output.WriteLine($"ODLINE for order 0007009826: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"ODLINE rows error: {ex.Message}"); }

        // Get ALL fields of FSLEDG for invoice 003662262 to find the OINVOL link
        using var cmdFSfull = connection.CreateCommand();
        cmdFSfull.CommandText = $"SELECT ESJRNO, ESJSNO, ESVSER, ESVONO, ESPYNO, ESCINO, ESINYR FROM {schema}.FSLEDG WHERE TRIM(ESCINO)='003662262' AND ESCONO=100 FETCH FIRST 1 ROW ONLY";
        try
        {
            using var r = await cmdFSfull.ExecuteReaderAsync();
            if (await r.ReadAsync())
                _output.WriteLine($"FSLEDG 003662262: JRNO={r[0]} JSNO={r[1]} VSER={r[2]} VONO={r[3]} PYNO={r[4]} CINO={r[5]} INYR={r[6]}");
        }
        catch (Exception ex) { _output.WriteLine($"FSLEDG full row error: {ex.Message}"); }

        // Does FSLEDG.ESVONO link to OINVOL somehow?
        // Check OINVOL where ONIVNO matches ESVONO
        using var cmdVonoMatch = connection.CreateCommand();
        cmdVonoMatch.CommandText = $@"SELECT o.ONIVNO, o.ONORNO, o.ONDLIX
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND f.ESVONO=o.ONIVNO
            WHERE TRIM(f.ESCINO)='003662262' AND f.ESCONO=100
            FETCH FIRST 3 ROWS ONLY";
        try
        {
            using var r = await cmdVonoMatch.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"ONIVNO={r[0]} ORNO={r[1]} DLIX={r[2]}");
            _output.WriteLine($"FSLEDG.ESVONO=OINVOL.ONIVNO for 003662262: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"VONO→ONIVNO match error: {ex.Message}"); }

        // Try FSLEDG.ESJRNO = OINVOL.ONIVNO
        using var cmdJrnoMatch = connection.CreateCommand();
        cmdJrnoMatch.CommandText = $@"SELECT DISTINCT o.ONIVNO, o.ONORNO, o.ONDLIX
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND f.ESJRNO=o.ONIVNO
            WHERE TRIM(f.ESCINO)='003662262' AND f.ESCONO=100
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdJrnoMatch.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"ONIVNO={r[0]} ORNO={r[1]} DLIX={r[2]}");
            _output.WriteLine($"FSLEDG.ESJRNO=OINVOL.ONIVNO: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"JRNO→ONIVNO error: {ex.Message}"); }

        // ACUINV — Order invoice transactions — has ITIVNO + ITORNO + ITITNO
        // Check if ITIVNO = OINVOL.ONIVNO (internal) or FSLEDG.ESCINO (AR invoice number)
        using var cmdACUINV = connection.CreateCommand();
        cmdACUINV.CommandText = $"SELECT ITIVNO, ITORNO, ITITNO, ITTRQT, ITASP1 FROM {schema}.ACUINV WHERE ITCONO=100 FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdACUINV.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]} ITNO={r[2]} QT={r[3]} SAPR={r[4]}");
            _output.WriteLine($"ACUINV sample: {(rows.Count > 0 ? string.Join(" | ", rows) : "no rows")}");
        }
        catch (Exception ex) { _output.WriteLine($"ACUINV error: {ex.Message}"); }

        // Try ACUINV.ITIVNO = OINVOL.ONIVNO (both are DECIMAL invoice numbers)
        using var cmdACUINVmatch = connection.CreateCommand();
        cmdACUINVmatch.CommandText = $@"SELECT DISTINCT ai.ITIVNO, ai.ITORNO, ai.ITITNO
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL ov ON f.ESCONO=ov.ONCONO AND TRIM(f.ESPYNO)=TRIM(ov.ONPYNO)
            JOIN {schema}.ACUINV ai ON ov.ONCONO=ai.ITCONO AND ov.ONIVNO=ai.ITIVNO
            WHERE TRIM(f.ESCINO)='003662262' AND f.ESCONO=100
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdACUINVmatch.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} ORNO={r[1]} ITNO={r[2]}");
            _output.WriteLine($"OINVOL.ONIVNO→ACUINV: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"ACUINV join error: {ex.Message}"); }

        // Understand OINVOL.ONIVTP (information type) — find unique ORNO+DLIX combos
        using var cmdONIVTP = connection.CreateCommand();
        cmdONIVTP.CommandText = $@"SELECT DISTINCT ONIVNO, ONIVTP, ONORNO, ONDLIX, ONIVAM, COUNT(*) OVER (PARTITION BY ONIVNO) AS ROWS_PER_IVNO
            FROM {schema}.OINVOL
            WHERE ONCONO=100 AND ONIVNO=1515836
            ORDER BY ONIVTP, ONDLIX";
        try
        {
            using var r = await cmdONIVTP.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} IVTP={r[1]} ORNO={r[2]} DLIX={r[3]} AM={r[4]} ROWS={r[5]}");
            _output.WriteLine($"OINVOL IVTP breakdown for IVNO=1515836: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOL IVTP error: {ex.Message}"); }

        // Check OINVLL: HHIVNO values
        foreach (var tbl in new[] { "OINVLL", "OINVIP", "OINVDA" })
        {
            using var cmdTbl = connection.CreateCommand();
            cmdTbl.CommandText = $"SELECT COLUMN_NAME FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='{tbl}' ORDER BY ORDINAL_POSITION FETCH FIRST 10 ROWS ONLY";
            try
            {
                using var rTbl = await cmdTbl.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await rTbl.ReadAsync()) cols.Add(rTbl.GetString(0));
                if (cols.Count > 0) _output.WriteLine($"{tbl} columns: {string.Join(", ", cols)}");
                else _output.WriteLine($"{tbl}: not found");
            }
            catch { _output.WriteLine($"{tbl}: not found"); }
        }

        // Check ACUILW columns — ACU Invoice Line Work candidate
        using var cmd5b = connection.CreateCommand();
        cmd5b.CommandText = $"SELECT COLUMN_NAME FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND TABLE_NAME='ACUILW' ORDER BY ORDINAL_POSITION FETCH FIRST 30 ROWS ONLY";
        try
        {
            using var r5b = await cmd5b.ExecuteReaderAsync();
            var cols = new List<string>();
            while (await r5b.ReadAsync()) cols.Add(r5b.GetString(0));
            _output.WriteLine($"ACUILW columns: {string.Join(", ", cols)}");
        }
        catch (Exception ex) { _output.WriteLine($"ACUILW describe error: {ex.Message}"); }

        // Search for tables containing both invoice number AND item number — candidate line tables
        using var cmd5 = connection.CreateCommand();
        cmd5.CommandText = $@"SELECT TABLE_NAME, COUNT(*) AS COL_COUNT
            FROM QSYS2.SYSCOLUMNS
            WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}'
              AND TABLE_NAME IN (
                SELECT TABLE_NAME FROM QSYS2.SYSCOLUMNS WHERE TABLE_SCHEMA='{schema.ToUpperInvariant()}' AND COLUMN_NAME LIKE '%IVNO%'
              )
              AND COLUMN_NAME LIKE '%ITNO%'
            GROUP BY TABLE_NAME
            FETCH FIRST 10 ROWS ONLY";
        try
        {
            using var r5 = await cmd5.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await r5.ReadAsync()) tables.Add(r5.GetString(0));
            _output.WriteLine($"Tables with both IVNO and ITNO (candidate AR line tables): {string.Join(", ", tables)}");
        }
        catch (Exception ex) { _output.WriteLine($"AR line table search error: {ex.Message}"); }

        // --- MMITCL diagnostic ---
        using var cmdMMITCL = connection.CreateCommand();
        cmdMMITCL.CommandText = $@"SELECT TRIM(MMITNO),
            CASE WHEN MMITCL IS NULL OR TRIM(MMITCL) = '' THEN '000' ELSE TRIM(MMITCL) END AS CLS,
            LENGTH(CASE WHEN MMITCL IS NULL OR TRIM(MMITCL) = '' THEN '000' ELSE TRIM(MMITCL) END) AS CLS_LEN
            FROM {schema}.MITMAS WHERE MMCONO=100 AND TRIM(MMITNO)='84585' FETCH FIRST 1 ROW ONLY";
        try
        {
            using var r = await cmdMMITCL.ExecuteReaderAsync();
            if (await r.ReadAsync()) _output.WriteLine($"MMITCL for 84585: ITNO={r[0]} CLS='{r[1]}' LEN={r[2]}");
            else _output.WriteLine("MMITCL: item 84585 not found in MITMAS");
        }
        catch (Exception ex) { _output.WriteLine($"MMITCL error: {ex.Message}"); }

        // --- ONIVRF exploration ---
        // OINVOL.ONIVRF = "Invoice reference" — check if it matches FSLEDG.ESCINO
        using var cmdONIVRF = connection.CreateCommand();
        cmdONIVRF.CommandText = $@"SELECT ONIVNO, ONIVRF, ONPYNO, ONIVTP, ONORNO, ONDLIX
            FROM {schema}.OINVOL
            WHERE ONCONO=100 AND ONIVNO=1515836
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdONIVRF.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} IVRF='{r[1]}' PYNO={r[2]} IVTP={r[3]} ORNO={r[4]} DLIX={r[5]}");
            _output.WriteLine($"OINVOL.ONIVRF for IVNO=1515836: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"ONIVRF error: {ex.Message}"); }

        // Does ONIVRF match FSLEDG.ESCINO?
        using var cmdONIVRFmatch = connection.CreateCommand();
        cmdONIVRFmatch.CommandText = $@"SELECT DISTINCT o.ONIVNO, TRIM(o.ONIVRF) AS IVRF, o.ONIVTP, o.ONORNO, o.ONDLIX
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESCINO)=TRIM(o.ONIVRF)
            WHERE f.ESCONO=100 AND TRIM(f.ESCINO)='003662262'
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdONIVRFmatch.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} IVRF='{r[1]}' IVTP={r[2]} ORNO={r[3]} DLIX={r[4]}");
            _output.WriteLine($"FSLEDG.ESCINO=OINVOL.ONIVRF for 003662262: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"ONIVRF match error: {ex.Message}"); }

        // Count how many 2026 FSLEDG invoices match OINVOL via ESCINO=ONIVRF
        using var cmdONIVRFcoverage = connection.CreateCommand();
        cmdONIVRFcoverage.CommandText = $@"SELECT COUNT(DISTINCT f.ESCINO) AS MATCHED, COUNT(DISTINCT f2.ESCINO) AS TOTAL
            FROM (SELECT DISTINCT ESCONO, ESCINO FROM {schema}.FSLEDG WHERE ESCONO=100 AND ESRGDT BETWEEN 20260101 AND 20261231 AND ESDIVI='L' AND ESTRCD=20) f2
            LEFT JOIN (
                SELECT DISTINCT f.ESCINO
                FROM {schema}.FSLEDG f
                JOIN {schema}.OINVOL o ON f.ESCONO=o.ONCONO AND TRIM(f.ESCINO)=TRIM(o.ONIVRF)
                WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20261231 AND f.ESDIVI='L' AND f.ESTRCD=20
            ) f ON f.ESCINO = f2.ESCINO";
        try
        {
            using var r = await cmdONIVRFcoverage.ExecuteReaderAsync();
            if (await r.ReadAsync())
                _output.WriteLine($"ESCINO=ONIVRF coverage 2026: MATCHED={r[0]} TOTAL={r[1]}");
        }
        catch (Exception ex) { _output.WriteLine($"ONIVRF coverage error: {ex.Message}"); }

        // --- ODLINE.UBIVNO exploration ---
        // ODLINE has column UBIVNO (Invoice number) — check if it matches FSLEDG.ESCINO
        using var cmdUBIVNO = connection.CreateCommand();
        cmdUBIVNO.CommandText = $@"SELECT UBORNO, UBDLIX, UBPONR, UBPOSX, UBITNO, TRIM(UBIVNO) AS UBIVNO
            FROM {schema}.ODLINE
            WHERE UBCONO=100 AND TRIM(UBORNO)='0007009826' AND UBDLIX=80283
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdUBIVNO.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"ORNO={r[0]} DLIX={r[1]} PONR={r[2]} POSX={r[3]} ITNO={r[4]} IVNO='{r[5]}'");
            _output.WriteLine($"ODLINE.UBIVNO for order 0007009826/80283: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { _output.WriteLine($"ODLINE.UBIVNO error: {ex.Message}"); }

        // Does FSLEDG.ESCINO = ODLINE.UBIVNO?
        using var cmdUBIVNOmatch = connection.CreateCommand();
        cmdUBIVNOmatch.CommandText = $@"SELECT DISTINCT TRIM(f.ESCINO) AS ESCINO, TRIM(dl.UBIVNO) AS UBIVNO, TRIM(dl.UBORNO) AS ORNO, dl.UBDLIX
            FROM {schema}.FSLEDG f
            JOIN {schema}.ODLINE dl ON f.ESCONO=dl.UBCONO AND TRIM(f.ESCINO)=TRIM(dl.UBIVNO)
            WHERE f.ESCONO=100 AND TRIM(f.ESCINO)='003662262'
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdUBIVNOmatch.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"ESCINO='{r[0]}' UBIVNO='{r[1]}' ORNO={r[2]} DLIX={r[3]}");
            _output.WriteLine($"FSLEDG.ESCINO=ODLINE.UBIVNO for 003662262: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"ODLINE.UBIVNO match error: {ex.Message}"); }

        // Coverage: how many 2026 FSLEDG invoices match ODLINE via ESCINO=UBIVNO?
        using var cmdUBIVNOcoverage = connection.CreateCommand();
        cmdUBIVNOcoverage.CommandText = $@"SELECT COUNT(DISTINCT f.ESCINO) AS MATCHED,
            (SELECT COUNT(DISTINCT ESCINO) FROM {schema}.FSLEDG WHERE ESCONO=100 AND ESRGDT BETWEEN 20260101 AND 20261231 AND ESDIVI='L' AND ESTRCD=20) AS TOTAL
            FROM {schema}.FSLEDG f
            JOIN {schema}.ODLINE dl ON f.ESCONO=dl.UBCONO AND TRIM(f.ESCINO)=TRIM(dl.UBIVNO)
            WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20261231 AND f.ESDIVI='L' AND f.ESTRCD=20";
        try
        {
            using var r = await cmdUBIVNOcoverage.ExecuteReaderAsync();
            if (await r.ReadAsync())
                _output.WriteLine($"ESCINO=UBIVNO coverage 2026: MATCHED={r[0]} TOTAL={r[1]}");
        }
        catch (Exception ex) { _output.WriteLine($"UBIVNO coverage error: {ex.Message}"); }

        // --- OINVOH (Invoice head) exploration ---
        // OINVOH has UHIVNO (= OINVOL.ONIVNO) and UHVONO (voucher number)
        // FSLEDG 003662262: VONO=2103650 — check if OINVOH.UHVONO matches
        using var cmdOINVOH = connection.CreateCommand();
        cmdOINVOH.CommandText = $@"SELECT UHIVNO, UHPYNO, UHVONO, UHIVAM, UHIDAT, UHINST
            FROM {schema}.OINVOH
            WHERE UHCONO=100 AND UHVONO=2103650
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVOH.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"IVNO={r[0]} PYNO={r[1]} VONO={r[2]} AM={r[3]} IDAT={r[4]} INST={r[5]}");
            _output.WriteLine($"OINVOH where UHVONO=2103650: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOH VONO error: {ex.Message}"); }

        // Full path: FSLEDG.ESVONO = OINVOH.UHVONO → OINVOH.UHIVNO = ODLINE.UBIVNO
        using var cmdOINVOHpath = connection.CreateCommand();
        cmdOINVOHpath.CommandText = $@"SELECT DISTINCT f.ESCINO, oh.UHIVNO, oh.UHVONO, TRIM(dl.UBORNO) AS ORNO, dl.UBDLIX, dl.UBPONR, TRIM(dl.UBITNO) AS ITNO
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOH oh ON f.ESCONO=oh.UHCONO AND f.ESVONO=oh.UHVONO
            JOIN {schema}.ODLINE dl ON oh.UHCONO=dl.UBCONO AND oh.UHIVNO=dl.UBIVNO
            WHERE f.ESCONO=100 AND TRIM(f.ESCINO)='003662262'
            FETCH FIRST 5 ROWS ONLY";
        try
        {
            using var r = await cmdOINVOHpath.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await r.ReadAsync()) rows.Add($"CINO={r[0]} IVNO={r[1]} VONO={r[2]} ORNO={r[3]} DLIX={r[4]} PONR={r[5]} ITNO={r[6]}");
            _output.WriteLine($"FSLEDG.ESVONO→OINVOH→ODLINE for 003662262: {(rows.Count > 0 ? string.Join(" | ", rows) : "no match")}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOH path error: {ex.Message}"); }

        // Coverage: FSLEDG.ESVONO = OINVOH.UHVONO for 2026 invoices
        using var cmdOINVOHcoverage = connection.CreateCommand();
        cmdOINVOHcoverage.CommandText = $@"SELECT COUNT(DISTINCT f.ESCINO) AS MATCHED,
            (SELECT COUNT(DISTINCT ESCINO) FROM {schema}.FSLEDG WHERE ESCONO=100 AND ESRGDT BETWEEN 20260101 AND 20261231 AND ESDIVI='L' AND ESTRCD=20) AS TOTAL
            FROM {schema}.FSLEDG f
            JOIN {schema}.OINVOH oh ON f.ESCONO=oh.UHCONO AND f.ESVONO=oh.UHVONO
            WHERE f.ESCONO=100 AND f.ESRGDT BETWEEN 20260101 AND 20261231 AND f.ESDIVI='L' AND f.ESTRCD=20";
        try
        {
            using var r = await cmdOINVOHcoverage.ExecuteReaderAsync();
            if (await r.ReadAsync())
                _output.WriteLine($"ESVONO=UHVONO coverage 2026: MATCHED={r[0]} TOTAL={r[1]}");
        }
        catch (Exception ex) { _output.WriteLine($"OINVOH coverage error: {ex.Message}"); }
    }

    [Fact(DisplayName = "Full Pipeline Smoke Test with REAL DB2 and Validators")]
    public async Task SmokeTest_RealDb2_FetchAndMapInvoices_ShowValidationResults()
    {
        // ====================================================================
        // ARRANGE — Wire up REAL components (except HTTP + Audit Logger)
        // ====================================================================

        _output.WriteLine("=== MyInvois Service — Full Pipeline Smoke Test ===");
        _output.WriteLine($"Execution Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");

        // --- Configuration ---
        var movexDbSettings = _configuration.GetSection("MovexDb").Get<MovexDbSettings>()
            ?? throw new InvalidOperationException("MovexDb configuration missing");

        var apiSettings = _configuration.GetSection("MyInvoisApi").Get<MyInvoisApiSettings>()
            ?? throw new InvalidOperationException("MyInvoisApi configuration missing");

        var companySettings = _configuration.GetSection("Companies").Get<Dictionary<string, CompanyDetails>>()
            ?? throw new InvalidOperationException("Companies configuration missing");

        _output.WriteLine($"DB2 Strategy: {movexDbSettings.DataSourceStrategy}");
        _output.WriteLine($"Party Data Source: {movexDbSettings.PartyDataSource}");
        _output.WriteLine($"Active Companies: {string.Join(", ", movexDbSettings.ActiveCompanyCodes)}");
        _output.WriteLine($"MyInvois Environment: {apiSettings.Environment} ({apiSettings.BaseUrl})");
        _output.WriteLine("");

        // --- REAL Data Access Layer ---
        var dataSource = new DirectQueryDataSource(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<DirectQueryDataSource>());

        var partyProvider = new MovexMasterPartyDataProvider(
            Options.Create(movexDbSettings),
            new LoggerFactory().CreateLogger<MovexMasterPartyDataProvider>());

        var reader = new MovexInvoiceReader(
            dataSource,
            partyProvider,
            Options.Create(new ForeignPartyDefaultsSettings()),
            new LoggerFactory().CreateLogger<MovexInvoiceReader>());

        // --- REAL Validators ---
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var httpHandlerMock = new Mock<HttpMessageHandler>();
        SetupMockOAuthResponse(httpHandlerMock);
        SetupMockSubmissionResponse(httpHandlerMock);

        var httpClient = new HttpClient(httpHandlerMock.Object);
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var tinValidator = new TINValidator(
            memoryCache,
            httpClientFactoryMock.Object,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<TINValidator>());

        var mandatoryValidator = new MandatoryFieldsValidator();
        var dateValidator = new DateValidator();
        var currencyValidator = new CurrencyValidator();
        var totalsValidator = new TotalsValidator();

        // --- REAL Mapper ---
        var mapper = new MyInvoisMapper(
            mandatoryValidator,
            tinValidator,
            dateValidator,
            currencyValidator,
            totalsValidator,
            Options.Create(new CompanySettings { Companies = companySettings }),
            new LoggerFactory().CreateLogger<MyInvoisMapper>());

        // --- REAL Submitter ---
        var submitter = new MyInvoiceSubmitter(
            httpClientFactoryMock.Object,
            Options.Create(apiSettings),
            new LoggerFactory().CreateLogger<MyInvoiceSubmitter>());

        // --- MOCK Audit Logger  ---
        var auditLoggerMock = new Mock<IAuditLogger>();
        auditLoggerMock.Setup(a => a.IsInvoiceAlreadySubmitted(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // --- REAL Processor ---
        var processor = new InvoiceProcessor(
            reader,
            mapper,
            submitter,
            auditLoggerMock.Object,
            new LoggerFactory().CreateLogger<InvoiceProcessor>());

        // ====================================================================
        // ACT — Process invoices for the configured date range
        // ====================================================================

        // SmokeTest:FromDate / ToDate in appsettings.json (or User Secrets)
        // Format: "yyyy-MM-dd"  e.g. "2026-01-01"
        // Leave empty to default to the current calendar month.
        var fromDateStr = _configuration["SmokeTest:FromDate"];
        var toDateStr   = _configuration["SmokeTest:ToDate"];

        var now = DateTime.UtcNow;
        var fromDate = string.IsNullOrWhiteSpace(fromDateStr)
            ? new DateTime(now.Year, now.Month, 1)
            : DateTime.Parse(fromDateStr);
        var toDate = string.IsNullOrWhiteSpace(toDateStr)
            ? fromDate.AddMonths(1).AddSeconds(-1)
            : DateTime.Parse(toDateStr).Date.AddDays(1).AddSeconds(-1); // inclusive end-of-day

        _output.WriteLine($"--- FETCHING INVOICES FROM MOVEX DB2: {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd} ---");

        var result = await processor.ProcessDateRangeBatch(fromDate, toDate);

        // ====================================================================
        // ASSERT & REPORT — Structured output for stakeholders
        // ====================================================================

        _output.WriteLine("");
        _output.WriteLine("=== BATCH PROCESSING RESULTS ===");
        _output.WriteLine($"Total Invoices Found: {result.TotalInvoices}");
        _output.WriteLine($"Success Count: {result.SuccessCount}");
        _output.WriteLine($"Failed Count: {result.FailedCount}");
        _output.WriteLine($"Skipped Count: {result.SkippedCount}");
        _output.WriteLine($"Duration: {result.CompletedAt - result.StartedAt}");
        _output.WriteLine("");

        if (result.TotalInvoices == 0)
        {
            _output.WriteLine("⚠️ No invoices found in MOVEX for current month.");
            var activeCompanyCodes = _configuration.GetSection("MovexDb:ActiveCompanyCodes").Get<string[]>();
            var firstCompanyCode = (activeCompanyCodes != null && activeCompanyCodes.Length > 0)
                ? activeCompanyCodes[0]
                : "N/A";
            _output.WriteLine($"   Check: MovexDb:ActiveCompanyCodes and current month data in {firstCompanyCode}.");
            return;
        }

        // --- Detailed Submission Report ---
        _output.WriteLine("=== INVOICE DETAILS ===");
        foreach (var submission in result.Submissions.Take(10)) // Limit to first 10 for readability
        {
            _output.WriteLine($"\nInvoice: {submission.InvoiceNumber}");
            _output.WriteLine($"  Status: {submission.Status}");
            if (!string.IsNullOrEmpty(submission.MyInvoisUUID))
                _output.WriteLine($"  MyInvois UUID: {submission.MyInvoisUUID}");
            if (!string.IsNullOrEmpty(submission.ErrorCode))
                _output.WriteLine($"  Error Code: {submission.ErrorCode}");
            if (!string.IsNullOrEmpty(submission.ErrorMessage))
                _output.WriteLine($"  Error Message: {submission.ErrorMessage}");
            _output.WriteLine($"  Duration: {submission.DurationMs}ms");
        }

        if (result.Submissions.Count > 10)
        {
            _output.WriteLine($"\n... and {result.Submissions.Count - 10} more invoices.");
        }

        // --- Validation Gap Analysis ---
        _output.WriteLine("");
        _output.WriteLine("=== VALIDATION GAP ANALYSIS ===");

        var validationFailures = result.Submissions
            .Where(s => s.Status == "Failed" && s.ErrorCode == "VALIDATION")
            .ToList();

        if (validationFailures.Count > 0)
        {
            _output.WriteLine($"Validation Failures: {validationFailures.Count}/{result.TotalInvoices}");
            _output.WriteLine("");
            _output.WriteLine("Common validation errors (indicates missing party data):");

            var errorSummary = validationFailures
                .SelectMany(s => ExtractValidationErrors(s.ErrorMessage ?? ""))
                .GroupBy(e => e)
                .OrderByDescending(g => g.Count())
                .Take(5);

            foreach (var errorGroup in errorSummary)
            {
                _output.WriteLine($"  - {errorGroup.Key}: {errorGroup.Count()} occurrences");
            }

            _output.WriteLine("");
            _output.WriteLine("💡 Next Steps:");
            if (string.IsNullOrEmpty(movexDbSettings.SupplierTinColumn) || string.IsNullOrEmpty(movexDbSettings.CustomerTinColumn))
            {
                _output.WriteLine("   1. Configure TIN/BRN columns in MovexDbSettings (waiting on Finance team)");
                _output.WriteLine("      - MovexDb:SupplierTinColumn (e.g., 'IDCFC1')");
                _output.WriteLine("      - MovexDb:CustomerTinColumn (e.g., 'OKCFC1')");
            }
        }
        else
        {
            _output.WriteLine("✅ All invoices passed validation!");
        }

        // --- MyInvois Submission Analysis ---
        var submissionFailures = result.Submissions
            .Where(s => s.Status == "Failed" && s.ErrorCode != "VALIDATION")
            .ToList();

        if (submissionFailures.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"MyInvois Submission Failures: {submissionFailures.Count}");
            var submissionErrors = submissionFailures
                .GroupBy(s => s.ErrorCode)
                .OrderByDescending(g => g.Count());

            foreach (var errorGroup in submissionErrors)
            {
                _output.WriteLine($"  - {errorGroup.Key}: {errorGroup.Count()} occurrences");
            }

            if (submissionFailures.Any(s => s.ErrorCode == "DS301"))
            {
                _output.WriteLine("");
                _output.WriteLine("💡 DS301 (Invalid Signature) detected:");
                _output.WriteLine("   - XAdES v1.1 signing is placeholder — need to implement UBL 2.1 serialization + signing");
                _output.WriteLine("   - This is expected until MyInvois SDK is integrated");
            }
        }

        _output.WriteLine("");
        _output.WriteLine("=== END OF SMOKE TEST ===");

        // Basic assertion — test should not throw exceptions
        result.Should().NotBeNull();
        result.TotalInvoices.Should().BeGreaterThanOrEqualTo(0);
    }

    #region Helper Methods

    private void SetupMockOAuthResponse(Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("token")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    access_token = "smoke-test-token",
                    token_type = "Bearer",
                    expires_in = 3600
                }))
            });
    }

    private void SetupMockSubmissionResponse(Mock<HttpMessageHandler> handlerMock)
    {
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains("documentsubmissions")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    uuid = $"SMOKE-{Guid.NewGuid():N}",
                    submissionDate = DateTime.UtcNow.ToString("o"),
                    status = "Valid"
                }))
            });
    }

    private static List<string> ExtractValidationErrors(string errorMessage)
    {
        // Extract validation error messages from the error string
        var errors = new List<string>();
        if (string.IsNullOrEmpty(errorMessage))
            return errors;

        var lines = errorMessage.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("TIN") || trimmed.Contains("BRN") || trimmed.Contains("required"))
            {
                errors.Add(trimmed);
            }
        }

        return errors;
    }

    #endregion
}
