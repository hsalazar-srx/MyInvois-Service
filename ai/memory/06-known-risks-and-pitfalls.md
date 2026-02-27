# Known Risks and Pitfalls - MyInvois-Service

**Last Updated:** February 5, 2026  
**Status:** Active  
**Owner:** Solution Architect + Operations Manager

---

## Risk Register

### Risk [R001]: MyInvois API Outage During Batch Window
- **Date Identified**: 2026-02-05
- **Risk Description**: MyInvois government system may be unavailable during batch submission (6 AM - 10 PM window)
- **Impact on Plant Operations**: Finance team cannot submit invoices; potential tax filing delays
- **Likelihood**: Medium (government systems have occasional maintenance)
- **Severity**: High (tax compliance risk)
- **Mitigation Strategy**:
  - Queue all failed submissions for retry
  - Implement exponential backoff (5s, 10s, 20s, etc.)
  - Finance team receives SMS/email alert on all failures
  - Retry window: 48 hours (until next business day)
  - Finance manager authorized to manually submit via MyInvois portal
- **Owner**: IT Manager
- **Status**: Mitigated (has retry logic)

### Risk [R002]: M3/MOVEX Database Unreachable During Batch Processing
- **Date Identified**: 2026-02-05
- **Risk Description**: MOVEX database (DB2 on AS/400) may be offline/unreachable when batch tries to fetch invoices
- **Impact on Plant Operations**: Monthly batch fails; finance team must manually extract invoices
- **Likelihood**: Low (MOVEX is production system, well-maintained)
- **Severity**: High (batch cannot complete)
- **Mitigation Strategy**:
  - Set DB2 connection timeout to 30 seconds (workspace standard)
  - Implement 3-attempt retry with 5-second delays
  - Log all connection failures in audit trail
  - Alert Operations Manager on all failures
  - Finance team can manually trigger batch after MOVEX DB2 recovers
  - Keep fallback: CSV import from MOVEX export (manual)
- **Owner**: Integration Manager
- **Status**: Mitigated (has fallback)

### Risk [R003]: Invoice Data Quality from M3
- **Date Identified**: 2026-02-05
- **Risk Description**: MOVEX database may contain incomplete/invalid invoices (missing fields, wrong types)
- **Impact on Plant Operations**: Batch fails validation; unprocessed invoices; finance team must manually fix data
- **Likelihood**: High (ERP data is often messy)
- **Severity**: Medium (recoverable by fixing data)
- **Mitigation Strategy**:
  - Validate all 20+ mandatory fields before submission
  - Collect validation errors (don't fail-fast)
  - Generate detailed error report for finance team
  - Email error report with actionable next steps
  - Provide "fix & resubmit" interface (Phase 2)
  - Document common data issues in runbooks
- **Owner**: QA Lead
- **Status**: Mitigated (has validation + reporting)

### Risk [R004]: Duplicate Invoice Submission
- **Date Identified**: 2026-02-05
- **Risk Description**: Same invoice submitted twice (network retry, manual rerun, etc.)
- **Impact on Plant Operations**: MyInvois rejects duplicate; invoice not recognized by tax authority; compliance gap
- **Likelihood**: Medium (network is unreliable, retries happen)
- **Severity**: High (compliance & audit risk)
- **Mitigation Strategy**:
  - Use invoice ID (INVOICENO) as unique key in audit database
  - Check for duplicates before MyInvois submission
  - Track MyInvois UUID (return value) in audit log
  - Never submit same invoice twice without explicit override
  - Log all submission attempts (success & failure)
  - Finance team can view submission history per invoice
- **Owner**: Database Admin + QA Lead
- **Status**: Mitigated (has duplicate detection)

### Risk [R005]: Authentication Token Expiry During Batch
- **Date Identified**: 2026-02-05
- **Risk Description**: OAuth token expires mid-batch; remaining invoices fail to submit
- **Impact on Plant Operations**: Partial batch failure; some invoices not submitted to MyInvois
- **Likelihood**: Low (token TTL is 1 hour, batch runs ~30 min)
- **Severity**: High (partial compliance failure)
- **Mitigation Strategy**:
  - Cache OAuth token with 1-hour TTL
  - Refresh token on 401 response (automatic)
  - Use service account (not user account) for batch
  - Log all token refreshes in audit trail
  - Fail-safe: Retry entire invoice with new token
- **Owner**: Security Officer + Development Lead
- **Status**: Mitigated (auto-refresh on 401)

### Risk [R006]: Rate Limit Exceeded (100 req/min)
- **Date Identified**: 2026-02-05
- **Risk Description**: Batch submits >100 invoices/minute; MyInvois throttles requests (HTTP 429)
- **Impact on Plant Operations**: Batch slows down significantly; may not complete in expected window
- **Likelihood**: Medium (if batch not rate-limited)
- **Severity**: Medium (batch still completes, just slower)
- **Mitigation Strategy**:
  - Enforce 600ms delay between submissions (99 req/min < 100 limit)
  - Monitor response times; alert if approaching limit
  - Implement exponential backoff on 429 response
  - Retry on 429 (after backoff)
  - Document rate limit in integration guide
- **Owner**: Development Lead
- **Status**: Mitigated (implemented 600ms delay)

### Risk [R007]: SQL Server Audit Database Failure
- **Date Identified**: 2026-02-05
- **Risk Description**: SQL Server audit database unavailable; cannot write audit logs
- **Impact on Plant Operations**: No audit trail; compliance violation; cannot prove submissions
- **Likelihood**: Low (database is mission-critical, monitored)
- **Severity**: Critical (compliance + forensics lost)
- **Mitigation Strategy**:
  - Database is managed by IT (backups, replication)
  - Log connection failures in Windows Event Log
  - Alert Operations Manager immediately on DB failure
  - Fallback: Log to file (temporary) until DB recovers
  - Implement database connection retry (30s timeout)
  - Quarterly backup restoration tests
- **Owner**: Database Admin
- **Status**: Mitigated (monitoring + alerts)

### Risk [R008]: Encryption Key Loss (e-signature)
- **Date Identified**: 2026-02-05
- **Risk Description**: e-signature private key lost/corrupted; cannot sign invoices for MyInvois
- **Impact on Plant Operations**: Cannot submit invoices to MyInvois; batch fails; tax compliance gap
- **Likelihood**: Very Low (keys stored in secure vault)
- **Severity**: Critical (tax compliance impossible)
- **Mitigation Strategy**:
  - Store keys in Azure Key Vault (never hardcode)
  - Use official MyInvois SDK (no custom cryptography)
  - Implement key rotation policy (annually)
  - Backup & restore key rotation tests (semi-annually)
  - Key access logged to audit trail
  - Finance team can re-register with new key (emergency)
- **Owner**: Security Officer
- **Status**: Mitigated (vault + SDK)

---

## Lessons Learned

### Lesson [L001]: Always Validate Before Submission
- **Date**: 2026-02-05
- **Context**: Early prototype submitted invoices without full validation
- **What Went Wrong**: Some invoices rejected by MyInvois for missing fields; no audit trail of rejection
- **Root Cause**: Assumed MOVEX database records were always valid
- **Manufacturing Impact**: Finance team spent 2 hours manually fixing and resubmitting
- **Solution**: Implemented comprehensive validation (20+ mandatory fields) before submission
- **Prevention**: Always validate data before external API calls; test with real data samples
- **Related Decisions**: [D005] Queue-based retry pattern (enables validation reporting)

### Lesson [L002]: Network Retries Must Be Idempotent
- **Date**: 2026-02-05
- **Context**: Early implementation retried failed submissions immediately
- **What Went Wrong**: Same invoice submitted twice on network timeout; MyInvois rejected as duplicate
- **Root Cause**: No idempotency check; retry logic didn't track submission status
- **Manufacturing Impact**: Invoice marked as failed in audit trail despite MyInvois acceptance
- **Solution**: 
  - Add idempotency check (track invoice ID + submission timestamp)
  - Check for duplicates before retry
  - Log submission attempts separately
- **Prevention**: Design all external API calls to be idempotent; implement duplicate detection
- **Related Decisions**: [D005] Queue-based retry pattern

### Lesson [L003]: Batch Windows Matter in Manufacturing
- **Date**: 2026-02-05
- **Context**: Early design proposed real-time submissions
- **What Went Wrong**: Finance team complained about unpredictability; couldn't plan month-end close
- **Root Cause**: Didn't consult finance team early enough
- **Manufacturing Impact**: Finance workflow disrupted; additional training needed
- **Solution**: Switch to monthly batch (1st of month); predictable, testable, finance-friendly
- **Prevention**: Always consult with business users about timing; match to their operational rhythm
- **Related Decisions**: [D002] Monthly batch processing

---

## Common Pitfalls (Manufacturing + Finance Software)

### Integration Pitfalls

❌ **Assuming synchronous availability**
- **What**: Expecting MOVEX DB2/MyInvois to always respond immediately
- **Why it fails**: Legacy systems have maintenance windows; government systems unavailable 10 PM - 6 AM
- **Manufacturing impact**: Batch fails; no invoices submitted; compliance gap
- **✅ Solution**: Implement queue-based async with exponential backoff + retry queue

❌ **Hard-coding timeouts**
- **What**: Setting fixed 10-second timeout in code
- **Why it fails**: Network conditions vary; finance month-end causes M3 to slow down
- **Manufacturing impact**: Batch randomly fails at unpredictable times
- **✅ Solution**: Make timeouts configurable (30s default); document rationale

❌ **Breaking existing interfaces**
- **What**: Changing MOVEX DB2 table schema without notice
- **Why it fails**: Finance system depends on exact schema; breaks downstream queries
- **Manufacturing impact**: Data parsing fails; batch broken until manual intervention
- **✅ Solution**: Maintain backward compatibility; version interfaces; test with legacy data

❌ **Ignoring rate limits**
- **What**: Submitting 200 invoices/minute to MyInvois (limit is 100)
- **Why it fails**: API throttles; batch stalls
- **Manufacturing impact**: Batch takes 2x longer; may miss SLA
- **✅ Solution**: Enforce rate limiting in code; monitor usage

### Data Pitfalls

❌ **Assuming clean data from ERP**
- **What**: Trusting MOVEX database records are always valid/complete
- **Why it fails**: ERP data is often messy (blanks, typos, wrong types)
- **Manufacturing impact**: Validation fails; finance team must manually fix
- **✅ Solution**: Implement comprehensive validation; report errors clearly; test with real data samples

❌ **Ignoring time zones**
- **What**: Storing timestamps in local time instead of UTC
- **Why it fails**: Batch runs during different seasons; dates shift; audits are confusing
- **Manufacturing impact**: Compliance audit fails; can't prove submission timing
- **✅ Solution**: Always store timestamps in UTC (ISO 8601); convert for display only

❌ **Not handling duplicates**
- **What**: Assuming each invoice is unique; no duplicate detection
- **Why it fails**: Network timeouts cause retries; batch reruns; same invoices submitted twice
- **Manufacturing impact**: MyInvois rejects duplicates; invoice lost; compliance gap
- **✅ Solution**: Track submission status in audit log; check for duplicates before retry

❌ **Logging PII (Personally Identifiable Information)**
- **What**: Including customer names, email, phone in error logs
- **Why it fails**: Audit logs are compliance-critical; cannot log customer data
- **Manufacturing impact**: Compliance violation; potential data breach
- **✅ Solution**: Redact PII in logs; log only invoice ID, not customer details

### Deployment Pitfalls

❌ **Deploying during month-end close**
- **What**: Pushing changes on 28th-31st
- **Why it fails**: Finance team is in critical close period; no time to test/rollback
- **Manufacturing impact**: Finance operations disrupted; batch may be unstable
- **✅ Solution**: Only deploy 1st-27th; avoid quarter/year-end

❌ **No rollback plan**
- **What**: Deploying without knowing how to undo
- **Why it fails**: If something breaks, cannot recover quickly
- **Manufacturing impact**: Batch stuck in broken state; invoices not submitted
- **✅ Solution**: Always have tested rollback (database revert, code rollback, etc.)

❌ **Insufficient testing**
- **What**: Testing only with 10 invoices; deploying to batch of 1,000
- **Why it fails**: Hidden issues only appear at scale
- **Manufacturing impact**: Batch fails; manual workaround needed
- **✅ Solution**: Dry-run with production data (shadow mode); smoke tests before deploy

❌ **No communication plan**
- **What**: Deploying without notifying finance team
- **Why it fails**: Finance team doesn't know system is down; can't plan workaround
- **Manufacturing impact**: Finance operations disrupted; no backup plan
- **✅ Solution**: Notify Finance Manager 1 week before; provide runbooks for issues

### Performance Pitfalls

❌ **Unbounded queries**
- **What**: Fetching all invoices from MOVEX DB2 without pagination
- **Why it fails**: ERP databases are huge (millions of records); query times out
- **Manufacturing impact**: Batch fails to retrieve data
- **✅ Solution**: Always use pagination (e.g., 500 invoices at a time) + limits

❌ **Real-time when batch works**
- **What**: Building real-time submissions when monthly batch is sufficient
- **Why it fails**: Over-engineering; added complexity without business need
- **Manufacturing impact**: More code to maintain; more failures; no benefit
- **✅ Solution**: Match latency to business need; batch is fine for finance

❌ **Not considering scale**
- **What**: Designing for 600 invoices/month; what if 6,000 next year?
- **Why it fails**: System hits resource limits; batch fails under load
- **Manufacturing impact**: Batch becomes unstable as volume grows
- **✅ Solution**: Design for 3x current volume from day one; test with large datasets

---

## Prevention Strategies

### Before Writing Code
- [ ] Consult with business users about timing/workflow
- [ ] Validate all integration contracts
- [ ] Review similar projects for pitfalls
- [ ] Identify all failure scenarios

### Before Deployment
- [ ] Test with production-like data volume (3x current)
- [ ] Run dry-run with actual systems (shadow mode)
- [ ] Validate rollback procedure
- [ ] Notify stakeholders 1 week in advance

### In Production
- [ ] Monitor error logs daily
- [ ] Set up alerts for common failures
- [ ] Quarterly review of risk register
- [ ] Annual lessons-learned session with finance + IT

---

## Related Documentation
- [01-manufacturing-context.md](01-manufacturing-context.md) - Operational environment
- [08-governance-and-decisions.md](08-governance-and-decisions.md) - Decision framework
- [rules.md](rules.md) - Quality standards
- [ai/evidence/decision-log.md](../evidence/decision-log.md) - Execution decisions
