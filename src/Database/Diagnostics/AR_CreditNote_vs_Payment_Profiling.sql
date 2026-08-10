-- ============================================================================
-- AR Credit Note vs Payment Profiling
-- ============================================================================
-- PURPOSE
--   The business reported that some AR "credit notes" (FSLEDG.ESTRCD = 20) are
--   really payment/settlement postings and should be treated the way AP treats
--   payments (i.e. never submitted to LHDN), while GENUINE commercial credit
--   notes must still be submitted as LHDN document type "02".
--
--   These queries profile the ESTRCD=20 population to find a reliable
--   discriminator between the two kinds.
--
-- CONTEXT
--   AP is filtered to eptrcd = 40 (invoices only); eptrcd = 50 (payments) is
--   never queried. AP credit notes are eptrcd=40 rows identified by amount sign.
--
-- OUTCOME
--   See FINDINGS at the foot of this file. Result: ESTRCD=20 is AR's settlement
--   code, not a credit-note code. Implemented as an ESTRCD='10' source filter
--   in DirectQueryDataSource - see ADR-019.
--
-- USAGE
--   Set CONO to the target company before running:
--     CONO = 100  -> Production
--     CONO = 300  -> Development / UAT
--   Replace {SCHEMA} with the DB2 schema.
--   Adjust the ESRGDT window to a period the business can comment on.
--
-- SAFETY: All statements are read-only SELECTs.
-- ============================================================================


-- ----------------------------------------------------------------------------
-- Q1. Population overview - how many standalone ESTRCD=20 rows are there?
--     "Standalone" = no ESTRCD=10 row shares the same ESCINO.
-- ----------------------------------------------------------------------------
SELECT
    f.ESTRCD                              AS TransCode,
    COUNT(*)                              AS RowCount,
    SUM(CASE WHEN f.ESCUAM > 0 THEN 1 ELSE 0 END) AS PositiveAmounts,
    SUM(CASE WHEN f.ESCUAM < 0 THEN 1 ELSE 0 END) AS NegativeAmounts
FROM {SCHEMA}.FSLEDG f
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
  AND f.ESCUAM <> 0
  AND f.ESTRCD = '20'
  AND NOT EXISTS (
        SELECT 1
        FROM {SCHEMA}.FSLEDG f10
        WHERE f10.ESCONO = f.ESCONO
          AND f10.ESDIVI = f.ESDIVI
          AND TRIM(f10.ESCINO) = TRIM(f.ESCINO)
          AND f10.ESTRCD = '10'
      )
GROUP BY f.ESTRCD;


-- ----------------------------------------------------------------------------
-- Q2. Does the row have invoice line items?
--     Hypothesis: a genuine credit note joins through to OINVOH/ODLINE and has
--     line items; a payment/settlement posting does not.
-- ----------------------------------------------------------------------------
SELECT
    TRIM(f.ESCINO)                        AS InvoiceNo,
    f.ESRGDT                              AS RegDate,
    TRIM(f.ESCUNO)                        AS CustomerNo,
    f.ESVONO                              AS VoucherNo,
    f.ESYEA4                              AS VoucherYear,
    CASE WHEN oh.UHVONO IS NULL THEN 'NO'  ELSE 'YES' END AS HasInvoiceHeader,
    COALESCE(dl.LineCount, 0)             AS LineItemCount
FROM {SCHEMA}.FSLEDG f
LEFT JOIN {SCHEMA}.OINVOH oh
       ON oh.UHCONO = f.ESCONO
      AND oh.UHVONO = f.ESVONO
      AND oh.UHYEA4 = f.ESYEA4
LEFT JOIN (
        SELECT UBCONO, UBIVNO, COUNT(*) AS LineCount
        FROM {SCHEMA}.ODLINE
        GROUP BY UBCONO, UBIVNO
     ) dl
       ON dl.UBCONO = oh.UHCONO
      AND dl.UBIVNO = oh.UHIVNO
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
  AND f.ESCUAM <> 0
  AND f.ESTRCD = '20'
  AND NOT EXISTS (
        SELECT 1
        FROM {SCHEMA}.FSLEDG f10
        WHERE f10.ESCONO = f.ESCONO
          AND f10.ESDIVI = f.ESDIVI
          AND TRIM(f10.ESCINO) = TRIM(f.ESCINO)
          AND f10.ESTRCD = '10'
      )
ORDER BY LineItemCount, f.ESRGDT DESC
FETCH FIRST 200 ROWS ONLY;


-- ----------------------------------------------------------------------------
-- Q3. Summary of Q2 - how cleanly does line-item presence split the population?
-- ----------------------------------------------------------------------------
SELECT
    CASE WHEN COALESCE(dl.LineCount, 0) = 0
         THEN 'NO LINE ITEMS (likely payment)'
         ELSE 'HAS LINE ITEMS (likely genuine CN)' END AS Bucket,
    COUNT(*)          AS RowCount
FROM {SCHEMA}.FSLEDG f
LEFT JOIN {SCHEMA}.OINVOH oh
       ON oh.UHCONO = f.ESCONO
      AND oh.UHVONO = f.ESVONO
      AND oh.UHYEA4 = f.ESYEA4
LEFT JOIN (
        SELECT UBCONO, UBIVNO, COUNT(*) AS LineCount
        FROM {SCHEMA}.ODLINE
        GROUP BY UBCONO, UBIVNO
     ) dl
       ON dl.UBCONO = oh.UHCONO
      AND dl.UBIVNO = oh.UHIVNO
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
  AND f.ESCUAM <> 0
  AND f.ESTRCD = '20'
  AND NOT EXISTS (
        SELECT 1
        FROM {SCHEMA}.FSLEDG f10
        WHERE f10.ESCONO = f.ESCONO
          AND f10.ESDIVI = f.ESDIVI
          AND TRIM(f10.ESCINO) = TRIM(f.ESCINO)
          AND f10.ESTRCD = '10'
      )
GROUP BY CASE WHEN COALESCE(dl.LineCount, 0) = 0
              THEN 'NO LINE ITEMS (likely payment)'
              ELSE 'HAS LINE ITEMS (likely genuine CN)' END;


-- ----------------------------------------------------------------------------
-- Q4. What transaction codes exist in FSLEDG for this division/period?
--     AP uses 40=invoice / 50=payment. If AR has a dedicated payment code we
--     are not currently filtering on, it will show up here.
-- ----------------------------------------------------------------------------
SELECT
    TRIM(f.ESTRCD)    AS TransCode,
    COUNT(*)          AS RowCount,
    MIN(f.ESCUAM)     AS MinAmount,
    MAX(f.ESCUAM)     AS MaxAmount,
    SUM(f.ESCUAM)     AS TotalAmount
FROM {SCHEMA}.FSLEDG f
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
GROUP BY TRIM(f.ESTRCD)
ORDER BY RowCount DESC;


-- ----------------------------------------------------------------------------
-- Q5. Transaction codes across ALL divisions and years.
--     Q4 was scoped to one division and period. Genuine credit notes must exist
--     somewhere if Finance issues them - widen the net before excluding
--     ESTRCD=20. Any code other than 10/20 needs investigation.
-- ----------------------------------------------------------------------------
SELECT
    TRIM(f.ESDIVI)    AS Division,
    TRIM(f.ESTRCD)    AS TransCode,
    f.ESYEA4          AS Year,
    COUNT(*)          AS RowCount,
    MIN(f.ESCUAM)     AS MinAmount,
    MAX(f.ESCUAM)     AS MaxAmount,
    SUM(f.ESCUAM)     AS TotalAmount
FROM {SCHEMA}.FSLEDG f
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESYEA4 >= 2024
GROUP BY TRIM(f.ESDIVI), TRIM(f.ESTRCD), f.ESYEA4
ORDER BY Year DESC, Division, TransCode;


-- ----------------------------------------------------------------------------
-- Q6. Pairing integrity check - confirm EVERY ESTRCD=20 row is offset by an
--     ESTRCD=10 row with the same ESCINO, and quantify how exactly they net.
--     UnpairedCount MUST be 0. If it is not, standalone credit notes DO exist
--     and an ESTRCD='10'-only filter would suppress statutory documents.
-- ----------------------------------------------------------------------------
SELECT
    COUNT(*)                                                        AS Total20Rows,
    SUM(CASE WHEN p.PairedAmount IS NULL THEN 1 ELSE 0 END)         AS UnpairedCount,
    SUM(CASE WHEN p.PairedAmount IS NOT NULL
                  AND ABS(f.ESCUAM + p.PairedAmount) < 0.01
             THEN 1 ELSE 0 END)                                     AS ExactlyNettingToZero,
    SUM(CASE WHEN p.PairedAmount IS NOT NULL
                  AND ABS(f.ESCUAM + p.PairedAmount) >= 0.01
             THEN 1 ELSE 0 END)                                     AS PairedButNotNetting
FROM {SCHEMA}.FSLEDG f
LEFT JOIN (
        SELECT ESCONO, ESDIVI, TRIM(ESCINO) AS CINO, SUM(ESCUAM) AS PairedAmount
        FROM {SCHEMA}.FSLEDG
        WHERE ESTRCD = '10'
        GROUP BY ESCONO, ESDIVI, TRIM(ESCINO)
     ) p
       ON p.ESCONO = f.ESCONO
      AND p.ESDIVI = f.ESDIVI
      AND p.CINO   = TRIM(f.ESCINO)
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESTRCD = '20'
  AND f.ESCUAM <> 0
  AND f.ESYEA4 >= 2024;


-- ----------------------------------------------------------------------------
-- Q7. Column discovery - confirm which FSLEDG columns actually exist here.
--     This installation uses non-standard transaction codes, so do not assume
--     the stock M3 column set.
-- ----------------------------------------------------------------------------
SELECT COLUMN_NAME, DATA_TYPE, LENGTH
FROM QSYS2.SYSCOLUMNS
WHERE TABLE_SCHEMA = '{SCHEMA}'
  AND TABLE_NAME   = 'FSLEDG'
ORDER BY COLUMN_NAME;


-- ############################################################################
-- FINDINGS (profiling run 2026-08-10)
--
-- Monetary values are deliberately NOT recorded here. Re-run the queries above
-- against the live system if the figures are needed.
--
--   Q1/Q2/Q3 : EMPTY - there are NO standalone ESTRCD=20 rows. Every single
--              ESTRCD=20 row shares an ESCINO with an ESTRCD=10 row.
--
--   Q4       : Both codes present. The min/max amounts of ESTRCD=20 are the
--              exact negation of ESTRCD=10, and the period sums are equal and
--              opposite in sign - the signature of contra/offset postings, not
--              independent commercial documents.
--
--   Q5       : Only transaction codes 10 and 20 exist - no hidden third code in
--              any division or year sampled (ESYEA4 >= 2024). The mirror-image
--              pattern holds in EVERY division/year combination; in one
--              low-volume division the two codes net to exactly zero.
--              Note code20 rows frequently OUTNUMBER code10 rows (by roughly
--              40% in the two highest-volume division/year combinations).
--              Credit notes cannot outnumber invoices - partial settlements
--              can, because one invoice attracts multiple payment postings.
--
--   Q6       : UnpairedCount = 0 across 646 rows. This is the critical safety
--              assertion: not one standalone ESTRCD=20 row exists. Most pairs
--              net to exactly zero; the remainder are partially-settled
--              invoices.
--
-- ############################################################################
-- VERDICT: ESTRCD=20 is AR's settlement/payment code (AP eptrcd=50 analogue),
--          NOT a credit-note code. Safe to exclude at source via ESTRCD='10'.
--          Implemented in DirectQueryDataSource - see ADR-019.
--
-- OPEN ASSUMPTION: if Finance ever issues a genuine AR credit note, it will not
--          be submitted under the current filter. No such document exists in
--          three years of data, but confirm the business process and revisit
--          ADR-019 if that changes.
-- ############################################################################
