# Development Workflow

**Project:** MyInvois-Service
**Purpose:** Detailed step-by-step development workflow
**Last Updated:** 2026-02-17

---

## 📋 Complete Task Workflow

### 1. Understand Requirements

**Steps:**
```
1. Read ai/tasks/sprint-backlog.md for current assignments
2. Check ai/memory/03-myinvois-requirements.md for business rules
3. Review ai/memory/06-known-risks-and-pitfalls.md for pitfalls
4. Check ai/evidence/decision-log.md for recent decisions
5. Verify scope with ai/planning/execution-plan.md
```

**Key Questions to Answer:**
- What is the acceptance criteria?
- What are the constraints?
- What are the dependencies?
- What are the risks?

---

### 2. Check Architecture

**Steps:**
```
1. Review relevant ADRs in ai/memory/09-implementation-decisions.md
2. Verify approach aligns with existing decisions
3. Check if skills exist for this functionality
4. If new ADR needed, follow governance process (ai/memory/08-governance-and-decisions.md)
```

**Architecture Checklist:**
- [ ] No conflicting ADRs
- [ ] Approach fits existing patterns
- [ ] Dependencies identified
- [ ] Performance implications considered

---

### 3. Implement with Skills-First Approach

**Steps:**
```
1. Search centralized skills registry: C:\Projects\.github\skills\manifest.json
2. If skill exists:
   - Use and reference it in code comments
   - Follow skill's implementation pattern
   - Document in ai/memory/00-skills-audit.md
3. If skill doesn't exist:
   - Document as skill gap in ai/memory/00-skills-audit.md
   - Propose new skill (name, purpose, inputs, outputs)
   - Implement following workspace standards
4. Write code following C# conventions (see WORKSPACE_RULES.md)
```

**Code Standards:**
- PascalCase for classes, methods, properties
- _camelCase for private fields
- UPPER_SNAKE_CASE for constants
- IPascalCase for interfaces
- XML docs on all public members

---

### 4. Test Thoroughly

**Test Strategy:**
```
1. Write unit tests alongside implementation (TDD approach)
   - Test file: Tests/[ComponentName]Tests.cs
   - Test naming: [Method]_[Scenario]_[Expected]
   - Use xUnit + FluentAssertions + Moq

2. Run tests locally:
   dotnet test

3. Verify coverage:
   dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
   (Target: ≥80%)

4. Write integration tests for critical paths
   - Test against real dependencies (MOVEX sandbox, MyInvois sandbox)
   - Clean up test data after execution

5. Review ai/memory/10-testing-strategy.md for requirements
```

**Testing Checklist:**
- [ ] Unit tests for all new code paths
- [ ] Integration tests for external dependencies
- [ ] Edge cases tested (null, empty, invalid)
- [ ] Error handling paths tested
- [ ] All tests passing
- [ ] Coverage ≥80%

---

### 5. Document Decisions

**Documentation Steps:**
```
1. Update ai/evidence/decision-log.md for design choices
   - What: Problem statement
   - Why: Context + constraints
   - Options: Alternatives considered
   - Decision: What was chosen
   - Rationale: Why it's better
   - Consequences: What changes

2. Update ai/memory/00-skills-audit.md if skills used/proposed

3. Add XML documentation to public methods:
   /// <summary>
   /// [Clear description of what this does]
   /// Uses skill: [category]/[skill-name] v[version]
   /// </summary>

4. Update ai/memory/ files if architecture changes
   - 01-system-architecture.md for component changes
   - 02-data-model.md for data structure changes
   - 09-implementation-decisions.md for new ADRs
```

---

### 6. Multi-Phase Work (Optional)

**For long-running tasks, create PROGRESS.md:**

```markdown
# Task: [Task Name]

## Phase 1: [Name] - COMPLETED ✅
- ✅ [Subtask 1]
- ✅ [Subtask 2]
- ✅ Tests passing (85% coverage)

## Phase 2: [Name] - IN PROGRESS ⏳
- ✅ [Completed subtask]
- ⏳ [Current subtask]
- [ ] [Pending subtask]

## Phase 3: [Name] - PENDING
- [ ] [Future work]
```

**Use TodoWrite tool to track progress:**
- Break large tasks into phases
- Update status after each phase
- Helps resume after interruptions

---

### 7. Before Committing

**Pre-Commit Checklist:**

See [../checklists/pre-commit.md](../checklists/pre-commit.md) for full checklist.

**Quick Check:**
```bash
# 1. Build
dotnet build

# 2. Test
dotnet test

# 3. Coverage
dotnet test /p:CollectCoverage=true

# 4. Manual checks
# - No hardcoded credentials
# - No PII in logs
# - XML docs on public methods
```

---

## 🔄 Decision Making During Development

### Use This Tree

```
Is it documented in ai/memory/ files?
  ├─ YES → Follow documented decision
  │        (don't re-litigate, maintain consistency)
  └─ NO → Is it a small decision (low risk, low impact)?
           ├─ YES → Document & proceed
           │        (add to ai/evidence/decision-log.md)
           └─ NO → Is it an ADR-worthy decision?
                    ├─ YES → Write ADR, get approval
                    │        (add to ai/memory/09-implementation-decisions.md)
                    │        (also add to ai/evidence/decision-log.md)
                    └─ NO → Escalate to Tech Lead
```

---

## 🚨 When to Stop & Escalate

### Stop & Escalate to Dev Lead

- ❌ Missing MOVEX API endpoint (integration contract broken)
- ❌ MyInvois authentication failing (credentials issue)
- ❌ Database connection error (infrastructure)
- ❌ Ambiguous requirements (conflicting specs)
- ❌ Design decision conflicts with multiple ADRs

### Stop & Escalate to Architecture

- ❌ Fundamental design change needed (e.g., not using XAdES)
- ❌ Rate limit strategy insufficient (>100 req/min needed)
- ❌ Database schema redesign required
- ❌ Phase 1 scope creep (features beyond core submission)

### Stop Immediately & Alert Team

- 🔴 Security breach suspected (credentials exposed)
- 🔴 Data loss detected (audit logs corrupted)
- 🔴 Production deployment failed uncontrolled
- 🔴 MyInvois API behavior changed unexpectedly

---

## 📚 Related Documents

- [Pre-Commit Checklist](../checklists/pre-commit.md)
- [Pre-Deployment Checklist](../checklists/pre-deployment.md)
- [Code Patterns](../patterns/)
- [Testing Strategy](../memory/10-testing-strategy.md)
- [CLAUDE.md](../../CLAUDE.md) - Quick reference

---

**Owner:** Development Team
**Maintained By:** Tech Lead
**Review Schedule:** Monthly
