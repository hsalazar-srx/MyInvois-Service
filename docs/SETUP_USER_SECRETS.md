# User Secrets Setup Guide

This guide helps you configure User Secrets for the MyInvois-Service smoke test.

## Prerequisites

- DB2/ODBC connection to MOVEX AS/400 (CMP300 dev environment)
- MyInvois sandbox API credentials (ClientId + ClientSecret)

## Setup Commands

Run these commands from the `src/MyInvois.Service` directory:

```bash
cd c:\Projects\MyInvois-Service\src\MyInvois.Service

# 1. DB2 Connection String (MOVEX AS/400)
dotnet user-secrets set "MovexDb:ConnectionString" "DSN=YOUR_DB2_DSN;UID=YOUR_USER;PWD=YOUR_PASSWORD"

# Alternative ODBC connection string format:
# dotnet user-secrets set "MovexDb:ConnectionString" "Driver={IBM i Access ODBC Driver};System=YOUR_AS400_HOST;UID=YOUR_USER;PWD=YOUR_PASSWORD"

# 2. MyInvois Sandbox API Credentials
dotnet user-secrets set "MyInvoisApi:ClientId" "YOUR_SANDBOX_CLIENT_ID"
dotnet user-secrets set "MyInvoisApi:ClientSecret" "YOUR_SANDBOX_CLIENT_SECRET"
```

## Verify Secrets

Check that secrets are set correctly:

```bash
dotnet user-secrets list
```

Expected output:
```
MovexDb:ConnectionString = DSN=...
MyInvoisApi:ClientId = ...
MyInvoisApi:ClientSecret = ...
```

## Test Connection

Run the smoke test to verify connectivity:

```bash
cd c:\Projects\MyInvois-Service

# Remove the Skip attribute from FullPipelineSmokeTest.cs first (line 38)
# Change: [Fact(Skip = "Manual execution only...")]
# To:     [Fact]

dotnet test tests/MyInvois.Service.Tests --filter "Category=Smoke" --logger "console;verbosity=detailed"
```

## Configuration: Enable MovexMasterPartyDataProvider

Update `appsettings.json` to use real party data provider:

```json
{
  "MovexDb": {
    "PartyDataSource": "MovexMaster",  // Change from "Placeholder"
    "SupplierTinColumn": "",  // Leave empty until Finance provides column name
    "CustomerTinColumn": "",  // Leave empty until Finance provides column name
    "SupplierBrnColumn": "",
    "CustomerBrnColumn": ""
  }
}
```

## Expected Smoke Test Results

### Stage 1: Before Finance provides TIN/BRN columns

```
=== BATCH PROCESSING RESULTS ===
Total Invoices Found: X
Success Count: 0
Failed Count: X
Skipped Count: 0

=== VALIDATION GAP ANALYSIS ===
Validation Failures: X/X

Common validation errors:
  - TIN is required: X occurrences
  - BRN is required: X occurrences

💡 Next Steps:
   1. Configure TIN/BRN columns in MovexDbSettings (waiting on Finance team)
```

**Interpretation:** Pipeline works! DB2 connection successful, invoices fetched, party data (name/address) retrieved. Validation correctly identifies missing TIN/BRN — this gap closes when Finance provides column names.

### Stage 2: After Finance provides TIN/BRN columns

Update `appsettings.json`:
```json
{
  "MovexDb": {
    "SupplierTinColumn": "IDCFC1",  // Example — confirm with Finance
    "CustomerTinColumn": "OKCFC1"   // Example — confirm with Finance
  }
}
```

Re-run smoke test:
```
=== BATCH PROCESSING RESULTS ===
Total Invoices Found: X
Success Count: 0
Failed Count: X

=== MyInvois Submission Failures: X
  - DS301 (Invalid Signature): X occurrences

💡 DS301 (Invalid Signature) detected:
   - XAdES v1.1 signing is placeholder
```

**Interpretation:** Validation passes! Submission fails due to unsigned XML — expected until UBL 2.1 + XAdES implementation.

## Troubleshooting

### DB2 Connection Errors

**Error:** `SQLSTATE 08001` or connection timeout
- **Fix:** Verify AS/400 hostname, credentials, and network connectivity
- **Test:** `ping YOUR_AS400_HOST` and check firewall rules

**Error:** `[IBM][CLI Driver] SQL1024N  A database connection does not exist.`
- **Fix:** DB2 ODBC driver not installed
- **Install:** Download IBM i Access ODBC Driver from IBM

### MyInvois API Errors

**Error:** 401 Unauthorized during OAuth
- **Fix:** Verify ClientId/ClientSecret are correct for sandbox environment
- **Check:** Credentials match those registered at https://sandbox.myinvois.hasil.gov.my

**Error:** Token endpoint timeout
- **Fix:** Check network access to MyInvois sandbox
- **Test:** `curl https://sandbox.myinvois.hasil.gov.my/connect/token`

## Next Steps After Smoke Test

1. ✅ Verify DB2 connectivity and invoice retrieval
2. ✅ Confirm party data enrichment (name/address)
3. ⏳ Get TIN/BRN column names from Finance team
4. ⏳ Implement UBL 2.1 serialization (manual or SDK-based)
5. ⏳ Implement XAdES v1.1 signing (requires digital certificate)
6. 🎯 Full end-to-end submission to MyInvois sandbox

## Support

- **DB2 Connection Issues:** Contact DBA team
- **MyInvois API Issues:** Check https://sdk.myinvois.hasil.gov.my/
- **Code Issues:** Review PROJECT_STATUS.md and IMPLEMENTATION_CHECKLIST.md
