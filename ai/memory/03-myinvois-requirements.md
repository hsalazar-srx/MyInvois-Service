# MyInvois-Service - MyInvois Requirements & Constraints

**Last Updated**: 2026-02-05  
**Status**: MVAI Iteration 1 (Active)  
**Version**: 1.0  
**Source**: LHDNM Official Constraints & MyInvois API Documentation

---

## 📋 Overview

This document maps **MyInvois validation rules** from official LHDNM constraints to code implementation, with field-by-field requirements and acceptance criteria.

**Core Principle**: Every MyInvois constraint has a:
1. Code implementation (Validator class)
2. Test case
3. Audit log entry
4. Traceability matrix entry

---

## Part 1: Mandatory Fields by Layer

### Document-Level Fields (20+)

| Field | Type | Constraint | MOVEX Source | Validator | Status |
|-------|------|-----------|--------------|-----------|--------|
| **Invoice Number** | String | ≤50 chars, mandatory | OINVOH.IVNO | MandatoryFieldsValidator | ✅ |
| **Invoice Date** | Date | Real date (not "N/A", "0000-00-00"), UTC | OINVOH.IVDT | DateValidator | ✅ |
| **Invoice Time** | Time | 9 chars (HH:MM:SSss), UTC | System timestamp | DateValidator | ✅ |
| **Document Type** | Code | "01" = invoice | Hardcoded | MandatoryFieldsValidator | ✅ |
| **Version** | String | "1.1" | Hardcoded | MandatoryFieldsValidator | ✅ |
| **Currency Code** | Code | ISO 4217 (MYR, USD, SGD, etc.), 3 chars | OINVOH.CUCD | CurrencyValidator | ✅ |
| **Exchange Rate** | Decimal | Conditional: if currency ≠ MYR, mandatory and > 0 | OINVOH.ARAT | CurrencyValidator | ✅ |

### Supplier Fields (5+)

| Field | Type | Constraint | MOVEX Source | Validator | Status |
|-------|------|-----------|--------------|-----------|--------|
| **Supplier TIN** | String | 12 digits, mandatory | Company master | TINValidator | ✅ |
| **Supplier Name** | String | ≤300 chars, mandatory | Company master | MandatoryFieldsValidator | ✅ |
| **Supplier BRN** | String | Mandatory, schemeID=BRN | Company registration | MandatoryFieldsValidator | ✅ |
| **Supplier Address** | String | Recommended | Company master | MandatoryFieldsValidator | ✅ |
| **Supplier ID Scheme** | Code | "BRN" (or NRIC, PASSPORT, ARMY for individuals) | Hardcoded | MandatoryFieldsValidator | ✅ |

### Buyer Fields (4+)

| Field | Type | Constraint | MOVEX Source | Validator | Status |
|-------|------|-----------|--------------|-----------|--------|
| **Buyer TIN** | String | 12 digits, mandatory **if available** (B2C: can be null) | OCUSMA.TINO | TINValidator | ✅ |
| **Buyer Name** | String | ≤300 chars, mandatory | OCUSMA.CUNM | MandatoryFieldsValidator | ✅ |
| **Buyer Address** | String | Recommended | OCUSMA.CUNA | MandatoryFieldsValidator | ✅ |
| **Buyer ID Scheme** | Code | "BRN" (or alternative if TIN unavailable) | Hardcoded | MandatoryFieldsValidator | ✅ |

### Totals (4 fields)

| Field | Type | Constraint | Validation | Status |
|-------|------|-----------|-----------|--------|
| **Total Excl Tax** | Decimal | = Sum of line amounts (with ±1 cent tolerance) | TotalsValidator | ✅ |
| **Total Tax** | Decimal | = Sum of line taxes (with ±1 cent tolerance) | TotalsValidator | ✅ |
| **Total Incl Tax** | Decimal | = TotalExclTax + TotalTax | TotalsValidator | ✅ |
| **Payable Amount** | Decimal | = Total Incl Tax (or with adjustments) | TotalsValidator | ✅ |

---

## Part 2: Line-Item Fields

### Line-Level (7+ per line)

| Field | Type | Constraint | MOVEX Source | Validator | Status |
|-------|------|-----------|--------------|-----------|--------|
| **Item Number** | String | SKU or item code | MITMAS.ITNO | MandatoryFieldsValidator | ✅ |
| **Description** | String | ≤300 chars, mandatory | OINVOL.ITDS | MandatoryFieldsValidator | ✅ |
| **Classification Code** | Code | 3 chars, mandatory (per MyInvois scheme) | MITMAS.ITCL | MandatoryFieldsValidator | ✅ |
| **Quantity** | Decimal | > 0, mandatory | OINVOL.IVQA | MandatoryFieldsValidator | ✅ |
| **Unit of Measure** | Code | EA, KG, L, etc. | OINVOL.UOMEAS | MandatoryFieldsValidator | ✅ |
| **Unit Price** | Decimal | Mandatory, with currency context | OINVOL.SAPR | MandatoryFieldsValidator | ✅ |
| **Line Total Excl Tax** | Decimal | = Qty × UnitPrice | Calculated | TotalsValidator | ✅ |
| **Tax Type** | Code | UN/ECE 5153 (e.g., "01" = Standard VAT) | OINVOL.VTCD | MandatoryFieldsValidator | ✅ |
| **Tax Rate** | Decimal | % (6% in Malaysia standard) | CTAXC.TAPR | MandatoryFieldsValidator | ✅ |
| **Tax Amount** | Decimal | = LineTotal × TaxRate | Calculated | TotalsValidator | ✅ |

---

## Part 3: Validation Rules (Detailed)

### MandatoryFieldsValidator

**Responsible for**: Checks all 20+ mandatory fields and formats

**Test Cases**:

| Test Case | Input | Expected | Status |
|-----------|-------|----------|--------|
| `SupplierTIN_Required` | TIN = null | FAIL with error "TIN_Missing" | ✅ |
| `SupplierTIN_ValidFormat` | TIN = "123456789012" | PASS | ✅ |
| `InvoiceNumber_MaxLength` | Number > 50 chars | FAIL | ✅ |
| `InvoiceName_MaxLength` | Name > 300 chars | FAIL | ✅ |
| `ItemDescription_MaxLength` | Desc > 300 chars | FAIL | ✅ |
| `ItemClassification_Format` | Code ≠ 3 chars | FAIL | ✅ |
| `Quantity_GreaterThanZero` | Qty ≤ 0 | FAIL | ✅ |

### TINValidator

**Responsible for**: Format validation (12 digits, numeric)

**Test Cases**:

| Test Case | Input | Expected | Status |
|-----------|-------|----------|--------|
| `TIN_ValidFormat` | "123456789012" | PASS | ✅ |
| `TIN_TooShort` | "12345678901" | FAIL | ✅ |
| `TIN_TooLong` | "1234567890123" | FAIL | ✅ |
| `TIN_NonNumeric` | "12345678901a" | FAIL | ✅ |
| `BuyerTIN_Optional` | TIN = null (B2C) | PASS (with warning) | ✅ |
| `TIN_APIValidation_Sandbox` | MyInvois API check | PASS (if exists) | ⏳ Phase 2 |

### DateValidator

**Responsible for**: Real date validation, UTC conversion

**Test Cases**:

| Test Case | Input | Expected | Status |
|-----------|-------|----------|--------|
| `Date_ValidISO8601` | "2026-02-05" | PASS | ✅ |
| `Date_Placeholder_NA` | "N/A" | FAIL | ✅ |
| `Date_Placeholder_Zeros` | "0000-00-00" | FAIL | ✅ |
| `Date_InvalidDay` | "2026-02-30" | FAIL | ✅ |
| `Date_InvalidMonth` | "2026-13-01" | FAIL | ✅ |
| `Date_NotInFuture` | Date > today | FAIL | ✅ |
| `Time_ValidFormat` | "14:30:45" | PASS | ✅ |
| `Time_9CharFormat` | "143045ss" | PASS | ✅ |

### CurrencyValidator

**Responsible for**: ISO 4217 validation, exchange rate requirements

**Test Cases**:

| Test Case | Input | Expected | Status |
|-----------|-------|----------|--------|
| `Currency_ValidISO4217_MYR` | "MYR" | PASS | ✅ |
| `Currency_ValidISO4217_USD` | "USD" | PASS | ✅ |
| `Currency_InvalidCode` | "XYZ" | FAIL | ✅ |
| `ExchangeRate_MYR_NotRequired` | Currency="MYR", Rate=null | PASS | ✅ |
| `ExchangeRate_NonMYR_Required` | Currency="USD", Rate=null | FAIL | ✅ |
| `ExchangeRate_MustBePositive` | Rate=0 or negative | FAIL | ✅ |
| `ExchangeRate_6DecimalMax` | Rate=1.1234567 | FAIL | ✅ |
| `ExchangeRate_6DecimalOK` | Rate=1.123456 | PASS | ✅ |

### TotalsValidator

**Responsible for**: Mathematical consistency of invoice totals

**Test Cases**:

| Test Case | Input | Expected | Status |
|-----------|-------|----------|--------|
| `TotalExclTax_Correct` | Sum of lines = 300 | PASS | ✅ |
| `TotalExclTax_Incorrect` | Sum = 300 but total = 250 | FAIL | ✅ |
| `TotalTax_Correct` | Sum of line taxes = 30 | PASS | ✅ |
| `TotalInclTax_Correct` | = TotalExclTax + TotalTax | PASS | ✅ |
| `RoundingTolerance_1Cent` | Difference < 0.01 | PASS | ✅ |
| `RoundingTolerance_Over` | Difference > 0.01 | FAIL | ✅ |
| `EmptyLines_ZeroTotals` | No lines, all 0 | PASS | ✅ |

---

## Part 4: Field Mapping (MOVEX → MyInvois)

### Supplier Block

```
MOVEX Company Master (General + TIN Config)
    └─ CMCONO, CMCUSM (Company)
    └─ CMCO10 (TIN or Tax ID)
    └─ CMNAME (Name)
    └─ CMCSCD (Customer/Vendor Code)

Mapping:
    ├─ SupplierTIN ← CMCO10 (12 digits)
    ├─ SupplierName ← CMNAME
    ├─ SupplierBRN ← CMCSCD
    ├─ SupplierIdScheme ← "BRN" (hardcoded)
    └─ SupplierAddress ← CMADD1 + CMADD2 + CMADD3
```

### Buyer Block

```
MOVEX Customer Master (OCUSMA)
    └─ CACONO, CACUSN (Customer Code)
    └─ CATINO (TIN, if available)
    └─ CACUNM (Name)
    └─ CACSCD (Registration Code)

Mapping:
    ├─ BuyerTIN ← CATINO (optional, can be null for B2C)
    ├─ BuyerName ← CACUNM
    ├─ BuyerAlternativeId ← CACSCD (if TIN unavailable)
    ├─ BuyerIdScheme ← "BRN" or "NRIC" (based on available ID)
    └─ BuyerAddress ← CAADD1 + CAADD2 + CAADD3
```

### Invoice Header

```
MOVEX Invoice Header (OINVOH)
    └─ OIVNO (Invoice Number)
    └─ OIVDT (Invoice Date, YYYYMMDD)
    └─ OIVCU (Currency Code)
    └─ OIARAT (Exchange Rate)
    └─ OINLAM (Total Excl Tax)
    └─ OIVTAM (Total Tax)
    └─ OICUAM (Total Incl Tax)

Mapping:
    ├─ InvoiceNumber ← OIVNO
    ├─ IssueDate ← Convert OIVDT to YYYY-MM-DD
    ├─ IssueTime ← System timestamp (HH:MM:SS)
    ├─ CurrencyCode ← OIVCU
    ├─ ExchangeRate ← OIARAT (default 1.0 if MYR)
    ├─ TotalExclTax ← OINLAM
    ├─ TotalTax ← OIVTAM
    └─ TotalInclTax ← OICUAM
```

### Line Items

```
MOVEX Invoice Line (OINVOL)
    └─ OILVNO (Line Number)
    └─ OILITNO (Item Number)
    └─ OILITDS (Description)
    └─ OILQA (Quantity)
    └─ OILSA (Sale Price / Unit Price)
    └─ OILTAX (Line Tax Amount)

Item Master (MITMAS)
    └─ ITCL (Item Classification Code, 3 chars)

Mapping:
    ├─ ItemNumber ← OILITNO
    ├─ Description ← OILITDS
    ├─ ClassificationCode ← ITCL (lookup from MITMAS)
    ├─ Quantity ← OILQA
    ├─ UnitPrice ← OILSA
    ├─ LineTotalExclTax ← Qty × UnitPrice (recalculate)
    ├─ TaxCode ← "01" (hardcoded for standard 6% VAT)
    ├─ TaxRate ← 6% (standard)
    └─ TaxAmount ← OILTAX
```

---

## Part 5: Error Codes & Classification

### MyInvois API Error Codes

| Code | Description | Type | Action | Status |
|------|-------------|------|--------|--------|
| **DS101** | Invalid JSON format | No-Retry | Manual review | ✅ |
| **DS102** | Missing mandatory field | No-Retry | Manual review | ✅ |
| **DS103** | Invalid field value | No-Retry | Manual review | ✅ |
| **DS301** | Invalid signature | No-Retry | Manual review | ✅ |
| **DS302** | Duplicate invoice (already submitted) | No-Retry | Verify in audit log | ✅ |
| **DS401** | Invalid TIN format | No-Retry | Manual review | ✅ |
| **DS402** | TIN not found in registry | No-Retry | Manual review | ✅ |
| **429** | Rate limit exceeded (100 req/min) | Retriable | Auto-retry with backoff | ✅ |
| **500** | Internal server error | Retriable | Auto-retry (up to 3) | ✅ |
| **503** | Service unavailable | Retriable | Auto-retry with backoff | ✅ |

---

## Part 6: MyInvois Compliance Checklist

- ✅ **Schema Compliance**: UBL 2.1 format validated
- ✅ **Mandatory Fields**: All 20+ fields validated
- ✅ **TIN Validation**: 12-digit format, numeric
- ✅ **Date/Time**: ISO 8601, UTC timezone
- ✅ **Currency**: ISO 4217 codes, exchange rates
- ✅ **Line Items**: Classification, descriptions, calculations
- ✅ **Digital Signature**: XAdES v1.1 format
- ✅ **Audit Trail**: Complete submission history
- ✅ **Duplicate Detection**: MyInvois UUID + local tracking
- ✅ **Error Handling**: Classified by retry-ability

---

## Part 7: Implementation Status (MVAI)

| Component | Status | Owner | ETA |
|-----------|--------|-------|-----|
| MandatoryFieldsValidator | ⏳ Week 2 | Dev Team | Feb 14 |
| TINValidator (format only) | ⏳ Week 2 | Dev Team | Feb 14 |
| TINValidator (API) | 📅 Phase 2 | Dev Team | Mar 5 |
| DateValidator | ⏳ Week 2 | Dev Team | Feb 14 |
| CurrencyValidator | ⏳ Week 2 | Dev Team | Feb 14 |
| TotalsValidator | ⏳ Week 2 | Dev Team | Feb 14 |
| MyInvoiceMapper | ⏳ Week 2 | Dev Team | Feb 14 |
| MyInvoiceSubmitter | ⏳ Week 2 | Dev Team | Feb 14 |
| Unit Tests | ⏳ Week 2 | QA Team | Feb 14 |
| Integration Tests | ⏳ Week 3 | QA Team | Feb 21 |
| UAT (Sandbox) | ⏳ Week 4 | Finance Team | Feb 28 |

---

---

## Part 8: Implementation Traceability Matrix

### Mandatory Fields Traceability

| MyInvois Field | Constraint | MOVEX Source | Implementation Class | Validation Method | Test Case | Priority |
|----------------|------------|--------------|----------------------|------------------|-----------|----------|
| **Supplier TIN** | Mandatory, TIN format | Company master (TIN config) | `MyInvoiceMapper.cs` | `ValidateTIN(string tin)` | `MandatoryFieldsValidatorTests.cs` - `TIN_Required_And_Valid` | P0 |
| **Supplier Name** | Mandatory, ≤300 chars | Company master | `MyInvoiceMapper.cs` | `ValidateSupplierName(string name)` | `MandatoryFieldsValidatorTests.cs` - `SupplierName_MaxLength` | P0 |
| **Buyer TIN** | Mandatory (if available) | Customer master (OCUSMA.TINO) | `MyInvoiceMapper.cs` | `ValidateBuyerTIN(string tin, bool required)` | `MandatoryFieldsValidatorTests.cs` - `BuyerTIN_OptionalIfMissing` | P0 |
| **Invoice Number** | Mandatory, ≤50 chars | Invoice header (OINVOH.IVNO) | `MovexInvoiceReader.cs` | `ValidateInvoiceNumber(string number)` | `MandatoryFieldsValidatorTests.cs` - `InvoiceNumber_MaxLength` | P0 |
| **Invoice Date** | Mandatory, real date (UTC) | Invoice header (OINVOH.IVDT) | `MyInvoiceMapper.cs` | `ValidateInvoiceDate(DateTime date)` | `DateValidatorTests.cs` - `Date_NoPlaceholders` | P0 |
| **Currency Code** | Mandatory, 3 chars | Invoice header (OINVOH.CUCD) | `MyInvoiceMapper.cs` | `ValidateCurrencyCode(string code)` | `CurrencyValidatorTests.cs` - `Currency_ValidISO4217` | P0 |

### Validation Rules → Implementation

| MyInvois Validation | Implementation Location | Owner | Test Location | Status | Notes |
|---------------------|------------------------|-------|-------------|--------|-------|
| **All mandatory fields present** | `Validators/MandatoryFieldsValidator.cs` | Dev | `MapperTests.cs` | ⏳ Week 2 | Check all 20 fields present before submission |
| **Field length constraints** | `Validators/MandatoryFieldsValidator.cs` | Dev | `MapperTests.cs` | ⏳ Week 2 | TIN 12 chars, invoice number ≤50, name ≤300 |
| **No placeholder dates** | `Validators/DateValidator.cs` | Dev | `DateValidatorTests.cs` | ⏳ Week 2 | Reject "N/A", "0000-00-00", null |
| **Real ISO 8601 date** | `Validators/DateValidator.cs` | Dev | `DateValidatorTests.cs` | ⏳ Week 2 | Format: YYYY-MM-DD, validate leap years, etc. |
| **Currency code ISO 4217** | `Validators/CurrencyValidator.cs` | Dev | `CurrencyValidatorTests.cs` | ⏳ Week 2 | MYR, USD, SGD, etc. Valid codes only |

### Error Codes & Handling Strategy

| MyInvois Error | HTTP Code | Meaning | Retry Strategy | Implementation | User Action | Owner |
|----------------|-----------|---------|-----------------|-----------------|------------|-------|
| **Duplicate (DS302)** | 400 | Invoice already submitted | NO RETRY | `MyInvoiceSubmitter.cs` | Mark as "Already Submitted", investigate | Finance |
| **Invalid TIN** | 400 | TIN format/lookup failed | NO RETRY | `TINValidator.cs` | Alert Finance team, correct TIN in MOVEX | Finance |
| **Invalid structure** | 400 | XML/UBL schema invalid | NO RETRY | `MandatoryFieldsValidator.cs` | Log error, alert Dev team | Dev |
| **Rate Limit** | 429 | Too many requests | YES - exponential backoff | Polly retry policy (Iteration 2) | Log warning, adjust batch timing | Dev |
| **Server Error** | 500/503 | MyInvois internal error | YES - 3 retries with 5s delay | Polly retry policy (Iteration 2) | Log error, mark as "Failed", retry next batch | Dev |
| **Timeout** | 408/504 | Response timeout | YES - 3 retries with 5s delay | Polly timeout policy (Iteration 2) | Log error, mark as "Failed", retry next batch | Dev |
| **Unauthorized (401)** | 401 | Token expired | YES - refresh token, retry once | `MyInvoiceSubmitter.cs` OAuth refresh | Log error, auto-retry with new token | Dev |
| **Forbidden (403)** | 403 | Access denied (wrong certificate) | NO RETRY | Check certificate DN fields | Alert IT Ops, verify certificate | IT Ops |

### Rate Limits & Resilience

| Endpoint | Limit | Window | Batch Strategy | Implementation |
|----------|-------|--------|-----------------|-----------------|
| **Submit Documents** | 100 requests | 1 minute | Sales: 1 batch/month, Purchase: 10 batches of 50 each | Batch size config |
| **Validate TIN** | 60 requests | 1 minute | Cache results 1 hour (90% cache hit target) | Token cache + Redis (Phase 2) |
| **Login (Token)** | 12 requests | 1 minute | Cache token 1 hour TTL | OAuth client credentials caching |

**Batch Processing Strategy:**
```
Sales Invoices:
  - ~100/month → 1 batch of 100
  - Processing: 100 invoices × 0.5 sec each = 50 sec total

Purchase Invoices:
  - ~500-1000/month → 10-20 batches of 50 each
  - Processing per batch: 50 invoices × 0.5 sec = 25 sec
  - Delay between batches: 0.6 sec (safety margin for 100 RPM)
  - Total processing time: 20 batches × (25 sec + 0.6 sec) = ~510 sec (~8.5 min)

Safety Margin: At 100 RPM, we submit max ~1.67 req/sec
Our peak: 20 batches/min = 20 req/min = 0.33 req/sec
Utilization: 20%
```

---

## 🔗 Related Documents

- [00-Product Vision](00-product-vision.md) - Vision & objectives
- [01-System Architecture](01-system-architecture.md) - Architecture & components
- [02-Data Model](02-data-model.md) - Data structures & schema
- [04-API Integration](04-api-integration.md) - MOVEX & MyInvois API specs
- [09-implementation-decisions.md](09-implementation-decisions.md) - ADRs & decisions
- [10-testing-strategy.md](10-testing-strategy.md) - Testing strategy & test cases

---

**Owner**: Requirements Team  
**Source**: LHDNM Official Documentation & Implementation Traceability  
**Last Review**: 2026-02-06  
**Next Review**: 2026-03-05

