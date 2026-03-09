# Governance and Decisions - MyInvois-Service

**Last Updated:** March 9, 2026
**Status:** Active  
**Owner:** IT Manager + Finance Manager

---

## Purpose

This document defines **governance**, **approval requirements**, and **change control**.  
It does **not** store detailed ADRs. Those are centralized elsewhere.

### Canonical Decision Locations

- **Architecture Decision Records (ADRs):** `ai/memory/implementation-decisions.md` (canonical)
- **Decision Log (Evidence):** `ai/evidence/decision-log.md` (chronological log)
- **Change Impact:** `ai/evidence/change-impact.md`
- **Release Notes:** `ai/evidence/release-notes.md`

---

## Decision Workflow (How to Record Decisions)

1. **Architecture-level decisions** → Create/Update ADR in `implementation-decisions.md` and add a log entry in `evidence/decision-log.md`.
2. **Operational / process decisions** → Add entry in `evidence/decision-log.md`, and update `change-impact.md` + `release-notes.md` if externally visible.
3. **Approval requirements** → Use the matrix below to confirm who must approve.

---

## Approval Matrix

| Change Type | Approver Role | Required | Lead Time | Notes |
|-------------|---------------|----------|-----------|-------|
| Architecture changes | Solution Architect | Yes | 2 weeks | Review tech stack, design patterns |
| Integration changes | IT Manager + Integration Lead | Yes | 3 weeks | M3, MyInvois, audit DB impacts |
| Database schema changes | Database Admin + Audit Officer | Yes | 4 weeks | Audit trail changes are high-impact |
| Batch timing/window changes | Finance Manager + Operations | Yes | 1 week | Affects finance operations |
| Credential/security changes | Security Officer | Yes | 2 weeks | OAuth tokens, encryption keys |
| Process workflow changes | Finance Manager | Yes | 1 week | Affects finance team training |
| MyInvois compliance changes | Compliance Officer + IT Manager | Yes | 3 weeks | Tax law impact |
| Emergency production fixes | IT Manager (on-call) | Immediate | N/A | Post-change review required within 48h |
| Configuration changes | IT Manager | No | 3 days | Non-code changes (timeouts, retry counts) |
| Documentation updates | Project Owner | No | 0 days | Best effort to keep current |

---

## Change Control Process

### Normal Change Process (Standard Changes)

#### Step 1: Proposal & Planning (Owner: Requester)
- Identify change need
- Estimate scope and risk
- Document in `ai/planning/execution-plan.md`
- Assess manufacturing impact (downtime, disruptions)
- Identify rollback strategy

#### Step 2: Review (Owner: Technical Lead)
- Technical feasibility review
- Backward compatibility check
- Testing strategy review
- Manufacturing impact assessment
- Security implications review

#### Step 3: Approval (Owner: Approver from matrix above)
- Verify all required approvals obtained
- Confirm manufacturing impact acceptable
- Schedule deployment window
- Create change record
- Notify stakeholders

**Approval Timeline**
- 3–5 business days for normal changes
- Risk review required for “High” risk changes
- Finance Manager must review batch-related changes

#### Step 4: Implementation
- Deploy in approved window (avoid month-end)
- Execute pre-deployment validation
- Deploy code/database changes
- Run smoke tests
- Monitor error logs

#### Step 5: Validation
- Verify all smoke tests pass
- Check audit logs for errors
- Finance team spot-checks batch output
- Monitor system for 24 hours

#### Step 6: Evidence & Documentation
- Log decision in `ai/evidence/decision-log.md`
- Document impact in `ai/evidence/change-impact.md`
- Update release notes
- Update affected documentation

---

## Emergency Change Process

### When to Use Emergency Process
- **Production outage** preventing finance team from working
- **Security breach** requiring immediate patching
- **Compliance violation** discovered in production
- **Data loss** risk requiring immediate mitigation

### Approval Authority
- **IT Manager** (on-call) can approve emergency changes
- **No prior approval** required (inform stakeholders immediately)
- **Post-implementation review** required within 48 hours

### Notification
- Immediately notify: Finance Manager, Operations Manager
- Document issue + fix in emergency log
- Schedule post-mortem within 1 week

### Documentation Requirements
1. What was the emergency?
2. What was the fix?
3. Why was it emergency (not scheduled)?
4. How will this be prevented?
5. What approval is needed in hindsight?

**Emergency Deployment Window**
- Can occur any time (24/7) for production outages
- Prefer off-business hours when possible
- Finance team on-call availability matters

---

## Deployment Windows

### Preferred Deployment Window
- **Primary**: Friday 5 PM - Sunday 8 AM
- **Secondary**: Weekday 6 PM - 8 AM
- **Avoid**: Month-end (27th-31st), Quarter-end, Year-end

### Maintenance Windows
- **Scheduled Maintenance**: First Saturday of month, 8 PM - 10 PM
- **Security Patches**: Within 1 week of release (urgent patches: within 24h)

---

## Phase 2 Architectural Changes (March 2026)

### ADR-014: SQLite Audit Storage (Approved March 7, 2026)

| Field | Value |
|-------|-------|
| Decision | Replace SQL Server audit logger with SQLite via EF Core 8 |
| Supersedes | ADR-003 (SQL Server for Audit Logs) — for MyInvois-Service only |
| Approved by | IT Manager (Architecture Review) |
| Evidence | `ai/evidence/decision-001-sqlite-audit-storage.md` |
| Architecture Review | ✅ Signed off March 7, 2026 |
| Approval scope | MyInvois-Service only (not workspace-wide standard change) |
| WORKSPACE_RULES impact | WORKSPACE_RULES.md updated — SQLite now approved for IIS deployments <500 events/day |

This decision is scoped to MyInvois-Service. The workspace-wide standard (WORKSPACE_RULES.md) has
been updated to recognise SQLite as an approved alternative for self-hosted IIS deployments where
the volume is <500 audit events/day, BitLocker encryption is enabled on the server volume, and
NTFS ACL restricts access to the App Pool identity.
