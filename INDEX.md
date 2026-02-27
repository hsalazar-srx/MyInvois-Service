# MyInvois-Service: Complete Index

**Quick Navigation:** [Documentation](#documentation) | [AI Workspace](#ai-workspace) | [Memory](#memory) | [Planning](#planning) | [Tasks](#tasks) | [Evidence](#evidence) | [Source Code](#source-code)

---

## 📖 Documentation

### Main Documentation Files

| File | Description | Audience | When to Read |
|------|-------------|----------|--------------|
| [README.md](README.md) | **START HERE** - Complete overview, getting started, project scope | Everyone | First time |
| [QUICK_REFERENCE.md](QUICK_REFERENCE.md) | Quick reference for common tasks, decision trees, workflows | Developers | Daily |
| [DIRECTORY_MAP.md](DIRECTORY_MAP.md) | Visual navigation guide organized by role | Everyone | When lost |
| [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md) | What was built, why, week-by-week summary | Sponsors | Planning |
| [docs/SETUP.md](docs/SETUP.md) | Local development environment setup (5 min) | Developers | First time setup |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Production deployment procedures & checklist | Operations | Deployment |
| [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common issues, diagnosis, solutions | Operations | Problem solving |

---

## 🤖 AI Workspace

### ai/rules.md

**Purpose:** AI operating instructions and safety rules  
**Critical:** YES - Read FIRST  
**Read By:** AI assistants, developers  
**Update Frequency:** Rarely

**Key Sections:**
- Order of operation (10 required steps)
- Hard rules (never violate)
- Safety rules by component
- Data safety rules
- When to escalate
- Decision framework
- Quality gates
- Best practices
- Validation checklist

---

## 📚 Memory (Long-term Project Knowledge)

### ai/memory/00-product-vision.md
**Purpose:** What & why we're building  
**Owner:** Product Owner  
**Audience:** Everyone  

**Contains:**
- Business context
- MyInvois platform overview
- MVAI initiative scope
- Key success metrics
- Phase 1 vs Phase 2 roadmap

### ai/memory/00-skills-audit.md
**Purpose:** Skills-first architecture audit and reuse  
**Owner:** Architecture Team  
**Audience:** Developers, AI agents  

**Contains:**
- Skills registry review
- Skills gaps and proposed new skills
- Refactor notes for reuse

### ai/memory/01-manufacturing-context.md
**Purpose:** Operational environment & constraints  
**Owner:** Finance Stakeholder  
**Audience:** Developers, Operations  

**Contains:**
- MOVEX ERP integration points
- MyInvois government platform constraints
- Invoice volume profile (100 sales, 500-1000 purchase/month)
- Batch submission window (1st of month, 2-hour window)
- Regulatory requirements (7-year audit trail)

### ai/memory/01-system-architecture.md
**Purpose:** Technical stack & system design  
**Owner:** Architecture Team  
**Audience:** Developers, Tech Leads  

**Contains:**
- System diagram (MOVEX → Service → MyInvois → SQL Server)
- Component responsibilities
- Integration points & APIs
- Data flow (happy path & error cases)
- Technology choices (.NET 8.0, SQL Server, OAuth 2.0)

### ai/memory/02-data-model.md
**Purpose:** Data structures & schema  
**Owner:** Data Lead  
**Audience:** Developers, DBAs  

**Contains:**
- SQL schema and audit tables
- DTOs and configuration models
- Data flow & storage decisions

### ai/memory/03-myinvois-requirements.md
**Purpose:** Business rules & validation requirements  
**Owner:** Requirements Team  
**Audience:** Developers, QA  

**Contains:**
- Validation rules and mandatory fields
- Error codes and retry rules
- Requirement traceability references

### ai/memory/04-api-integration.md
**Purpose:** External API integration guide  
**Owner:** Integration Lead  
**Audience:** Developers implementing integrations  

**Contains:**
- MOVEX REST API endpoints
- MyInvois API endpoints
- OAuth 2.0 flow, rate limits, error handling

### ai/memory/05-deployment-guide.md
**Purpose:** Operational setup & deployment  
**Owner:** Operations  
**Audience:** DevOps, IT Ops  

**Contains:**
- SQL setup, scheduling, secrets, monitoring
- Deployment checklist and rollback steps

### ai/memory/06-known-risks-and-pitfalls.md
**Purpose:** Risk register and mitigations  
**Owner:** Development Team  
**Audience:** Everyone  

**Contains:**
- Common failure modes
- Risk mitigations
- Escalation criteria

### ai/memory/07-product-roadmap.md
**Purpose:** Strategic direction & phases  
**Owner:** Product Owner  
**Audience:** Sponsors, Product, IT  

**Contains:**
- Phase 1/2 roadmap
- Initiatives and timelines

### ai/memory/08-governance-and-decisions.md
**Purpose:** Approval matrix & change control  
**Owner:** IT Manager + Finance Manager  
**Audience:** Decision makers, Operations  

**Contains:**
- Approval matrix
- Change control process
- Deployment windows

### ai/memory/09-implementation-decisions.md
**Purpose:** Canonical ADRs  
**Owner:** Architecture Team  
**Audience:** Developers, Decision makers  

**Contains:**
- 12 ADRs documenting key design choices

### ai/memory/06-known-risks-and-pitfalls.md
**Purpose:** Lessons learned & risk mitigation  
**Owner:** Development Team  
**Audience:** Everyone  

**Contains:**
- High-risk areas & probability assessment
- Mitigations for each risk
- Common failure modes
- Troubleshooting strategies
- Escalation criteria
- Post-mortem templates

### ai/memory/07-product-roadmap.md
**Purpose:** Strategic direction & future phases  
**Owner:** Product Owner  
**Audience:** Planning, decision makers  

**Contains:**
- Phase 1 (MVAI) scope & timeline: Feb 3-28
- Phase 2 planned features: Mar-Apr (Portal UI, automatic retry, status polling)
- Phase 3 considerations: Cloud migration, real-time submission
- Deferred items & rationale
- Long-term vision

---

## 📋 Planning (Initiative & Sprint Planning)

### ai/planning/initiative.md
**Purpose:** Major initiative planning & scope  
**Owner:** Product Owner  
**Audience:** Stakeholders, team leads  

**Contains:**
- Initiative name: MVAI (MOVEX MyInvois Adoption Initiative)
- Business context & drivers
- Objectives & success criteria
- Scope (in & out)
- Timeline: Feb 3 - Feb 28 (4 weeks)
- Resource requirements
- Dependencies & constraints
- Risk assessment

### ai/planning/sprint-plan.md
**Purpose:** Sprint planning template & current sprints  
**Owner:** Scrum Master  
**Audience:** Development team  

**Contains:**
- Week 1 (Feb 3-7): Planning & architecture ✅ COMPLETE
- Week 2 (Feb 10-14): Core implementation
  - Tasks: MovexReader, Mapper, Validators, Submitter, AuditLogger
  - Unit tests (54+ cases)
- Week 3 (Feb 17-21): Integration & sandbox testing
  - Integration tests, E2E tests, performance baseline
- Week 4 (Feb 24-28): UAT & go-live
  - Finance UAT, dry run, production go-live

### ai/planning/execution-plan.md
**Purpose:** Detailed task execution & step-by-step procedures  
**Owner:** Dev Lead  
**Audience:** Developers, QA  

**Contains:**
- Week 2 task breakdown (6 tasks)
- Week 3 task breakdown (4 tasks)
- Week 4 task breakdown (3 tasks)
- Acceptance criteria per task
- Dependencies & sequencing
- Effort estimates
- Success metrics

---

## ✅ Tasks (Day-to-day Task Tracking)

### ai/tasks/sprint-backlog.md
**Purpose:** Current sprint progress & work items  
**Owner:** Scrum Master  
**Audience:** Development team  

**Contains:**
- Active work items (who assigned, status, % complete)
- Blockers & dependencies
- Daily standup notes
- Sprint metrics (velocity, burn-down)
- Retrospective feedback

### ai/tasks/task-template.md
**Purpose:** Individual task structure  
**Owner:** Dev Lead  
**Audience:** Task owners  

**Contains:**
- Task structure template
- Required sections (title, description, acceptance criteria)
- Example task (with all sections filled)
- How to use for consistency

---

## 📊 Evidence (Audit Trail & Documentation)

### ai/evidence/decision-log.md
**Purpose:** Why decisions were made  
**Owner:** Architecture Team  
**Audience:** Future developers, auditors  

**Contains:**
- All major decisions (date, context, rationale)
- Stakeholder approval dates
- Change history
- Links to ADRs & evidence

### ai/evidence/change-impact.md
**Purpose:** Change impact assessments  
**Owner:** Dev Lead  
**Audience:** Reviewers, stakeholders  

**Contains:**
- Changes made (sprint, date)
- Components affected
- Risk assessment
- Testing impact
- Deployment impact
- Rollback impact

### ai/evidence/release-notes.md
**Purpose:** Release documentation  
**Owner:** Tech Writer  
**Audience:** Users, operations  

**Contains:**
- Version history (current: 1.0, released Feb 28)
- Features shipped per release
- Known issues
- Migration notes
- Support contacts

---

## 💻 Source Code

### src/ (Application Code)

```
src/
├── Configuration/       # Strongly-typed settings
├── Models/             # DTOs (MOVEX, MyInvois)
├── Services/           # Core business logic
├── Validators/         # Field-level validation (5 validators)
├── Controllers/        # ASP.NET controllers (Phase 2)
└── database/           # SQL scripts
```

### tests/ (Test Code)

```
tests/
├── MyInvois.Service.Tests/    # Unit tests
├── MyInvois.Service.Integration.Tests/  # Integration tests
└── testdata/                  # Sample data
    ├── sample-movex-invoice.json
    └── sample-myinvois-output.xml
```

---

## 🗂️ Navigation by Role

### 👨‍💼 Product Owner / Business Sponsor

**Start Here:**
1. [ai/memory/00-product-vision.md](ai/memory/00-product-vision.md) - What we're building
2. [ai/memory/07-product-roadmap.md](ai/memory/07-product-roadmap.md) - Strategic direction
3. [ai/planning/initiative.md](ai/planning/initiative.md) - Initiative scope
4. [ai/evidence/release-notes.md](ai/evidence/release-notes.md) - What shipped

### 👨‍💻 Developer

**Start Here:**
1. [README.md](README.md) - Project overview
2. [ai/rules.md](ai/rules.md) - Safety rules & operating instructions
3. [ai/memory/01-system-architecture.md](ai/memory/01-system-architecture.md) - How it works
4. [ai/memory/04-api-integration.md](ai/memory/04-api-integration.md) - External APIs
5. [ai/memory/rules.md](ai/memory/rules.md) - Quality standards
6. [docs/SETUP.md](docs/SETUP.md) - Local environment setup

### 🧪 QA / Test Engineer

**Start Here:**
1. [ai/memory/rules.md](ai/memory/rules.md) - Test standards
2. [ai/planning/execution-plan.md](ai/planning/execution-plan.md) - What to test
3. [ai/tasks/sprint-backlog.md](ai/tasks/sprint-backlog.md) - Current test tasks
4. [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) - Known issues

### 🚀 Operations / DevOps

**Start Here:**
1. [ai/memory/01-manufacturing-context.md](ai/memory/01-manufacturing-context.md) - Environment constraints
2. [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) - Deployment procedures
3. [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) - How to diagnose issues
4. [ai/memory/06-known-risks-and-pitfalls.md](ai/memory/06-known-risks-and-pitfalls.md) - What to watch for

### 🏗️ Architecture / Tech Lead

**Start Here:**
1. [ai/memory/01-system-architecture.md](ai/memory/01-system-architecture.md) - System design
2. [ai/memory/04-api-integration.md](ai/memory/04-api-integration.md) - APIs & contracts
3. [ai/memory/09-implementation-decisions.md](ai/memory/09-implementation-decisions.md) - ADRs
4. [ai/memory/10-testing-strategy.md](ai/memory/10-testing-strategy.md) - Testing strategy
4. [ai/memory/08-governance-and-decisions.md](ai/memory/08-governance-and-decisions.md) - Approvals & change control
4. [ai/memory/06-known-risks-and-pitfalls.md](ai/memory/06-known-risks-and-pitfalls.md) - Design risks

---

**Last Updated:** February 6, 2026  
**Owner:** Development Team  
**Distribution:** All team members

