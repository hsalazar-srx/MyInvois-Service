# MyInvois-Service: Claude Code Instructions

**Project:** .NET 8.0 Microservice - Malaysian e-Invoicing Integration
**Phase:** MVAI (Target: Feb 28, 2026)
**Critical:** Read [ai/rules.md](ai/rules.md) FIRST

---

## 🤖 Multi-Agent System (MAS)

**Primary Agents:**
- **@expert-myinvois-compliance** - UBL transformation, LHDN compliance validation, digital signatures
- **@developer-dotnet** - .NET API implementation, service layer, clean architecture
- **@architect-system-design** - ADR creation, design reviews, architecture decisions

**Process Agents:**
- **@validator-quality** - Security review, quality gates
- **@documenter-technical** - ADRs, API docs
- **@validator-iis-deploy** - IIS pre-deployment validation: route audit, secrets check, app pool config, smoke tests (skill: `cloud/dev-prod-parity` v1.0.0)

**Collaboration Pattern:**
1. **Compliance questions** → @expert-myinvois-compliance analyzes requirements
2. **Architecture decisions** → @architect-system-design reviews impact, creates ADR if needed
3. **Implementation** → @developer-dotnet coordinates with integration/compliance experts
4. **Security review** → @validator-quality enforces quality gates
5. **Documentation** → @documenter-technical updates ADRs and API docs
6. **IIS deployment** → @validator-iis-deploy runs pre-deploy checklist before every UAT/production push

**Workflows:**
- **Compliance changes** → `C:\.github\governance\workflows\compliance-change.yaml`
- **Integration features** → `C:\.github\governance\workflows\integration-feature.yaml`

**Registry:** `C:\.github\agents\manifest.json` (MAS v2.0)

---

## 🚀 Quick Start

**First session?** Read [ai/rules.md](ai/rules.md) → [00-product-vision.md](ai/memory/00-product-vision.md) → [00-skills-audit.md](ai/memory/00-skills-audit.md)

**Before coding:** Check skills registry → Update audit → Reference in code

**Current work:** [sprint-backlog.md](ai/tasks/sprint-backlog.md)

---

## 🔒 Critical Rules (Never Violate)

### Workspace Standards (MANDATORY)
Per [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md):
- ✅ **TLS 1.2+** for all external connections
- ✅ **7-year retention** for audit logs
- ✅ **User Secrets** (dev) / **Azure Key Vault** (prod)
- ✅ **Test coverage ≥80%** (xUnit + FluentAssertions + Moq)
- ✅ **Never log PII** (Tax IDs, names, emails)
- ✅ **TDE encryption** at rest, **TLS 1.2+** in transit

### MyInvois-Specific (from ai/rules.md)
- ❌ Never exceed 100 req/min (enforced by 600ms batch delay)
- ❌ Never skip XAdES v1.1 signature
- ❌ Never submit duplicates (check uniqueness first)
- ✅ Validate 20+ mandatory fields before submission
- ✅ Use MyInvois SDK for signing (no custom cryptography)

---

**Critical constraints:** See [ai/rules.md](ai/rules.md) and [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md)

---

## 🔄 Context Management

**Prevent bloat:** /plan for complex tasks • Break into subtasks <50% • /compact at 50% • Commit each phase

**Multi-phase work:** Create PROGRESS.md for resumption

---

## ⚡ Git Workflow

### Branch Protection
- ❌ **Never push directly to `master`** — all changes go through a feature branch + PR
- Branch naming: `feature/description`, `fix/description`, `docs/description`, `chore/description`
- PR required before merging to `master`; keep PRs small and focused

### Commit Discipline
- **Commit after each feature/fix** (not end of day) • Small commits = easier rollback + clearer history • Don't batch unrelated changes
- Use Conventional Commits: `feat:`, `fix:`, `docs:`, `chore:`, `test:`, `refactor:`

See [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) — Git Workflow Standards for full rules.

---

## 💻 Workflow

**Steps:** Understand → Check ADRs → Skills → Implement → Test → Commit

**Decision tree:** In memory? Follow it. Small? Document. Big? ADR. Unclear? Escalate.

**Details:** [ai/workflows/development.md](ai/workflows/development.md)

---

## 🎯 Essential Pattern: Skills-First

**Before implementing ANY feature:**
```csharp
// 1. Check C:\Projects\.github\skills\manifest.json
// 2. Document in ai/memory/00-skills-audit.md
// 3. Reference skill in code

/// <summary>
/// Reads invoices from MOVEX DB2/AS400.
/// Uses skill: integration/movex-db2-data-source v1.0 (ADR-013)
/// </summary>
public class MovexInvoiceReader : IMovexInvoiceReader { }
```

**All other patterns:** [ai/patterns/](ai/patterns/) (audit logging, validation, error handling, configuration)

---

**Error handling:** See [ai/memory/04-api-integration.md](ai/memory/04-api-integration.md) and [ai/patterns/error-handling.md](ai/patterns/error-handling.md)

---

**Configuration:** See [ai/memory/02-data-model.md](ai/memory/02-data-model.md) and [ai/patterns/configuration.md](ai/patterns/configuration.md)

---

## ✅ Quality Gates

**Before commit:** Build passes, tests pass (≥80%), no credentials/PII → [Full checklist](ai/checklists/pre-commit.md)

**Before deploy:** Workspace compliance verified → [Full checklist](ai/checklists/pre-deployment.md)

---

## 🚨 Escalate

**Dev Lead:** Missing APIs, unclear requirements
**Architecture:** Design changes, schema redesign
**Stop immediately:** Security breach, data loss, production failure

**Details:** [ai/workflows/development.md](ai/workflows/development.md)

---

## 📚 Documentation

| Quick Access | Detailed Knowledge |
|--------------|-------------------|
| [ai/rules.md](ai/rules.md) - Critical rules | [ai/memory/](ai/memory/) - Knowledge base |
| [ai/workflows/development.md](ai/workflows/development.md) - Workflow | [ai/patterns/](ai/patterns/) - Code patterns |
| [ai/checklists/](ai/checklists/) - Quality gates | [WORKSPACE_RULES.md](../../.github/WORKSPACE_RULES.md) - Standards |
| [ai/tasks/sprint-backlog.md](ai/tasks/sprint-backlog.md) - Current work | [ai/evidence/decision-log.md](ai/evidence/decision-log.md) - Decisions |

---

## 🎯 Session Checklist

**Start:** Read sprint-backlog → /plan for complex tasks
**During:** Check skills → Write tests → /compact at 50%
**End:** Tests pass → No credentials/PII → Commit → Push to feature branch (never master directly)

---

## 📞 Getting Help

Search `ai/memory/` → Check `decision-log.md` → Review `06-known-risks-and-pitfalls.md` → Consult skills/agents → Escalate per [ai/workflows/development.md](ai/workflows/development.md)

---

**Version:** 2.2
**Last Updated:** 2026-03-19
**Next Review:** 2026-04-01
