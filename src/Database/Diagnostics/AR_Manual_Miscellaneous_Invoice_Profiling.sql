-- ============================================================================
-- Manual / Miscellaneous Invoice Profiling
-- ============================================================================
-- PURPOSE
--   Finance reported (2026-08-11, after the UAT deployment) that some invalid
--   MANUAL entries relating to MISCELLANEOUS invoices were submitted to MyInvois
--   pre-prod and should not have been.
--
--   This is an eligibility/scope gap, not a signature or data-quality defect:
--   the documents were built and signed correctly, they simply should never
--   have entered the pipeline.
--
--   These queries aim to find the discriminator that separates a manually keyed
--   miscellaneous entry from a system-generated sales invoice.
--
-- STATUS: OPEN - no discriminator identified yet. Do NOT implement a filter
--   until the evidence supports a specific rule.
--
-- METHOD
--   Follows the approach that produced ADR-019: profile the data first, confirm
--   what the codes actually mean, then implement. That investigation overturned
--   a long-standing assumption about ESTRCD=20.
--
-- PREREQUISITE
--   Ask Finance for 2-3 example document numbers that should NOT have been
--   submitted, and 1-2 that legitimately should be. Substitute them into Q3.
--   Without known-bad examples these queries only narrow the search space.
--
-- COMPLIANCE CAUTION
--   Over-exclusion is the dangerous direction. Suppressing documents that are
--   legally reportable is worse than submitting a few that are not. Establish
--   the rule on evidence before applying it.
--
-- USAGE
--   Set CONO (100 = Production, 300 = Dev/UAT) and replace {SCHEMA}.
--
-- SAFETY: All statements are read-only SELECTs.
-- ============================================================================


-- ----------------------------------------------------------------------------
-- Q1. Column discovery - what does FSLEDG actually offer as a discriminator?
--     Look for voucher type / voucher series / entry origin / manual flag
--     columns. This installation uses non-standard codes, so do not assume the
--     stock M3 column set.
-- ----------------------------------------------------------------------------
SELECT COLUMN_NAME, DATA_TYPE, LENGTH
FROM QSYS2.SYSCOLUMNS
WHERE TABLE_SCHEMA = '{SCHEMA}'
  AND TABLE_NAME   = 'FSLEDG'
ORDER BY COLUMN_NAME;


-- ----------------------------------------------------------------------------
-- Q2. Do submitted AR invoices link to a customer order?
--     Hypothesis: a system-generated sales invoice traces back through
--     OINVOH/ODLINE to an order; a manual miscellaneous entry does not.
--     If this splits cleanly it is a strong candidate rule - and it reuses the
--     join path the line-item fetcher already relies on.
-- ----------------------------------------------------------------------------
SELECT
    CASE WHEN oh.UHVONO IS NULL       THEN 'NO INVOICE HEADER (candidate: manual)'
         WHEN COALESCE(dl.LineCount,0) = 0 THEN 'HEADER BUT NO LINES (candidate: manual)'
         ELSE 'HAS ORDER LINES (system-generated)' END AS Bucket,
    COUNT(*) AS RowCount
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
  AND f.ESTRCD = '10'         -- only what we actually submit (ADR-019)
  AND f.ESCUAM <> 0
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
GROUP BY
    CASE WHEN oh.UHVONO IS NULL       THEN 'NO INVOICE HEADER (candidate: manual)'
         WHEN COALESCE(dl.LineCount,0) = 0 THEN 'HEADER BUT NO LINES (candidate: manual)'
         ELSE 'HAS ORDER LINES (system-generated)' END;


-- ----------------------------------------------------------------------------
-- Q3. Side-by-side comparison of known-bad vs known-good documents.
--     THIS IS THE KEY QUERY once Finance supplies examples.
--     Substitute the document numbers below, then compare every column looking
--     for the one that separates the two groups.
-- ----------------------------------------------------------------------------
SELECT
    TRIM(f.ESCINO)    AS InvoiceNo,
    TRIM(f.ESTRCD)    AS TransCode,
    TRIM(f.ESDIVI)    AS Division,
    f.ESRGDT          AS RegDate,
    f.ESYEA4          AS Year,
    f.ESVONO          AS VoucherNo,
    TRIM(f.ESCUNO)    AS CustomerNo,
    TRIM(f.ESCUCD)    AS Currency,
    CASE WHEN oh.UHVONO IS NULL THEN 'NO' ELSE 'YES' END AS HasInvoiceHeader,
    COALESCE(dl.LineCount, 0) AS LineItemCount
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
  AND TRIM(f.ESCINO) IN (
        -- <<< known-BAD (should not have been submitted)
        'REPLACE_ME_BAD_1',
        'REPLACE_ME_BAD_2',
        -- <<< known-GOOD (correctly submitted)
        'REPLACE_ME_GOOD_1'
      )
ORDER BY InvoiceNo;


-- ----------------------------------------------------------------------------
-- Q4. Invoice-number pattern check.
--     Manual entries often occupy a distinct number series or carry a prefix.
--     Group by leading characters to see whether a series is distinguishable.
-- ----------------------------------------------------------------------------
SELECT
    SUBSTR(TRIM(f.ESCINO), 1, 3) AS NumberPrefix,
    COUNT(*)                     AS RowCount,
    MIN(TRIM(f.ESCINO))          AS ExampleLow,
    MAX(TRIM(f.ESCINO))          AS ExampleHigh
FROM {SCHEMA}.FSLEDG f
WHERE f.ESCONO = 100          -- <<< set company
  AND f.ESDIVI = 'L'
  AND f.ESTRCD = '10'
  AND f.ESCUAM <> 0
  AND f.ESRGDT BETWEEN 20260101 AND 20261231
GROUP BY SUBSTR(TRIM(f.ESCINO), 1, 3)
ORDER BY RowCount DESC;


-- ############################################################################
-- FINDINGS
--
--   (Not yet run - awaiting example document numbers from Finance.)
--
--   Record results here in the same style as
--   AR_CreditNote_vs_Payment_Profiling.sql: row counts, structural findings and
--   proportions only. Do NOT record monetary values, customer identifiers or
--   UUIDs in this file.
--
-- ############################################################################
