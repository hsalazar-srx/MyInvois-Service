# Audit Logging Pattern

**Purpose:** Log all operations to workspace-compliant audit schema
**Last Updated:** 2026-02-17

---

## 📋 Workspace Standard Schema

Per [../../.github/WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md):
- Database: `SRX_AuditLog`
- Table: `[dbo].[AuditLog]`
- Standard fields + MyInvois-specific extensions

---

## 🎯 Complete Example

```csharp
/// <summary>
/// Logs MyInvois submission to workspace-compliant audit table.
/// </summary>
public async Task LogSubmissionAsync(
    string invoiceNumber,
    decimal totalAmount,
    string currencyCode,
    bool success,
    MyInvoisResponse response,
    Guid correlationId,
    int durationMs)
{
    await _auditLogger.LogAsync(new AuditEntry
    {
        // ===== STANDARD FIELDS (Required by Workspace) =====
        // Who
        UserId = _currentUser.WindowsId,           // e.g., "SRXGLOBAL\\svc-myinvois"
        UserRole = "Service_MyInvoicing",
        IpAddress = _httpContext.Connection.RemoteIpAddress?.ToString(),

        // What
        Action = "MyInvois_Submit",
        Category = "Integration",
        Severity = success ? "Info" : "Error",

        // Where
        ResourceType = "Invoice",
        ResourceId = invoiceNumber,                // e.g., "INV-2026-00001"
        Endpoint = "POST /api/v1/submissions",

        // Result
        Status = success ? "Success" : "Failed",
        StatusCode = response?.StatusCode,         // e.g., "200", "DS302"
        ErrorMessage = response?.ErrorMessage,

        // Payload (for forensics - no PII!)
        RequestPayload = JsonSerializer.Serialize(new {
            invoiceNumber,
            totalAmount,
            currencyCode
            // NO customer names, Tax IDs, etc.
        }),
        ResponsePayload = response != null
            ? JsonSerializer.Serialize(response)
            : null,

        // Metadata
        CorrelationId = correlationId,
        Duration = durationMs,
        RetryCount = 0,

        // ===== MYINVOIS-SPECIFIC EXTENSIONS =====
        // Documented in ai/memory/02-data-model.md
        MyInvoisUUID = response?.UUID,
        InvoiceNumber = invoiceNumber,
        InvoiceDate = DateTime.UtcNow.Date,
        TotalAmount = totalAmount,
        CurrencyCode = currencyCode
    });
}
```

---

## ✅ Key Rules

### DO:
- ✅ Use standard workspace fields
- ✅ Log both success AND failure
- ✅ Include correlation ID for tracing
- ✅ Record duration for performance tracking
- ✅ Use UTC timestamps (SQL: `SYSUTCDATETIME()`)

### DON'T:
- ❌ Log PII (Tax IDs, customer names, emails, phones)
- ❌ Skip logging on success (log EVERYTHING)
- ❌ Use local time (always UTC)
- ❌ Modify audit logs after insert (immutable)
- ❌ Log full request/response with sensitive data

---

## 🔍 What to Log vs Not Log

### ✅ Safe to Log:
- Invoice numbers
- Amounts and currency codes
- Status codes and error codes
- Timestamps
- User Windows ID / service account
- Correlation IDs
- MyInvois UUID (public identifier)

### ❌ NEVER Log:
- Tax IDs (TIN, BRN)
- Customer names
- Email addresses
- Phone numbers
- Payment card details
- Passwords or API keys

---

## 📊 Query Examples

### Failed Submissions in Last 24 Hours

```sql
SELECT
    ResourceId AS InvoiceNumber,
    StatusCode,
    ErrorMessage,
    Timestamp
FROM [dbo].[AuditLog]
WHERE
    Action = 'MyInvois_Submit'
    AND Status = 'Failed'
    AND Timestamp >= DATEADD(HOUR, -24, SYSUTCDATETIME())
ORDER BY Timestamp DESC;
```

### Success Rate by Day

```sql
SELECT
    CAST(Timestamp AS DATE) AS SubmissionDate,
    COUNT(*) AS TotalSubmissions,
    SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) AS Successful,
    (SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) * 100.0 / COUNT(*)) AS SuccessRate
FROM [dbo].[AuditLog]
WHERE Action = 'MyInvois_Submit'
GROUP BY CAST(Timestamp AS DATE)
ORDER BY SubmissionDate DESC;
```

---

## 🔗 Related

- [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) - Standard audit schema
- [ai/memory/02-data-model.md](../memory/02-data-model.md) - MyInvois extensions
- [Configuration Pattern](configuration.md) - How to configure audit logger
