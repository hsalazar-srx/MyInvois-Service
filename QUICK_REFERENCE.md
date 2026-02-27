# MyInvois-Service: Quick Reference

## Common Tasks

### Getting Started (First Day)

```powershell
# 1. Clone the repository
git clone https://github.com/your-org/myinvois-service.git
cd MyInvois-Service

# 2. Setup local environment
cd docs
notepad SETUP.md            # Follow the 5-minute setup

# 3. Read the rules
notepad ..\ai\rules.md      # Critical safety rules

# 4. Understand the architecture
notepad ..\ai\memory\02-system-architecture.md
```

### Finding Your First Task

1. Open: `ai/tasks/sprint-backlog.md`
2. Find task assigned to you (Week 1-4)
3. Open: `ai/planning/execution-plan.md`
4. Follow step-by-step guidance for your task

### Implementing a Service Method

1. **Understand the interface:**
   ```csharp
   // Open: src/Services/IYourService.cs
   public interface IInvoiceProcessor
   {
       Task<BatchResult> ProcessMonthlyBatch(int year, int month);
   }
   ```

2. **Check the contract:**
   ```
   Open: ai/memory/03-integration-contracts.md
   Find: MyInvois API specs, MOVEX DB2 table specs
   ```

3. **Follow standards:**
   ```
   Open: ai/memory/05-standards-security-quality.md
   Review: Error handling, logging, validation patterns
   ```

4. **Implement with tests:**
   ```csharp
   // Write unit test first (TDD)
   // tests/MyInvois.Service.Tests/Services/InvoiceProcessorTests.cs
   [Test]
   public async Task ProcessMonthlyBatch_ValidInvoices_ReturnsSuccess() { }
   
   // Then implement the service
   // src/Services/InvoiceProcessor.cs
   public async Task<BatchResult> ProcessMonthlyBatch(int year, int month) { }
   ```

### Debugging a Test Failure

1. **Read test output** - Note the specific failure
2. **Open test file** - `tests/MyInvois.Service.Tests/...TestName.cs`
3. **Trace to source** - Follow stacktrace to failing service/validator
4. **Check contract** - Does implementation match spec in ai/memory/03?
5. **Check standards** - Does code follow patterns in ai/memory/05?
6. **Ask for help** - Escalate per ai/rules.md section 6

### Deploying to Production

1. **Verify readiness:**
   ```
   Open: docs/DEPLOYMENT.md
   Follow: Pre-deployment checklist
   ```

2. **Review risks:**
   ```
   Open: ai/memory/06-known-risks-and-pitfalls.md
   Review: What can go wrong in production
   ```

3. **Execute deployment:**
   ```
   Follow: docs/DEPLOYMENT.md "Deployment Steps"
   Monitor: SQL Server audit logs
   ```

### Troubleshooting a Production Issue

```
1. Symptom → Check Table 1 (Common Issues)
2. Find matching issue in docs/TROUBLESHOOTING.md
3. Follow diagnosis steps
4. If still stuck → Escalate per ai/rules.md section 6
```

---

## Decision Trees

### "My Code Doesn't Work" (Fast Path)

```
Is it a unit test failure?
├─ YES → Follow "Debugging a Test Failure" above
└─ NO → Is it a validation error?
   ├─ YES → Check ai/memory/rules.md for patterns
    └─ NO → Is it an API call error?
      ├─ YES → Check ai/memory/04-api-integration.md for spec (DB2 connection issues, MyInvois API)
        └─ NO → Check docs/TROUBLESHOOTING.md for diagnostic queries
```

### "Should I Add a New Field?" (Feature Request)

```
1. Open: ai/memory/03-myinvois-requirements.md
2. Find MyInvois mandatory field list
3. If field is NOT on MyInvois mandatory list:
   ├─ Ask: "Will this break MyInvois compliance?"
   ├─ If YES → Don't add (blocking issue)
   └─ If NO → Proceed with implementation
4. If field IS mandatory:
   ├─ Review: ai/memory/09-implementation-decisions.md (ADR-#)
   └─ Propose change to Tech Lead (escalate per ai/rules.md)
```

### "How Do I Validate This Data?" (Validation Pattern)

```
1. Is it a mandatory MyInvois field?
   ├─ Check: ai/memory/03-myinvois-requirements.md section "Mandatory Fields"
   
2. What type of validation is needed?
   ├─ Format validation? → See ai/memory/rules.md
   ├─ Range validation? → See ai/memory/rules.md
   ├─ Business logic? → Create new Validator class
   
3. Where does validation go?
   ├─ If simple field check → MandatoryFieldsValidator
   ├─ If complex → New validator class (see pattern in 05)
   └─ If service logic → Implement in service with try/catch per ai/memory/rules.md
   
4. Example implementation:
   // src/Validators/YourValidator.cs
   public class YourValidator : IValidator
   {
       public ValidationResult Validate(MyInvoiceDocument doc)
       {
           // Check per MyInvois spec (ai/memory/03)
           // Log errors per ai/rules.md section 9
           // Return structured result
       }
   }
```

### "Rate Limit Hit" (Performance Issue)

```
MyInvois enforces: 100 requests/minute (60 second window)

Current solution:
├─ Batch size: Sales 100/batch, Purchase 50/batch
├─ Delay between batches: 600ms (20% utilization safety margin)
└─ This is proven safe per ai/memory/09-implementation-decisions.md ADR-004

If hitting rate limits:
├─ Check: Are batches too small? (increase per ADR-002)
├─ Check: Are delays too short? (increase per ADR-004)
└─ Escalate: Ask Tech Lead before changing parameters
```

### "MyInvois API Returned Error 429" (Rate Limit During Batch)

```
429 = Rate Limit Exceeded

Immediate action:
├─ Stop batch processing (automatic per code)
├─ Implement exponential backoff (Week 2 ADR-006)
├─ Log error to SQL Server audit table
└─ Notify operations team

For now (Phase 1):
├─ Retry up to 3 times with delay
├─ If still failing: Notify via email alert
└─ Escalate: Requires manual intervention on MyInvois side
```

### "How Do I Handle OAuth Token Expiration?" (Auth Issue)

```
MyInvois token lifetime: 3600 seconds (1 hour)

Solution (per ADR-007):
├─ Cache token in memory with TTL timestamp
├─ Before each API call: Check if token expired
├─ If expired: Automatically refresh using client credentials
├─ If refresh fails: Stop batch, notify operations, escalate
│
Example:
// src/Services/MyInvoiceSubmitter.cs
private async Task<string> GetAccessToken()
{
    if (_cachedToken != null && !IsTokenExpired())
        return _cachedToken;
    
    // Token expired or missing, refresh from OAuth endpoint
    _cachedToken = await RefreshAccessToken();
    return _cachedToken;
}
```

---

## Key Commands

### Build & Run

```powershell
# Build project
dotnet build

# Run tests
dotnet test

# Run specific test file
dotnet test --filter "ClassName"

# Debug single test
dotnet test --logger console --verbosity detailed
```

### Database Operations

```powershell
# Execute audit table creation
sqlcmd -S (local) -d AuditLog -i src/database/create-audit-table.sql

# Execute audit views creation
sqlcmd -S (local) -d AuditLog -i src/database/create-audit-views.sql

# Query failed submissions (diagnostics)
sqlcmd -S (local) -d AuditLog
> SELECT TOP 10 * FROM [dbo].[MyInvoisFailedSubmissions];
```

### Git Workflows

```powershell
# Create feature branch (Week 2+)
git checkout -b feature/invoice-processor

# Commit code (follow ai/rules.md)
git commit -m "feat: implement InvoiceProcessor service"

# Push to remote
git push origin feature/invoice-processor

# Create pull request
# → Open GitHub, select your branch, create PR
# → Reference: ai/memory/04-governance-and-decisions.md for approval criteria
```

---

## Critical Files Quick Links

| Task | File | Section |
|------|------|---------|
| "What am I building?" | `README.md` | Overview |
| "How do I set up?" | `docs/SETUP.md` | All sections |
| "What are the rules?" | `ai/rules.md` | All sections |
| "How does it work?" | `ai/memory/02-system-architecture.md` | Architecture |
| "What's the API spec?" | `ai/memory/03-integration-contracts.md` | API specs |
| "What are the standards?" | `ai/memory/05-standards-security-quality.md` | Standards |
| "What can go wrong?" | `ai/memory/06-known-risks-and-pitfalls.md` | Risks |
| "What's my task?" | `ai/tasks/sprint-backlog.md` | Assignments |
| "How do I do it?" | `ai/planning/execution-plan.md` | Step-by-step |
| "Why did we decide X?" | `ai/memory/04-governance-and-decisions.md` | ADRs |
| "It's broken!" | `docs/TROUBLESHOOTING.md` | Issues |
| "When do we deploy?" | `docs/DEPLOYMENT.md` | Deployment |

---

## Contact Points

| Issue Type | Contact | Channel |
|-----------|---------|---------|
| Code issue | Dev Lead | Slack #myinvois-dev |
| Architecture question | Tech Lead | Weekly tech sync |
| Test/QA issue | QA Lead | Slack #myinvois-qa |
| Operations issue | Ops Lead | Incident channel |
| Blocker (escalate) | Project Manager | Slack urgent notification |

---

## Weekly Checklist

### Every Monday (Week Planning)

- [ ] Read `ai/tasks/sprint-backlog.md` for assignments
- [ ] Review `ai/planning/execution-plan.md` for your tasks
- [ ] Check `ai/memory/06-known-risks-and-pitfalls.md` for relevant risks
- [ ] Sync with team on blockers

### Every Day (Before Coding)

- [ ] Check `ai/rules.md` section 8 (Quality Gates) before committing
- [ ] Validate code against `ai/memory/05-standards-security-quality.md`
- [ ] Run full test suite: `dotnet test`

### Every Friday (Status Update)

- [ ] Update `ai/tasks/sprint-backlog.md` with completed tasks
- [ ] Add any lessons to `ai/memory/06-known-risks-and-pitfalls.md`
- [ ] Report progress to Project Manager

### Before Production Deployment

- [ ] Review `docs/DEPLOYMENT.md` checklist
- [ ] Check `ai/memory/06-known-risks-and-pitfalls.md`
- [ ] Verify all tests passing: `dotnet test`
- [ ] Confirm audit logs configured
- [ ] Get approval from Tech Lead & Ops

---

## What If...?

| Scenario | Action |
|----------|--------|
| "I don't know how to do my task" | Read `ai/planning/execution-plan.md`, then ask Dev Lead |
| "I think a design decision is wrong" | Document in `ai/evidence/change-impact.md`, discuss with Tech Lead |
| "I found a risk we missed" | Add to `ai/memory/06-known-risks-and-pitfalls.md` with mitigation |
| "Code is slow" | Check batch sizing (ai/memory/04 ADR-004), profile, escalate |
| "API call failed" | Check `docs/TROUBLESHOOTING.md`, then check `ai/memory/03-integration-contracts.md` |
| "MyInvois returned unexpected error" | Log full response, check spec in `ai/memory/03`, escalate if new error |
| "I'm stuck for >30 min" | Ask for help per `ai/rules.md` section 6 (Escalation) |
| "I found a bug in production" | See `docs/TROUBLESHOOTING.md` for diagnosis, then escalate |

---

## 30-Second Navigation

```
Need quick answer?
├─ Error? → docs/TROUBLESHOOTING.md
├─ How to do X? → ai/planning/execution-plan.md
├─ Why did we? → ai/memory/04-governance-and-decisions.md
├─ What's allowed? → ai/rules.md
├─ API spec? → ai/memory/03-integration-contracts.md
├─ Standards? → ai/memory/05-standards-security-quality.md
└─ Confused? → README.md + INDEX.md
```

---

**Last Updated:** February 16, 2026
**Owner:** Development Team  
**Keep this open while developing:** Yes

