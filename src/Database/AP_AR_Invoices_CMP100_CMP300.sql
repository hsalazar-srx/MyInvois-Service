-- MOVEX AP/AR Invoice Queries (CMP100/CMP300)
-- Source patterns: FPLEDG + FGLEDG (AP ledger + GL), FSLEDG (AR ledger)
-- Note: If FSLEDG column names differ in your environment, adjust the AR select list accordingly.
-- Schema mapping: CMP100 = mvxcdta, CMP300 = mvxc300

-- ==========================================================
-- CMP100 (mvxcdta)
-- ==========================================================

-- Accounts Payable Invoices (CMP100)
SELECT
    p.epsuno AS supplier,
    p.epsino AS invoice_no,
    p.epacdt AS accounting_date,
    p.epvono AS voucher,
    p.epcucd AS currency,
    p.eparat AS fx_rate,
    p.epcuam AS invoice_amount,
    p.epvtam AS gst_amount,
    g.egait1 AS gl_code,
    g.egtcar AS third_curr_fx_rate,
    g.egtcam AS third_curr_amount,
    g.egftco AS from_country,
    g.egbscd AS base_country
FROM mvxcdta.fpledg p
LEFT JOIN mvxcdta.fgledg g
    ON p.epvono = g.egvono
WHERE 1 = 1
-- Optional filters:
-- AND p.epacdt BETWEEN 20240101 AND 20241231
-- AND p.epsuno = 'SUPPLIER_ID'
-- AND p.epsino = 'INVOICE_NO'
ORDER BY p.epacdt, p.epsino;


-- Accounts Receivable Invoices (CMP100) - Ledger Level
SELECT
    s.escuno AS customer,
    s.esivno AS invoice_no,
    s.esivdt AS invoice_date,
    s.esacdt AS accounting_date,
    s.esvono AS voucher,
    s.escucd AS currency,
    s.esarat AS fx_rate,
    s.escuAm AS invoice_amount,
    g.egait1 AS gl_code,
    g.egacam AS recorded_amount,
    g.egcuam AS foreign_amount,
    g.egtcam AS third_curr_amount
FROM mvxcdta.fsledg s
LEFT JOIN mvxcdta.fgledg g
    ON s.esvono = g.egvono
WHERE 1 = 1
-- Optional filters:
-- AND s.esacdt BETWEEN 20240101 AND 20241231
-- AND s.escuno = 'CUSTOMER_ID'
-- AND s.esivno = 'INVOICE_NO'
ORDER BY s.esacdt, s.esivno;

-- If FSLEDG is not available, fallback to sales invoice lines:
-- SELECT * FROM mvxcdta.osbstd WHERE 1 = 1 AND ucivdt BETWEEN 20240101 AND 20241231;


-- ==========================================================
-- CMP300 (mvxc300)
-- ==========================================================

-- Accounts Payable Invoices (CMP300)
SELECT
    p.epsuno AS supplier,
    p.epsino AS invoice_no,
    p.epacdt AS accounting_date,
    p.epvono AS voucher,
    p.epcucd AS currency,
    p.eparat AS fx_rate,
    p.epcuam AS invoice_amount,
    p.epvtam AS gst_amount,
    g.egait1 AS gl_code,
    g.egtcar AS third_curr_fx_rate,
    g.egtcam AS third_curr_amount,
    g.egftco AS from_country,
    g.egbscd AS base_country
FROM mvxc300.fpledg p
LEFT JOIN mvxc300.fgledg g
    ON p.epvono = g.egvono
WHERE 1 = 1
-- Optional filters:
-- AND p.epacdt BETWEEN 20240101 AND 20241231
-- AND p.epsuno = 'SUPPLIER_ID'
-- AND p.epsino = 'INVOICE_NO'
ORDER BY p.epacdt, p.epsino;


-- Accounts Receivable Invoices (CMP300) - Ledger Level
SELECT
    s.escuno AS customer,
    s.esivno AS invoice_no,
    s.esivdt AS invoice_date,
    s.esacdt AS accounting_date,
    s.esvono AS voucher,
    s.escucd AS currency,
    s.esarat AS fx_rate,
    s.escuAm AS invoice_amount,
    g.egait1 AS gl_code,
    g.egacam AS recorded_amount,
    g.egcuam AS foreign_amount,
    g.egtcam AS third_curr_amount
FROM mvxc300.fsledg s
LEFT JOIN mvxc300.fgledg g
    ON s.esvono = g.egvono
WHERE 1 = 1
-- Optional filters:
-- AND s.esacdt BETWEEN 20240101 AND 20241231
-- AND s.escuno = 'CUSTOMER_ID'
-- AND s.esivno = 'INVOICE_NO'
ORDER BY s.esacdt, s.esivno;

-- If FSLEDG is not available, fallback to sales invoice lines:
-- SELECT * FROM mvxc300.osbstd WHERE 1 = 1 AND ucivdt BETWEEN 20240101 AND 20241231;
