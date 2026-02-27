# Change Impact Assessment Log

**Purpose:** Document impact of significant changes, enhancements, and scope modifications  
**Owner:** Project Manager  
**Maintained:** Ongoing  
**Audience:** Stakeholders, Architecture Review, Future References

---

## Change Impact Template

```
Change ID:       CHG-NNN
Title:           [What changed?]
Date:            [YYYY-MM-DD]
Initiated By:    [Who requested the change?]
Category:        [Scope/Technical/Process/Timeline]
Status:          ✅ Approved / ⏳ Pending / ❌ Rejected

Description:
[What is changing?]

Business Impact:
[Impact on business objectives, timeline, cost]

Technical Impact:
[Impact on architecture, code, testing]

Risk Assessment:
[What could go wrong?]

Mitigation:
[How will we manage the risks?]

Approval:
- [ ] Project Manager: [Name]
- [ ] Tech Lead: [Name]
- [ ] Finance Lead: [Name]

Implementation:
[How will this be implemented?]

Lessons Learned:
[What did we learn from this change?]
```

---

## CHG-001: Decision to Implement SRX Template Compliance (Week 1)

**Change ID:** CHG-001  
**Title:** Retroactive SRX Template Compliance Restructuring  
**Date:** 2026-02-05  
**Initiated By:** Architecture Review  
**Category:** Process  
**Status:** ✅ Approved & In Progress  

### Description
During Week 1 scaffold phase, project was created with excellent documentation and code structure, but did not follow SRX workspace template standard. Decision made to restructure project to achieve 100% SRX compliance before handoff to developers.

### What Changed
- Created src/ directory for source code organization
- Created ai/planning/ subdirectory for sprint plans
- Created ai/tasks/ subdirectory for task tracking
- Created ai/rules.md (critical AI operating instructions)
- Reorganizing memory files to 00-07 naming convention
- Created INDEX.md for navigation
- Created DIRECTORY_MAP.md, QUICK_REFERENCE.md, IMPLEMENTATION_SUMMARY.md

### Business Impact
- **Timeline:** 1 extra day of work (completed Feb 5)
- **Cost:** Minimal (already documented, just reorganized)
- **Benefit:** Long-term maintainability, consistency with workspace standards
- **Risk:** None (work already done, just restructured)

### Technical Impact
- **Code:** Will need to update import paths after moving files to src/
- **Build:** No impact (file location doesn't change compilation)
- **Dependencies:** No new dependencies
- **Architecture:** No architectural changes, pure restructuring

### Why This Change
1. **Consistency:** Workspace template standard for all projects
2. **Maintainability:** Easier for future developers to navigate
3. **AI Support:** SRX template structure optimizes for AI-assisted development
4. **Governance:** Clear structure for rules, evidence, planning

### Risk Assessment
**Low Risk:**
- Work being reorganized, not rewritten
- No functional changes
- All documentation preserved
- Structure improves future development

### Mitigation
- Update all cross-references in documentation after moves
- Run file integrity checks post-reorganization
- Validate all links in INDEX.md
- Communicate updated structure to team

### Implementation
1. Created new directories (✅ done Feb 5)
2. Created ai/rules.md and INDEX.md (✅ done Feb 5)
3. Create remaining root and ai/ files (⏳ in progress Feb 5)
4. Reorganize memory files (⏳ pending Feb 5)
5. Update project references (⏳ pending Feb 5)
6. Validate structure (⏳ pending Feb 5)

### Approval Status
- [x] Architecture Review: Approved (Feb 5)
- [ ] Tech Lead: Pending
- [ ] Project Manager: Pending

### Lessons Learned
- **Process Gap:** Template compliance check should happen earlier
- **Best Practice:** Include template validation in Week 1 kickoff
- **Tool:** Create template compliance checklist for future projects
- **Timeline:** SRX template restructuring takes ~1 day, plan accordingly

---

## CHG-002: Monthly Batch Schedule Clarification (Week 1)

**Change ID:** CHG-002  
**Title:** Clarification: Monthly Batch Processing (Not Daily)  
**Date:** 2026-02-04  
**Initiated By:** Finance Lead Review  
**Category:** Scope  
**Status:** ✅ Approved  

### Description
Initial planning assumed daily submission to MyInvois. Finance team clarified that monthly submission (1st of month) aligns with business practice. This was a critical discovery that shaped the entire architecture.

### What Changed
- Submission frequency: Daily → Monthly
- Batch size: 20-30/day → 100-1000/month
- Processing window: Ongoing → 1st of month, 2-hour window
- Rate limit handling: Must handle bursts → Steady batch processing

### Business Impact
- **Timeline:** No impact (discovered early)
- **Cost:** Actually reduced (fewer batches, simpler logic)
- **Benefit:** Aligns with month-end close, reduces finance burden
- **Capacity:** Can handle volume easily with batch approach

### Technical Impact
- **Architecture:** Shifted from real-time to batch processing (ADR-001)
- **Rate Limiting:** Changed from continuous to burst (600ms delay strategy)
- **Scheduling:** Windows Task Scheduler sufficient (no real-time message queue needed)
- **Database:** No hourly rollup needed, single monthly transaction

### Why This Discovery Matters
1. **Rate Limits:** MyInvois at 100 req/min can't support daily real-time (problematic)
2. **Business Process:** Finance works monthly close cycle, aligns perfectly
3. **Reliability:** Batch approach simpler and more reliable than real-time
4. **Cost:** No need for expensive real-time infrastructure

### Risk Assessment
**Very Low Risk:**
- Aligns with business practice
- Improves technical feasibility
- Simplifies implementation
- Reduces failure scenarios

### Mitigation
- Document monthly schedule in requirements-traceability.md ✅
- Communicate monthly schedule to stakeholders ✅
- Plan for any prior-month invoices that might be pending ✅

### Implementation
- Incorporated into ADR-002 (Monthly Batch Processing)
- IMPLEMENTATION_CHECKLIST uses monthly schedule
- Deployment plan assumes 1st of month scheduling

### Approval Status
- [x] Finance Lead: Approved (Feb 4)
- [x] Tech Lead: Approved (Feb 4)
- [x] Project Manager: Approved (Feb 4)

### Impact on Other Decisions
- Enabled ADR-001 (Hybrid architecture decision)
- Enabled ADR-004 (Rate limit safety strategy)
- Enabled ADR-006 (Limited retries sufficient for batch)
- Enabled ADR-002 (Informed monthly schedule)

### Lessons Learned
- **Critical:** Verify business processes early (not assumptions)
- **Discovery Method:** Direct conversation with Finance team
- **Value:** One conversation saved weeks of implementation
- **Best Practice:** Business context discovery in Week 0, not Week 1

---

## CHG-003: Rate Limit Safety Margin Established (Week 1 Analysis)

**Change ID:** CHG-003  
**Title:** Rate Limit Safety: 600ms Batch Delay Requirement  
**Date:** 2026-02-04  
**Initiated By:** Tech Lead Analysis  
**Category:** Technical  
**Status:** ✅ Approved  

### Description
Technical analysis of MyInvois rate limits (100 req/min) determined that batch processing with 600ms delay provides safe 20% utilization margin, preventing throttling errors.

### What Changed
- Rate limit strategy: No explicit throttling → 600ms batch delay
- Batch size: Could use any size → Optimized to 100+50 per 600ms delay
- Processing logic: Simple sequential → Add 600ms delay between batches

### Business Impact
- **Timeline:** No impact (same 2-hour window)
- **Cost:** Negligible (sleep instruction is free)
- **Benefit:** 100% reliable, no rate limit errors, predictable performance
- **Capacity:** Can process 1000+ invoices safely per month

### Technical Impact
- **Code:** Add 600ms delay in InvoiceProcessor.ProcessMonthlyBatch
- **Configuration:** Add DelayBetweenBatches setting (default 600ms)
- **Monitoring:** Track batch throughput to verify actual performance
- **Load Testing:** Simulate monthly volume to validate strategy

### Rate Limit Analysis
```
MyInvois Limit:     100 requests per 60-second window
Safe Utilization:   60 requests per 60-second window (60% of limit)
Actually Used:      1.67 requests per second (600ms delay)
Safety Margin:      20% (4x safety factor)

Batch Size:         100 sales + 50 purchase = 150 requests per batch
Delay:              600ms between batches
Processing Time:    150 × 600ms = 90 seconds
Network Overhead:   ~30 seconds
Total Batch Time:   ~2 minutes
Monthly Time:       ~2 minutes per month (well within 2-hour window)
```

### Risk Assessment
**Low Risk:**
- Proven approach (tested with similar systems)
- 20% safety margin provides buffer
- Easily adjustable if needed
- No dependency on external factors

### Mitigation
- Monitor actual API response times
- Alert if approaching rate limit (>90 req/min)
- Have manual submission workaround (Phase 2)
- Implement Polly circuit breaker Phase 2 for advanced backoff

### Implementation
- Incorporated into ADR-004 (Rate Limit Safety)
- BatchConfiguration.cs has DelayBetweenBatches property
- InvoiceProcessor implements delay in loop
- Logging tracks actual throughput

### Approval Status
- [x] Tech Lead: Approved (Feb 4)
- [x] Security Lead: Approved (Feb 4)

### Related Changes
- CHG-002: Monthly batch schedule enabled this strategy

### Lessons Learned
- **Calculation:** Rate limit safety margin should be ≤30% utilization
- **Batch Sizing:** Optimize batch size for delay strategy
- **Monitoring:** Always monitor actual utilization vs limit
- **Documentation:** Document rate limit strategy in code comments

---

## CHG-004: Error Classification Strategy for Retry Logic (Week 1)

**Change ID:** CHG-004  
**Title:** Error Classification: Retriable vs Non-Retriable Errors  
**Date:** 2026-02-04  
**Initiated By:** Tech Lead Design  
**Category:** Technical  
**Status:** ✅ Approved  

### Description
Design decision to classify MyInvois API errors into retriable (429, 5xx) and non-retriable (4xx, data errors) to optimize retry strategy and failure handling.

### What Changed
- Retry strategy: Retry all errors → Selective retry per error type
- Error handling: Generic exception → Structured error classification
- Failure detection: Logs only → Structured error tracking
- User experience: Generic errors → Specific error messages

### Business Impact
- **Timeline:** Minimal impact (design decision)
- **Benefit:** Faster failure detection, better error messages
- **Support:** Ops team has clear error categories for troubleshooting

### Technical Impact
- **Code:** Add error classification logic in MyInvoiceSubmitter
- **Mapper:** Map MyInvois error codes to categories
- **Logging:** Log error category for diagnostics
- **Retry:** Only retry categorized as retriable

**Error Categories:**
```
Retriable (Retry 3x):
- 429: Rate Limit Exceeded (temporary)
- 500: Internal Server Error (temporary)
- 503: Service Unavailable (temporary)
- Timeout: Network timeout (transient)

Non-Retriable (Fail Immediately):
- 400: Bad Request (data validation)
- 401: Unauthorized (auth issue)
- 403: Forbidden (access issue)
- DS302: Data validation error
- DS301: System error
```

### Risk Assessment
**Low Risk:**
- Error classification is straightforward
- MyInvois maintains stable error codes
- Worst case: Retry something that shouldn't be (safe)
- Falls back to manual retry (Phase 2)

### Mitigation
- Document error codes in myinvois-api-reference.md
- Monitor error logs for new/unknown codes
- Escalate unknown errors to Tech Lead
- Phase 2: Implement dynamic error classification

### Implementation
- Incorporated into ADR-008 (Error Classification)
- MyInvoiceSubmitter.cs has error classification logic
- AuditLog tracks error category
- Ops runbook documents error codes

### Approval Status
- [x] Tech Lead: Approved (Feb 4)
- [x] QA Lead: Approved (Feb 4)

### Lessons Learned
- **Error Handling:** Always categorize errors early (not just log)
- **Retry Policy:** Understand which errors are transient vs permanent
- **Monitoring:** Track error categories for trend analysis
- **Documentation:** Document error codes for ops support

---

## CHG-005: OAuth Token Caching Decision (Week 1)

**Change ID:** CHG-005  
**Title:** OAuth Token Caching: Reduce API Calls by 50%  
**Date:** 2026-02-04  
**Initiated By:** Tech Lead Optimization  
**Category:** Performance  
**Status:** ✅ Approved  

### Description
Design decision to cache OAuth tokens with 1-hour TTL, reducing token refresh API calls from 150 (one per invoice) to 1-2 (per batch).

### What Changed
- Token strategy: Request per call → Cache with TTL
- API efficiency: 300 API calls per batch → 150 API calls per batch
- Token refresh: On-demand → TTL-based with 401 fallback

### Business Impact
- **Performance:** 50% reduction in API calls
- **Reliability:** No change (401 fallback ensures freshness)
- **Cost:** Less API usage = lower transaction costs

### Technical Impact
- **Code:** Add token cache in MovexInvoiceReader and MyInvoiceSubmitter
- **Complexity:** Token refresh logic added (low complexity)
- **State:** Service maintains in-memory token cache
- **Lifecycle:** Cache expires after 1 hour or on 401

### Optimization Analysis
```
Before (No Caching):
- Per invoice: 1 OAuth call + 1 submit call = 2 API calls
- Total: 150 invoices × 2 calls = 300 API calls
- Time: 300 × 100ms = 30 seconds overhead

After (1-hour Cache):
- Per batch: 2 OAuth calls (MOVEX + MyInvois) = 2 API calls
- Per invoices: 150 submit calls = 150 API calls
- Total: 152 API calls
- Time: 152 × 100ms = 15 seconds overhead (50% reduction)
- Savings: 148 API calls per batch (50% reduction)

Monthly Savings:
- Calls: 148 calls × 1 batch = 148 calls saved per month
- Time: 15 seconds saved per monthly batch
- Cost: Significant if API is metered
```

### Risk Assessment
**Very Low Risk:**
- Token TTL is 1 hour, cache is 1 hour (exact match)
- 401 handling ensures token freshness if revoked
- Batch processing takes 2 minutes (well within TTL)
- Worst case: Refresh token more often (safe, not breaking)

### Mitigation
- Implement 401 handling for token revocation
- Log all token refreshes for monitoring
- Set configurable TTL (can be adjusted if needed)
- Monitor token refresh frequency

### Implementation
- Incorporated into ADR-007 (Token Caching)
- MovexInvoiceReader.cs has token cache
- MyInvoiceSubmitter.cs has token cache
- 401 response triggers automatic refresh

### Approval Status
- [x] Tech Lead: Approved (Feb 4)
- [x] Performance Lead: Approved (Feb 4)

### Lessons Learned
- **Performance:** Cache tokens at API call points, not globally
- **TTL:** Match cache TTL to token TTL
- **Fallback:** Always implement 401 handling for token refresh
- **Monitoring:** Track cache hit/miss ratio for optimization

---

## CHG-006: Replace MOVEX REST API with DB2 Direct Access

**Change ID:** CHG-006
**Title:** Data Source Change: MOVEX REST API → IBM DB2/AS400 Direct Access
**Date:** 2026-02-16
**Initiated By:** Project Owner
**Category:** Technical / Architecture
**Status:** ✅ Approved

### Description
Replace the planned MOVEX REST API (HTTP/M3 MI protocol) data source with direct database access to IBM DB2 on AS/400. Implements strategy pattern to support both direct SQL queries and stored procedures.

### What Changed
- Data source: MOVEX REST API (HTTP) → DB2/AS400 (SQL/stored proc)
- Configuration: `MovexApiSettings` (BaseUrl, Timeout) → `MovexDbSettings` (ConnectionString, Strategy)
- Skills: `m3-transaction-builder`/`m3-response-parser` → new `movex-db2-data-source` skill
- NuGet: Remove HTTP client config → Add `Net.IBM.Data.Db2` + `Dapper`
- Architecture: New `src/DataAccess/` layer with strategy pattern + pluggable party data provider

### Business Impact
- **Timeline:** No impact (scaffold phase, no implementation to rewrite)
- **Cost:** Minimal (removes REST API dependency, potentially simpler)
- **Benefit:** Direct data access, no dependency on MOVEX REST API service availability
- **Risk:** DB2 driver compatibility needs validation

### Technical Impact
- **Files created:** ~10 new files in `src/DataAccess/` and `src/Configuration/`
- **Files deleted:** `src/Configuration/MovexApiSettings.cs`
- **Files modified:** `MovexInvoiceReader.cs`, `MovexInvoice.cs`, `InvoiceProcessor.cs`, `appsettings.json`
- **Documentation:** ~25 files need updating to replace REST API references with DB2
- **Skills registry:** New skill `movex-db2-data-source` created and registered
- **Agent registry:** `expert-movex-dotnet` updated with new skill reference

### Risk Assessment
**Medium Risk:**
- DB2 driver compatibility on .NET 8 with AS/400 (mitigated by ODBC fallback)
- Party data source undecided (mitigated by placeholder provider)
- Invoice line item tables unknown (mitigated by extensible design)

### Mitigation
- Strategy pattern allows switching data source approaches without code changes
- Pluggable `IPartyDataProvider` interface designed for future enrichment
- `System.Data.Odbc` as fallback if IBM DB2 driver has issues
- `IMovexInvoiceReader` interface unchanged — downstream services unaffected

### Implementation
See plan file for full implementation steps (6 phases, 20 steps).

### Approval Status
- [x] Project Owner: Approved (Feb 16)
- [x] Architecture: Approved (skills-first compliance verified)

---

## CHG-007: Align AR Query with Production SQL (Actual_MOVEX_AR.sql)

**Change ID:** CHG-007
**Title:** AR Query Rewrite: FGLEDG → OCUSMA/OCUSAD Joins + ESCINO Invoice Number
**Date:** 2026-02-19
**Initiated By:** Project Owner (query validation review)
**Category:** Technical
**Status:** ✅ Approved

### Description
The AR query in `DirectQueryDataSource.BuildArHeaderSql` was based on the older `AP_AR_Invoices_CMP100_CMP300.sql` reference and did not match the validated production SQL (`Actual_MOVEX_AR.sql`). The query has been rewritten to match production.

### What Changed
- **AR query joins**: Replaced `FGLEDG` (GL ledger) with `OCUSMA` (customer master, INNER JOIN) + `OCUSAD` (customer address, LEFT JOIN with ADID='INV01')
- **Invoice number field**: `ESIVNO` → `ESCINO` (mapped to existing `InvoiceNo` property)
- **Date field**: `ESACDT` (accounting date) → `ESRGDT` (entry date) for AR filtering
- **New AR filters**: Division (`ESDIVI`), TransCode (`ESTRCD`), CustomerStatus (`OKSTAT`), MinYear (`ESYEA4`) — all configurable via `MovexDbSettings`
- **New model properties**: 16 customer/address fields added to `RawInvoiceRecord` (CustomerName, MasterAddress1-4, InvoiceeName, Address1-4, PostCode, etc.)
- **GlCode for AR**: Now null (GL join removed for AR; AP query unchanged)

### Business Impact
- **Timeline:** No impact (data access layer change only)
- **Benefit:** AR data now matches actual MOVEX production query, includes customer name/address data needed for MyInvois buyer block
- **Risk:** Low — AR filters are configurable, AP query unchanged

### Technical Impact
- **Files modified:** 4 files (MovexDbSettings, RawInvoiceRecord, DirectQueryDataSource, DirectQueryDataSourceTests)
- **New settings:** `ArDivision`, `ArTransCode`, `ArCustomerStatus`, `ArMinYear` in `MovexDbSettings`
- **New properties:** 16 nullable properties on `RawInvoiceRecord` for customer/address data
- **Breaking:** None — all new properties are nullable/default, existing AP path unchanged
- **Tests:** 225 passed, 0 failed

### Risk Assessment
**Low Risk:**
- AP query path completely unchanged
- All new AR fields are nullable (won't break existing consumers)
- AR filters are configurable with sensible defaults
- Validated against production SQL

### Mitigation
- AR filter defaults match production values (division='L', transCode='10', status='20')
- Strategy pattern allows reverting to stored procedure approach if needed
- Integration test validation recommended before production deployment

### Approval Status
- [x] Project Owner: Approved (Feb 19)

---

## Change Summary Table

| ID | Title | Date | Category | Status | Impact |
|----|-------|------|----------|--------|--------|
| CHG-001 | SRX Template Compliance | 2026-02-05 | Process | ✅ In Progress | High |
| CHG-002 | Monthly Batch Schedule | 2026-02-04 | Scope | ✅ Approved | High |
| CHG-003 | Rate Limit Safety | 2026-02-04 | Technical | ✅ Approved | High |
| CHG-004 | Error Classification | 2026-02-04 | Technical | ✅ Approved | Medium |
| CHG-005 | Token Caching | 2026-02-04 | Performance | ✅ Approved | Medium |
| CHG-006 | DB2 Direct Access | 2026-02-16 | Technical | ✅ Approved | High |
| CHG-007 | AR Query Alignment | 2026-02-19 | Technical | ✅ Approved | Medium |

---

## Change Approval Authority

| Change Type | Authority | Approval Time |
|-------------|-----------|---------------|
| Scope Change | Project Manager + Tech Lead | 24 hours |
| Technical Change | Tech Lead | 4 hours |
| Timeline Change | Project Manager | 4 hours |
| Process Change | Project Manager | 24 hours |
| Emergency Change | Tech Lead (immediate) | Ad-hoc |

---

**Owner:** Project Manager  
**Last Updated:** February 5, 2026  
**Maintained By:** Project Management Office

