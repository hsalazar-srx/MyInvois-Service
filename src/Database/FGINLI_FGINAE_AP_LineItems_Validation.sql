-- =============================================================================
-- FGINLI + FGINAE AP Line Items Validation Queries
-- Purpose: Validate that FGINLI (invoice lines) + FGINAE (line accounting entries)
--          provide correct per-line tax data for AP (purchase) invoices.
-- Schema:  MVXCDTA (Company 100)
-- Date:    2026-03-27
-- =============================================================================

-- Query 0a: Pick recent AP invoices from FPLEDG with non-zero VAT
-- Look for invoices that have VAT so we can validate the tax extraction
SELECT
    TRIM(EPSUNO) AS Supplier,
    TRIM(EPSINO) AS InvoiceNo,
    EPYEA4 AS InvoiceYear,
    EPACDT AS AccountingDate,
    EPCUAM AS InvoiceAmount,
    EPVTAM AS VatTotal,
    TRIM(EPCUCD) AS Currency
FROM MVXCDTA.FPLEDG
WHERE EPCONO = 100
  AND EPDIVI = 'L'
  AND EPTRCD = 10
  AND EPACDT >= 20260101
  AND EPVTAM <> 0
ORDER BY EPACDT DESC
FETCH FIRST 10 ROWS ONLY;

-- =============================================================================
-- After running 0a, pick a supplier+invoice and substitute below:
--   Replace <SUPPLIER> with the EPSUNO value (e.g., 'Y60050')
--   Replace <INVOICE> with the EPSINO value (e.g., 'INV-2026-001')
--   Replace <YEAR>    with the EPYEA4 value (e.g., 2026)
-- =============================================================================

-- Query 0b: Check FGINLI lines for that invoice
-- Confirms that FGINLI has line-level data keyed by SUNO+SINO+INYR
SELECT
    TRIM(F5SUNO) AS Supplier,
    TRIM(F5SINO) AS InvoiceNo,
    F5INYR AS InvoiceYear,
    TRIM(F5PUNO) AS PurchaseOrder,
    F5PNLI AS POLine,
    F5IVQT AS Quantity,
    F5IVOC AS InvoicedPrice,
    F5IVNA AS NetAmount,
    TRIM(F5VTCD) AS VatCode,
    TRIM(F5PUUN) AS PriceUoM,
    F5PUCD AS PriceQtyBasis,
    F5IMST AS MatchStatus
FROM MVXCDTA.FGINLI
WHERE F5CONO = 100
  AND TRIM(F5SUNO) = '<SUPPLIER>'
  AND TRIM(F5SINO) = '<INVOICE>'
ORDER BY F5PUNO, F5PNLI;

-- Query 0c: Check FGINAE accounting entries for those lines
-- Look for VAT entries (F9VTCD <> '') vs goods entries (F9VTCD = '')
SELECT
    TRIM(F9SUNO) AS Supplier,
    TRIM(F9SINO) AS InvoiceNo,
    F9INYR AS InvoiceYear,
    TRIM(F9PUNO) AS PurchaseOrder,
    F9PNLI AS POLine,
    F9INIT AS AcctInfoType,
    TRIM(F9VTCD) AS VatCode,
    F9CUAM AS ForeignAmount,
    F9ACAM AS LocalAmount,
    F9ACQT AS AccountedQty,
    TRIM(F9AIT1) AS Dimension1_GL,
    TRIM(F9CUCD) AS Currency
FROM MVXCDTA.FGINAE
WHERE F9CONO = 100
  AND TRIM(F9SUNO) = '<SUPPLIER>'
  AND TRIM(F9SINO) = '<INVOICE>'
ORDER BY F9PUNO, F9PNLI, F9INIT;

-- Query 0d: Validate VAT reconciliation
-- SUM of FGINAE VAT entries should match FPLEDG.EPVTAM
SELECT
    'FGINAE VAT Sum' AS Source,
    SUM(F9CUAM) AS VatAmount
FROM MVXCDTA.FGINAE
WHERE F9CONO = 100
  AND TRIM(F9SUNO) = '<SUPPLIER>'
  AND TRIM(F9SINO) = '<INVOICE>'
  AND TRIM(F9VTCD) <> ''
UNION ALL
SELECT
    'FPLEDG Header VAT' AS Source,
    EPVTAM AS VatAmount
FROM MVXCDTA.FPLEDG
WHERE EPCONO = 100
  AND TRIM(EPSUNO) = '<SUPPLIER>'
  AND TRIM(EPSINO) = '<INVOICE>'
  AND EPTRCD = 10;

-- Query 0f: Check FGLEDG for VAT rates on the voucher
-- FGLEDG stores VAT rate 1 (EGVTP1) and VAT rate 2 (EGVTP2) plus VAT amount (EGVTAM)
SELECT EGVONO, EGYEA4, TRIM(EGAIT1) AS GL, EGACAM, EGCUAM,
       TRIM(EGVTCD) AS VatCode, EGVTP1 AS VatRate1, EGVTP2 AS VatRate2, EGVTAM AS VatAmount
FROM MVXCDTA.FGLEDG
WHERE EGCONO = 100 AND EGVONO IN (
    SELECT TRIM(EPVONO) FROM MVXCDTA.FPLEDG
    WHERE EPCONO = 100 AND TRIM(EPSUNO) = 'SS015' AND TRIM(EPSINO) = '2405NV2600190'
)
ORDER BY EGAIT1;

-- Query 0g: Check FGINHE (invoice header) for VAT total
SELECT F4SUNO, F4SINO, F4INYR, F4IVCU AS InvoiceAmountCurrency, F4CUCD AS Currency,
       F4ARAT AS ExchangeRate, F4INS0 AS HeaderStatus
FROM MVXCDTA.FGINHE
WHERE F4CONO = 100 AND TRIM(F4SUNO) = 'SS015' AND TRIM(F4SINO) = '2405NV2600190';

-- Query 0h: Check ALL FGINAE rows for this invoice (no VTCD filter)
-- Look at F9INIT values to understand which rows are goods vs VAT vs other
SELECT F9SUNO, F9SINO, F9INYR, TRIM(F9PUNO) AS PO, F9PNLI AS POLine,
       F9INIT AS AcctInfoType, F9CDSE AS CostElementSeq,
       TRIM(F9VTCD) AS VatCode, F9CUAM AS ForeignAmt, F9ACAM AS LocalAmt,
       F9ACQT AS Qty, TRIM(F9AIT1) AS GL, TRIM(F9CUCD) AS Currency,
       TRIM(F9CEID) AS CostElement
FROM MVXCDTA.FGINAE
WHERE F9CONO = 100 AND TRIM(F9SUNO) = 'SS015' AND TRIM(F9SINO) = '2405NV2600190'
ORDER BY F9PUNO, F9PNLI, F9INIT, F9CDSE;

-- Query 0e: Full candidate query — the actual SQL we plan to use in C#
-- This is the complete FGINLI + FGINAE + MPLINE + MITMAS join
SELECT
    TRIM(li.F5SUNO) AS SupplierId,
    TRIM(li.F5SINO) AS SupplierInvoiceNo,
    li.F5INYR AS InvoiceYear,
    ROW_NUMBER() OVER (
        PARTITION BY li.F5SUNO, li.F5SINO, li.F5INYR
        ORDER BY li.F5PUNO, li.F5PNLI
    ) AS LineNumber,
    TRIM(COALESCE(po.IBITNO, '')) AS ItemNumber,
    TRIM(COALESCE(po.IBPITD, 'Purchase Line')) AS Description,
    COALESCE(TRIM(im.ITCL), '000') AS ClassificationCode,
    li.F5IVQT AS Quantity,
    COALESCE(TRIM(po.IBPUUN), 'EA') AS UnitOfMeasure,
    CASE WHEN li.F5IVQT <> 0
         THEN li.F5IVNA / li.F5IVQT
         ELSE li.F5IVOC
    END AS UnitPrice,
    li.F5IVNA AS LineTotal,
    COALESCE(TRIM(li.F5VTCD), '') AS TaxCode,
    COALESCE(vat.VatAmount, 0) AS TaxAmount
FROM MVXCDTA.FGINLI li
LEFT JOIN MVXCDTA.MPLINE po
    ON li.F5CONO = po.IBCONO
    AND li.F5PUNO = po.IBPUNO
    AND li.F5PNLI = po.IBPNLI
LEFT JOIN MVXCDTA.MITMAS im
    ON po.IBITNO = im.ITNO
LEFT JOIN LATERAL (
    SELECT SUM(ae.F9CUAM) AS VatAmount
    FROM MVXCDTA.FGINAE ae
    WHERE ae.F9CONO = li.F5CONO
      AND ae.F9SUNO = li.F5SUNO
      AND ae.F9SINO = li.F5SINO
      AND ae.F9INYR = li.F5INYR
      AND ae.F9PUNO = li.F5PUNO
      AND ae.F9PNLI = li.F5PNLI
      AND TRIM(ae.F9VTCD) <> ''
) vat ON 1=1
WHERE li.F5CONO = 100
  AND li.F5DIVI = 'L'
  AND TRIM(li.F5SUNO) = '<SUPPLIER>'
  AND TRIM(li.F5SINO) = '<INVOICE>'
ORDER BY li.F5SUNO, li.F5SINO, li.F5PUNO, li.F5PNLI;

-- =============================================================================
-- ADDITIONAL INVESTIGATION QUERIES (added after initial findings)
-- =============================================================================

-- Query 0i: Check FGINLC (line charges) and FGINHC (header charges)
-- The difference between FGINHE total (1390.50) and FGINAE sum (1287.50) = 103.00
-- might be explained by charges
SELECT 'FGINLC (line charges)' AS Source,
       TRIM(F6SUNO) AS Supplier, TRIM(F6SINO) AS Invoice, F6INYR AS Year,
       TRIM(F6PUNO) AS PO, F6PNLI AS POLine,
       F6IVNA AS InvoicedNetAmt, TRIM(F6VTCD) AS VatCode,
       TRIM(F6EXTY) AS ChargeType, TRIM(F6CEID) AS CostElement
FROM MVXCDTA.FGINLC
WHERE F6CONO = 100 AND TRIM(F6SUNO) = 'SS015' AND TRIM(F6SINO) = '2405NV2600190'

UNION ALL

SELECT 'FGINHC (header charges)' AS Source,
       TRIM(F7SUNO), TRIM(F7SINO), F7INYR,
       '' AS PO, 0 AS POLine,
       F7IEVA AS ChargeAmount, TRIM(F7VTCD) AS VatCode,
       TRIM(F7EXTY) AS ChargeType, TRIM(F7CEID) AS CostElement
FROM MVXCDTA.FGINHC
WHERE F7CONO = 100 AND TRIM(F7SUNO) = 'SS015' AND TRIM(F7SINO) = '2405NV2600190';

-- Query 0j: Find AP invoices that ACTUALLY have VAT (EPVTAM > 0)
-- CONFIRMED 2026-05-14: EPTRCD=10 (supplier invoices) returns zero rows with EPVTAM > 0.
-- All standard AP purchases at Scanfil APAC are zero-rated for Malaysian SST.
-- EPTRCD=40 (adjustments) does return rows with EPVTAM > 0, but FGINAE contains
-- no rows for those invoices — they are posted outside the FGINLI/FGINAE structure.
-- CONCLUSION: TaxAmount = 0 for all EPTRCD=10 AP lines. The FGINAE lateral join
-- has been removed from BuildApLineItemsSql and replaced with a hardcoded 0.
SELECT
    TRIM(EPSUNO) AS Supplier,
    TRIM(EPSINO) AS InvoiceNo,
    EPYEA4 AS Year,
    EPCUAM AS InvoiceAmount,
    EPVTAM AS VatTotal,
    TRIM(EPCUCD) AS Currency
FROM MVXCDTA.FPLEDG
WHERE EPCONO = 100
  AND EPDIVI = 'L'
  AND EPTRCD = 10
  AND EPVTAM > 0
ORDER BY EPACDT DESC
FETCH FIRST 10 ROWS ONLY;
