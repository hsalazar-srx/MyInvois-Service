# Data Flow - Invoice Processing Pipeline

**Last Updated:** February 18, 2026 (Updated with implementation progress)
**Status:** Production  
**Owner:** Solution Architect

## Purpose

This diagram shows how invoice data flows through MyInvois-Service from MOVEX to MyInvois, including:
- Data ingestion from MOVEX/M3 via IBM DB2/AS400 direct access (ADR-013)
- Validation and transformation
- Submission to MyInvois
- Audit logging
- Error handling and retry queues

## High-Level Data Flow

```mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
flowchart LR
  subgraph Sources["Data Sources"]
    MOVEX["MOVEX (M3)<br/>IBM DB2/AS400<br/>600-1100 invoices<br/>per month"]
  end
  
  subgraph Ingestion["Data Ingestion"]
    API["MOVEX DB2<br/>Direct Query<br/>30s timeout"]
  end
  
  subgraph Processing["Validation & Transformation"]
    FETCH["Fetch Invoices<br/>from MOVEX"]
    VALIDATE["Validate Fields<br/>20+ mandatory"]
    TRANSFORM["Transform<br/>MOVEX → UBL 2.1"]
    DEDUPE["Deduplication<br/>Check"]
  end
  
  subgraph Storage["Data Storage & Queues"]
    MEMORY["In-Memory<br/>Processing"]
    AUDITDB["SQL Server<br/>Audit Log<br/>7-year retention"]
    RETRYQ["Retry Queue<br/>Failed items"]
  end
  
  subgraph Submission["Submission"]
    SIGN["E-Sign<br/>XAdES v1.1<br/>Key Vault"]
    SUBMIT["Submit to<br/>MyInvois<br/>100 req/min"]
    RESPONSE["MyInvois<br/>Response"]
  end
  
  subgraph Output["Output & Reporting"]
    SUCCESS["Success Log<br/>MyInvois UUID"]
    FAILURE["Error Report<br/>to Finance"]
  end
  
  MOVEX --> API
  API --> FETCH
  FETCH --> MEMORY
  MEMORY --> VALIDATE
  VALIDATE --> DEDUPE
  DEDUPE --> TRANSFORM
  TRANSFORM --> SIGN
  SIGN --> SUBMIT
  SUBMIT --> RESPONSE
  RESPONSE -->|Success| SUCCESS
  RESPONSE -->|Failure| RETRYQ
  SUCCESS --> AUDITDB
  RETRYQ --> AUDITDB
  FAILURE --> AUDITDB
```

## Detailed Processing Sequence

```mermaid
---
config:
  theme: light
---
sequenceDiagram
  autonumber
  participant Scheduler as Batch Scheduler
  participant Service as MyInvois-Service
  participant MOVEX as MOVEX DB2
  participant Cache as Memory Cache
  participant Validator as Validator
  participant Signer as E-Signer
  participant MyInvois as MyInvois API
  participant DB as SQL Server<br/>Audit Log
  
  Scheduler->>Service: Trigger monthly batch
  Note over Service: Start of batch (1st of month)
  
  Service->>MOVEX: SELECT from fpledg/fsledg/fgledg<br/>(for past 30 days)
  MOVEX-->>Service: 600-1100 invoice rows (DB2 result set)
  Note over Service: MOVEX data received via DB2
  
  Service->>Cache: Load into memory
  Note over Service: Parse MOVEX format<br/>Organize by invoice ID
  
  rect rgb(200, 220, 255)
    Note over Service,Validator: Validation Loop (per invoice)
    loop For each invoice
      Service->>Validator: Validate 20+ fields
      alt Validation passes
        Validator-->>Service: Valid ✓
      else Validation fails
        Validator-->>Service: Errors collected
        Service->>DB: Log validation failure
        Service->>Service: Add to error report
      end
    end
  end
  
  rect rgb(200, 220, 255)
    Note over Service,Validator: Deduplication Check
    loop For each valid invoice
      Service->>Cache: Check invoice ID<br/>in audit log (last 12 months)
      alt Duplicate found
        Service->>DB: Log duplicate skip
      else New invoice
        Service->>Service: Mark for submission
      end
    end
  end
  
  rect rgb(200, 220, 255)
    Note over Service,Signer: Transformation & Signing
    loop For each invoice to submit
      Service->>Service: Transform: MOVEX → UBL 2.1
      Service->>Signer: Sign (XAdES v1.1)<br/>with Key Vault key
      Signer-->>Service: Signed document
    end
  end
  
  rect rgb(200, 255, 200)
    Note over Service,MyInvois: Submission Loop (600ms delay between each)
    loop For each signed invoice
      Note over Service: Enforce 600ms delay<br/>for rate limiting
      Service->>MyInvois: POST /submit<br/>Authorization: Bearer {token}
      alt Success (200 OK)
        MyInvois-->>Service: 200 + UUID
        Service->>DB: Log success + UUID
      else Rate Limited (429)
        MyInvois-->>Service: 429 Too Many
        Service->>Service: Exponential backoff
        Service->>Service: Retry later
      else Validation Error (400)
        MyInvois-->>Service: 400 + details
        Service->>DB: Log failure (non-retryable)
      else Server Error (500)
        MyInvois-->>Service: 500 Server Error
        Service->>Service: Queue for retry
      end
    end
  end
  
  Note over Service,DB: End of batch
  Service->>Service: Generate error report
  Service->>DB: Write batch summary<br/>Success count, failure count
  
  alt Failures present
    Service->>Service: Email error report<br/>to finance team
  end
```

## Data Transformation: MOVEX → UBL 2.1

### MOVEX Format (Source — DB2 Tables)

Data sourced from DB2 tables on schemas `mvxcdta`/`mvxc300`:
- `fpledg` — Accounts Payable ledger
- `fsledg` — Accounts Receivable ledger
- `fgledg` — General Ledger

```json
{
  "InvoiceNumber": "INV-2026-001",
  "CustomerName": "ABC Manufacturing",
  "InvoiceDate": "2026-02-05",
  "TotalAmount": 5000.00,
  "Currency": "MYR",
  "Items": [
    {
      "Description": "Widget",
      "Quantity": 100,
      "UnitPrice": 50.00
    }
  ]
}
```

### UBL 2.1 Format (Destination - MyInvois Required)
```xml
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">
  <ID>INV-2026-001</ID>
  <IssueDate>2026-02-05</IssueDate>
  <BillingReference>...</BillingReference>
  <InvoiceLine>
    <Item>
      <Description>Widget</Description>
      <Quantity>100</Quantity>
      <UnitPrice>50.00</UnitPrice>
    </Item>
  </InvoiceLine>
  <LegalMonetaryTotal>
    <TaxInclusiveAmount>5000.00</TaxInclusiveAmount>
  </LegalMonetaryTotal>
</Invoice>
```

---

## Validation Rules

### 20+ Mandatory Fields
1. Invoice ID ✓
2. Invoice Date ✓
3. Supplier Tax ID (TIN)
4. Buyer Tax ID (TIN)
5. Invoice Total ✓
6. Tax Amount
7. Line Items (min 1)
8. Currency (MYR for Malaysia)
9. Seller Name
10. Buyer Name
11. Payment Terms
12. Line item descriptions
13. Line item quantities
14. Line item unit prices
15. Line item tax rates
16. Document type code
17. Supplying country
18. Receiving country
19. Issue date format (ISO 8601)
20. Due date (if applicable)
... and more based on MyInvois spec

### Validation Error Collection
- ❌ NOT fail-fast (collect all errors)
- ✅ Report all issues in single error report
- ✅ Allow partial processing (some invoices valid, some not)
- ✅ Finance team can see exactly what's wrong

---

## Retry & Error Handling

### Transient Failures (Retryable)
- **408 Request Timeout** → Retry with exponential backoff
- **500 Internal Server Error** → Retry with exponential backoff
- **503 Service Unavailable** → Retry with exponential backoff
- **Network timeout** → Retry with exponential backoff

### Non-Transient Failures (Don't Retry)
- **400 Bad Request** → Data issue; finance team must fix
- **401 Unauthorized** → Token invalid; auto-refresh and retry
- **404 Not Found** → API endpoint wrong; escalate to IT
- **DS302 Duplicate** → Already submitted; skip

### Retry Strategy
- **1st attempt**: Immediate
- **2nd attempt**: After 5 seconds
- **3rd attempt**: After 10 seconds
- **Max retries**: 3 total
- **Backoff type**: Exponential

---

## Storage Strategy

### In-Memory Cache (During Batch)
- Raw MOVEX invoices: ~600-1100 items (~10 MB)
- Validated invoices: ~500-1000 items (processed)
- Transformation results: ~500-1000 UBL documents
- **Lifetime**: Single batch execution (~30 minutes)
- **Cleared**: After batch completes

### SQL Server Audit Log (Permanent)
- **Every submission attempt** logged: Invoice ID, source, result, timestamp
- **Success entries** include: MyInvois UUID, timestamp
- **Failure entries** include: Error message, timestamp
- **Immutable**: Never deleted (7-year retention)
- **Queryable**: Indexed for fast retrieval

### Retry Queue (Temporary)
- **Failed submissions** queued for retry
- **Manual review** before retry
- **Lifetime**: Until manually retried or manual skip
- **Logged** when moved to retry queue

---

## Performance Metrics

### Processing Time
- **Data fetch**: ~5 seconds (600-1100 invoices from MOVEX DB2)
- **Validation**: ~2-3 minutes (600+ validations)
- **Transformation**: ~1-2 minutes (600+ UBL conversions)
- **Signing**: ~2-3 minutes (600+ XAdES signatures)
- **Submission**: ~15-20 minutes (600+ @ 600ms each)
- **Total batch time**: ~25-30 minutes

### Throughput
- **MOVEX DB2 read rate**: 600-1100 per batch (monthly)
- **Submission rate**: 100 per minute (rate-limited by MyInvois)
- **Actual submission rate**: 99 per minute (600ms delay)

---

## Related Diagrams
- [architecture.md](architecture.md) - System components
- [integration-sequence.md](integration-sequence.md) - Batch timeline
- [auth-flow.md](auth-flow.md) - OAuth token management
