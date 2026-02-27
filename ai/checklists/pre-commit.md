# Pre-Commit Checklist

**Project:** MyInvois-Service
**Purpose:** Quality gates before committing code
**Last Updated:** 2026-02-17

---

## ✅ Pre-Commit Quality Gates

### 1. Code Compiles

```bash
dotnet build
```

**Expected:** ✅ Build succeeded. 0 Warning(s). 0 Error(s).

---

### 2. All Tests Pass

```bash
dotnet test
```

**Expected:** ✅ Passed! - Total: [N], Passed: [N], Failed: 0, Skipped: 0

---

### 3. Code Coverage ≥80%

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

**Expected:** Line coverage: ≥80%

**If below 80%:**
- Add missing unit tests
- Focus on: validators, mappers, services
- Acceptable to exclude: DTOs, configuration classes

---

### 4. No Hardcoded Credentials

**Manual Review:**

Search codebase for:
```bash
grep -ri "password\|secret\|apikey\|connectionstring" src/
```

**Check for:**
- ❌ Connection strings with passwords
- ❌ API keys in code
- ❌ Hardcoded tokens
- ❌ Database credentials

**Should be in:**
- ✅ User Secrets (development)
- ✅ Azure Key Vault references (production)
- ✅ Environment variables (CI/CD)

---

### 5. No PII in Logs

**Manual Review:**

Search for logging statements:
```bash
grep -ri "log.*TIN\|log.*name\|log.*email" src/
```

**Never log:**
- ❌ Tax IDs (TIN, BRN)
- ❌ Customer names
- ❌ Email addresses
- ❌ Phone numbers
- ❌ Payment details

**OK to log:**
- ✅ Invoice numbers
- ✅ User Windows ID (service account)
- ✅ Timestamps
- ✅ Status codes
- ✅ Correlation IDs

---

### 6. C# Naming Conventions

**Review:**
- ✅ Classes: `PascalCase` (e.g., `MovexInvoiceReader`)
- ✅ Methods: `PascalCase` (e.g., `GetPendingInvoices()`)
- ✅ Properties: `PascalCase` (e.g., `InvoiceNumber`)
- ✅ Private fields: `_camelCase` (e.g., `_settings`)
- ✅ Constants: `UPPER_SNAKE_CASE` (e.g., `MAX_RETRIES`)
- ✅ Interfaces: `IPascalCase` (e.g., `IMovexInvoiceReader`)
- ✅ Booleans: `Is` or `Has` prefix (e.g., `IsActive`)

---

### 7. XML Documentation on Public Methods

**Check:**
- ✅ All public classes have XML summary
- ✅ All public methods have XML summary
- ✅ Complex logic has inline comments explaining "why"

**Example:**
```csharp
/// <summary>
/// Validates invoice against MyInvois mandatory field requirements.
/// Uses skill: validation/myinvois-constraints v1.0
/// </summary>
/// <param name="invoice">Invoice to validate</param>
/// <returns>Validation result with errors if any</returns>
public ValidationResult Validate(MyInvoiceDocument invoice)
{
    // Implementation...
}
```

---

### 8. Commit Message References Ticket

**Format:**
```
[#123] Brief description of change

- Detail 1
- Detail 2

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>
```

**Check:**
- ✅ Starts with ticket reference (e.g., `[#123]`)
- ✅ Brief summary (under 72 characters)
- ✅ Descriptive details if needed
- ✅ Co-authored tag if working with Claude

---

### 9. Skills Audit Updated

**If you used or proposed skills:**

Update `ai/memory/00-skills-audit.md`:
```markdown
## [Date] - [Feature Name]

**Skills Used:**
- ✅ integration/movex-db2-data-source v1.0 - Used in MovexInvoiceReader

**Skills Proposed:**
- 📝 validation/myinvois-mandatory-fields - Validate 20+ MyInvois fields
  - Status: Documented in code, ready for skill extraction
```

---

### 10. Decision Log Updated (If Applicable)

**If you made a design decision:**

Update `ai/evidence/decision-log.md`:
```markdown
## [Date] - [Decision Title]

**What:** Problem statement
**Why:** Context + constraints
**Decision:** What was chosen
**Rationale:** Why it's better
**Consequences:** What changes
```

---

## 🚀 Quick Pre-Commit Script

```bash
#!/bin/bash
# Save as: pre-commit.sh

echo "🔨 Building..."
dotnet build || exit 1

echo "🧪 Running tests..."
dotnet test || exit 1

echo "📊 Checking coverage..."
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
# Note: Coverage check is informational, doesn't block commit

echo "🔍 Checking for credentials..."
if grep -r "password\|Password\|SECRET\|ApiKey" src/; then
    echo "❌ Found potential hardcoded credentials!"
    exit 1
fi

echo "✅ All pre-commit checks passed!"
```

---

## 📋 Full Checklist

**Before running `git commit`:**

- [ ] Code compiles without errors
- [ ] All tests pass (unit + integration)
- [ ] Code coverage ≥80%
- [ ] No hardcoded credentials
- [ ] No PII in logs
- [ ] Follows C# naming conventions
- [ ] All public methods have XML documentation
- [ ] Commit message references ticket number
- [ ] Skills audit updated (if applicable)
- [ ] Decision log updated (if applicable)

---

**Related:**
- [Pre-Deployment Checklist](pre-deployment.md)
- [Development Workflow](../workflows/development.md)
- [CLAUDE.md](../../CLAUDE.md)
