Next Steps for Pre-UAT Demo
To run the smoke test and show your boss:

Configure credentials (see SETUP_USER_SECRETS.md):


cd c:\Projects\MyInvois-Service\src\MyInvois.Service
dotnet user-secrets set "MovexDb:ConnectionString" "<YOUR_DB2_ODBC_CONNECTION>"
dotnet user-secrets set "MyInvoisApi:ClientId" "<SANDBOX_CLIENT_ID>"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "<SANDBOX_CLIENT_SECRET>"
Remove the Skip attribute from the smoke test:

Open FullPipelineSmokeTest.cs:38
Change: [Fact(Skip = "Manual...")] → [Fact]
Run the smoke test:


dotnet test --filter "Category=Smoke" --logger "console;verbosity=detailed"
Expected output (before Finance provides TIN columns):


=== BATCH PROCESSING RESULTS ===
Total Invoices Found: X
Success Count: 0
Failed Count: X

=== VALIDATION GAP ANALYSIS ===
Validation Failures: X/X
- TIN is required: X occurrences

💡 Next Steps:
   Configure TIN/BRN columns (waiting on Finance team)
This proves: DB2 connectivity works, invoices retrieved, party data (name/address) enriched, pipeline functional — only missing TIN/BRN column mappings.

📋 What Gaps Remain (By Design)
TIN/BRN columns — Finance provides next week → update appsettings.json config
UBL 2.1 serialization — Manual implementation needed (no SDK available)
XAdES v1.1 signing — Manual implementation needed
SQL Server audit table — Not required for smoke test (using mock)
The smoke test demonstrates the full pipeline up to the point where UBL/XAdES would be required. MyInvois will reject with DS301 (invalid signature) — proving submission logic works, just needs signing.