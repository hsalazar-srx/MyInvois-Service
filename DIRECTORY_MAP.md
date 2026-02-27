# MyInvois-Service Directory Map

## Complete Structure

```
MyInvois-Service/
│
├── README.md                           # Overview and getting started guide
├── QUICK_REFERENCE.md                  # Quick reference for common tasks
├── INDEX.md                            # Complete navigation guide (you are here)
├── DIRECTORY_MAP.md                    # Visual directory structure
├── IMPLEMENTATION_SUMMARY.md           # Summary of deliverables
│
├── appsettings.json                    # Production configuration
├── appsettings.Development.json        # Development overrides
│
├── src/                                # 💻 Application Source Code
│   ├── Configuration/
│   │   ├── MovexApiSettings.cs
│   │   ├── MyInvoisApiSettings.cs
│   │   ├── BatchConfiguration.cs
│   │   ├── ProcessingSettings.cs
│   │   └── ValidationSettings.cs
│   │
│   ├── Models/
│   │   ├── MovexInvoice.cs
│   │   ├── MyInvoiceDocument.cs
│   │   ├── SubmissionResult.cs
│   │   ├── ValidationError.cs
│   │   └── BatchResult.cs
│   │
│   ├── Services/
│   │   ├── InvoiceProcessor.cs
│   │   ├── MovexInvoiceReader.cs
│   │   ├── MyInvoiceMapper.cs
│   │   ├── MyInvoiceSubmitter.cs
│   │   └── AuditLogger.cs
│   │
│   ├── Validators/
│   │   ├── MandatoryFieldsValidator.cs
│   │   ├── TINValidator.cs
│   │   ├── DateValidator.cs
│   │   ├── CurrencyValidator.cs
│   │   └── TotalsValidator.cs
│   │
│   ├── Controllers/                   # Phase 2
│   │
│   └── database/
│       ├── create-audit-table.sql
│       └── create-audit-views.sql
│
├── tests/                              # 🧪 Test Code
│   ├── MyInvois.Service.Tests/
│   │   ├── Validators/
│   │   ├── Services/
│   │   └── Integration/
│   └── testdata/
│       ├── sample-movex-invoice.json
│       └── sample-myinvois-output.xml
│
├── docs/                               # 📚 Operational Documentation
│   ├── SETUP.md                        # 5-minute local setup guide
│   ├── DEPLOYMENT.md                   # Production deployment runbook
│   └── TROUBLESHOOTING.md              # Operations support guide
│
├── ai/                                 # 🤖 AI-Assisted Development Workspace
│   │
│   ├── rules.md                        # 🚨 AI OPERATING INSTRUCTIONS (READ FIRST!)
│   │
│   ├── memory/                         # 📚 Long-term Project Knowledge
│   │   ├── 00-product-vision.md        # What & why we're building
│   │   ├── 00-skills-audit.md          # Skills-first architecture audit
│   │   ├── 01-manufacturing-context.md # Operational environment & constraints
│   │   ├── 01-system-architecture.md   # Technical stack & architecture
│   │   ├── 02-data-model.md            # SQL schema & data structures
│   │   ├── 03-myinvois-requirements.md # Business rules & validations
│   │   ├── 04-api-integration.md       # MOVEX + MyInvois API specs
│   │   ├── 05-deployment-guide.md      # Setup & operations
│   │   ├── 06-known-risks-and-pitfalls.md # Lessons learned & risk mitigation
│   │   ├── 07-product-roadmap.md       # Strategic direction & phases
│   │   ├── 08-governance-and-decisions.md # Approvals & change control
│   │   ├── 09-implementation-decisions.md # ADRs (canonical)
│   │   └── 10-testing-strategy.md # Testing strategy & test cases
│   │
│   ├── planning/                       # 📋 Initiative & Sprint Planning
│   │   ├── initiative.md               # Major initiative scope & planning
│   │   ├── sprint-plan.md              # Sprint breakdown (Week 1-4)
│   │   └── execution-plan.md           # Detailed task execution procedures
│   │
│   ├── tasks/                          # ✅ Day-to-day Task Tracking
│   │   ├── task-template.md            # Individual task structure template
│   │   └── sprint-backlog.md           # Sprint progress & work items
│   │
│   └── evidence/                       # 📊 Audit Trail & Documentation
│       ├── decision-log.md             # Why decisions were made
│       ├── change-impact.md            # Change impact assessments
│       └── release-notes.md            # Release documentation
│
├── .github/                            # GitHub Configuration
│   └── (workflows, templates as needed)
│
├── .githooks/                          # Git Hooks
│   └── (pre-commit, post-merge as needed)
│
├── MyInvois.Service.csproj             # .NET Project File
└── Program.cs                          # Application Entry Point
```

---

## Navigation by File Type

### 📖 Documentation Files

**Getting Started:**
- `README.md` - Project overview, first time setup
- `QUICK_REFERENCE.md` - Common tasks, decision trees
- `INDEX.md` - Complete navigation guide
- `DIRECTORY_MAP.md` - This file (visual structure)

**Operational Guides:**
- `docs/SETUP.md` - Local development environment (5 min)
- `docs/DEPLOYMENT.md` - Production deployment checklist
- `docs/TROUBLESHOOTING.md` - Common issues & solutions

**AI Workspace:**
- `ai/rules.md` - AI operating instructions (CRITICAL)
- `ai/memory/` - Long-term project knowledge (8 files)
- `ai/planning/` - Initiative & sprint planning (3 files)
- `ai/tasks/` - Task tracking & templates (2 files)
- `ai/evidence/` - Audit trail & release notes (3 files)

### 💻 Source Code

**Configuration:**
- `src/Configuration/` - Settings classes (5 files)
- `appsettings.json`, `appsettings.Development.json` - Config files

**Business Logic:**
- `src/Models/` - DTOs (5 files)
- `src/Services/` - Core services (5 files)
- `src/Validators/` - Field validation (5 files)

**Infrastructure:**
- `src/database/` - SQL scripts (2 files)
- `src/Controllers/` - API endpoints (Phase 2)

### 🧪 Test Code

- `tests/MyInvois.Service.Tests/` - Unit & integration tests
- `tests/testdata/` - Sample data files

---

## Navigation by Role

### 👨‍💼 Product Owner / Business Sponsor

| When | Go To | Purpose |
|------|-------|---------|
| **Planning phase** | `ai/memory/00-product-vision.md` | Understand business objectives |
| **Review roadmap** | `ai/memory/07-product-roadmap.md` | See phases & timeline |
| **Initiative scope** | `ai/planning/initiative.md` | Confirm scope & resources |
| **Status update** | `IMPLEMENTATION_SUMMARY.md` | Weekly/monthly progress |
| **Release info** | `ai/evidence/release-notes.md` | What was shipped |

### 👨‍💻 Developer (Week 2+)

| When | Go To | Purpose |
|------|-------|---------|
| **First time** | `README.md` | Project overview |
| **Setup local** | `docs/SETUP.md` | 5-minute setup |
| **Understand system** | `ai/memory/02-system-architecture.md` | How components work |
| **Implement feature** | `ai/memory/03-integration-contracts.md` | External API specs |
| **Check standards** | `ai/memory/05-standards-security-quality.md` | Code quality rules |
| **Find task** | `ai/tasks/sprint-backlog.md` | Current assignments |
| **Execute task** | `ai/planning/execution-plan.md` | Step-by-step guidance |

### 🧪 QA / Test Engineer

| When | Go To | Purpose |
|------|-------|---------|
| **Test planning** | `ai/memory/05-standards-security-quality.md` | Test standards & targets |
| **Week 2 tests** | `ai/planning/execution-plan.md` | What to test |
| **Find test tasks** | `ai/tasks/sprint-backlog.md` | Test assignments |
| **Debug failures** | `docs/TROUBLESHOOTING.md` | Common issues |
| **Write tests** | `ai/memory/03-integration-contracts.md` | API specs to test against |

### 🚀 Operations / DevOps

| When | Go To | Purpose |
|------|-------|---------|
| **First time** | `ai/memory/01-manufacturing-context.md` | Operational constraints |
| **Before deploying** | `docs/DEPLOYMENT.md` | Deployment checklist |
| **Production issue** | `docs/TROUBLESHOOTING.md` | Diagnosis & resolution |
| **Risk assessment** | `ai/memory/06-known-risks-and-pitfalls.md` | What can go wrong |
| **Go-live planning** | `ai/planning/initiative.md` | Timeline & dependencies |

### 🏗️ Architecture / Tech Lead

| When | Go To | Purpose |
|------|-------|---------|
| **Understand design** | `ai/memory/02-system-architecture.md` | Technical architecture |
| **Review decisions** | `ai/memory/04-governance-and-decisions.md` | Architecture decisions (ADRs) |
| **API contracts** | `ai/memory/03-integration-contracts.md` | Integration boundaries |
| **Security review** | `ai/memory/05-standards-security-quality.md` | Security standards |
| **Risk management** | `ai/memory/06-known-risks-and-pitfalls.md` | Known risks & mitigations |

---

## Key Files by Purpose

### Understanding the Project
1. `README.md` - Start here for overview
2. `ai/memory/00-product-vision.md` - Business context
3. `ai/memory/02-system-architecture.md` - How it works

### Getting Started Developing
1. `docs/SETUP.md` - Local environment
2. `ai/rules.md` - Safety rules & best practices
3. `QUICK_REFERENCE.md` - Common tasks

### Implementing Features
1. `ai/memory/03-integration-contracts.md` - API specs
2. `ai/memory/05-standards-security-quality.md` - Code standards
3. `ai/planning/execution-plan.md` - Task breakdown

### Deploying to Production
1. `docs/DEPLOYMENT.md` - Deployment checklist
2. `ai/memory/01-manufacturing-context.md` - Environment constraints
3. `ai/memory/06-known-risks-and-pitfalls.md` - What to watch for

### Troubleshooting Production Issues
1. `docs/TROUBLESHOOTING.md` - Common issues & solutions
2. `ai/memory/06-known-risks-and-pitfalls.md` - Known failure modes
3. Contact: [Dev Lead] - Escalation for code issues

---

## File Statistics

| Category | Files | Total Lines | Purpose |
|----------|-------|-------------|---------|
| **Documentation** | 7 | 1800+ | Guides & references |
| **AI Memory** | 8 | 3700+ | Project knowledge |
| **AI Planning** | 3 | 600+ | Initiative & sprints |
| **AI Tasks** | 2 | 300+ | Task tracking |
| **AI Evidence** | 3 | 400+ | Audit trail |
| **Source Code** | 30+ | 2000+ | Implementation |
| **Tests** | 15+ | 1500+ | Quality assurance |
| **Database** | 2 | 700+ | Schema & views |
| **Configuration** | 2 | 100+ | App settings |
| **TOTAL** | **70+** | **11000+** | **Complete project** |

---

**Last Updated:** February 5, 2026  
**Owner:** Development Team  
**Distribution:** All team members

