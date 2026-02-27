-- MOVEX Party Data Reference Queries (DB2 on AS/400)
-- Purpose: Reference SQL for future IPartyDataProvider implementation (MovexMasterPartyDataProvider)
-- Status: ADR-013 Known Gap #1 — table structure and field mappings NOT YET CONFIRMED
-- Action: Confirm with DBA team before implementing

-- ==========================================================
-- Supplier Master (CIDMAS) — for AP invoices
-- ==========================================================
-- Fields needed for MyInvois:
--   TIN (Tax Identification Number), BRN (Business Registration Number),
--   Name (legal name), Address (registered address)

-- PLACEHOLDER: Adjust column names after DBA confirmation
-- SELECT
--     idsuno AS supplier_id,
--     idsunm AS supplier_name,
--     -- TIN field: TBD (may be in custom field or separate table)
--     -- BRN field: TBD
--     idadr1 AS address_line_1,
--     idadr2 AS address_line_2,
--     idadr3 AS address_line_3,
--     idpono AS postal_code,
--     idcscd AS country_code
-- FROM {schema}.cidmas
-- WHERE idsuno = @supplierId;


-- ==========================================================
-- Customer Master (OCUSMA) — for AR invoices
-- ==========================================================
-- Fields needed for MyInvois:
--   TIN (Tax Identification Number), BRN (Business Registration Number),
--   Name (legal name), Address (registered address)

-- PLACEHOLDER: Adjust column names after DBA confirmation
-- SELECT
--     okcuno AS customer_id,
--     okcunm AS customer_name,
--     -- TIN field: TBD (may be in custom field or separate table)
--     -- BRN field: TBD
--     okcua1 AS address_line_1,
--     okcua2 AS address_line_2,
--     okcua3 AS address_line_3,
-- FROM {schema}.ocusma
-- WHERE okcuno = @customerId;


-- ==========================================================
-- Notes
-- ==========================================================
-- 1. TIN/BRN fields may not exist in standard CIDMAS/OCUSMA tables
--    → Check if custom fields were added (e.g., user-defined fields in MMS001)
--    → Alternatively, may need a separate lookup table
-- 2. Schema mapping: CMP100 = mvxcdta, CMP300 = mvxc300
-- 3. MyInvois requires: TIN (12 digits), BRN, legal name, full address
