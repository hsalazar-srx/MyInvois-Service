# Task Template: Individual Work Item Structure

Use this template for every individual task within the sprint. Copy, fill out, and add to sprint-backlog.md or task tracking system.

---

## [Task ID: 2.1] Task Name: Implement MovexInvoiceReader Service

### Basic Information

**Task ID:** 2.1  
**Sprint:** Sprint 2 (Week 2: Feb 10-14)  
**Status:** ⏳ Not Started / 🔄 In Progress / ✅ Complete  
**Assignee:** [Developer Name]  
**Due Date:** Tuesday, Feb 11, 2 PM UTC  
**Priority:** P0 (Critical) / P1 (High) / P2 (Medium) / P3 (Low)  
**Effort Estimate:** 12 hours  
**Actual Effort:** [Update daily]  

---

### Task Description

**What to build:**
[Clear, concise description of what needs to be implemented]

**Why we're building it:**
[Business context and value]

**Definition of Done:**
- [ ] Code implemented per specification
- [ ] Unit tests written (12+ test cases)
- [ ] Code coverage ≥80%
- [ ] All tests passing
- [ ] Code review approved
- [ ] Zero critical defects
- [ ] Builds successfully
- [ ] Merged to main branch

---

### Requirements & Specification

**Functional Requirements:**
1. [Requirement 1 with acceptance criteria]
2. [Requirement 2 with acceptance criteria]
3. [Requirement 3 with acceptance criteria]

**Non-Functional Requirements:**
- Performance: [e.g., <5s response time]
- Scalability: [e.g., handle 1000 requests/batch]
- Security: [e.g., use OAuth 2.0 client credentials]
- Reliability: [e.g., 3 retry attempts with backoff]

**Edge Cases to Handle:**
- [ ] [Edge case 1]
- [ ] [Edge case 2]
- [ ] [Edge case 3]

**Related Documents:**
- See: ai/memory/03-integration-contracts.md (API specs)
- See: ai/memory/05-standards-security-quality.md (code standards)
- See: QUICK_REFERENCE.md (common patterns)

---

### Implementation Plan

**Step 1: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2
- [ ] Sub-action 3

**Step 2: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2
- [ ] Sub-action 3

**Step 3: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2

**Step 4: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2

**Step 5: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2

**Step 6: [Step Title] (2 hours)**
- [ ] Sub-action 1
- [ ] Sub-action 2

---

### Code Structure

**File(s) to Create/Modify:**
- `src/Services/MovexInvoiceReader.cs` - Main implementation
- `src/Services/IMovexInvoiceReader.cs` - Interface (if not exists)

**Key Methods:**
```csharp
public interface IMovexInvoiceReader
{
    Task<List<MovexInvoice>> GetPendingInvoices(int year, int month);
    Task<MovexInvoice> GetInvoiceById(string invoiceId);
    Task<List<MovexInvoice>> GetInvoicesByDateRange(DateTime from, DateTime to);
}
```

**Dependencies:**
- IInvoiceDataSource (for DB2 data access)
- ILogger (for logging)
- MovexDbSettings (for configuration)

**Code Pattern to Follow:**
[Copy from standards document]

---

### Test Plan

**Unit Tests (12+ test cases):**

Test Case 1: GetPendingInvoices - Valid Month - Returns Sales and Purchase
- **Setup:** Mock IInvoiceDataSource with valid results
- **Execute:** Call GetPendingInvoices(2026, 2)
- **Assert:** Returns list with both sales and purchase invoices
- **File:** tests/MyInvois.Service.Tests/Services/MovexInvoiceReaderTests.cs

Test Case 2: GetPendingInvoices - No Invoices - Returns Empty List
- **Setup:** Mock IInvoiceDataSource with empty results
- **Execute:** Call GetPendingInvoices(2026, 1)
- **Assert:** Returns empty list (not null)
- **File:** tests/MyInvois.Service.Tests/Services/MovexInvoiceReaderTests.cs

[... continue for all 12 test cases ...]

**Code Coverage Target:** ≥80% of MovexInvoiceReader.cs

**Test Execution:**
```bash
# Run all tests for this task
dotnet test --filter "MovexInvoiceReader"

# Check coverage
dotnet test /p:CollectCoverage=true /p:CoverageFilter="+[MyInvois.Service.Services]MovexInvoiceReader"
```

---

### Acceptance Criteria

**Functional:**
- [ ] Fetches pending invoices from MOVEX database (DB2 on AS/400)
- [ ] Returns MovexInvoice DTOs with all required fields
- [ ] Handles pagination (100 per request)
- [ ] Separates sales (100) from purchase (50) invoices
- [ ] Implements exponential backoff for 5xx errors
- [ ] Implements DB2 connection retry on transient errors
- [ ] Manages connection pooling via IInvoiceDataSource

**Quality:**
- [ ] Code coverage ≥80%
- [ ] All 12+ unit tests passing
- [ ] Zero critical code defects
- [ ] No hardcoded credentials
- [ ] Proper error logging
- [ ] Follows naming conventions (ai/memory/05)

**Documentation:**
- [ ] Code comments for complex logic
- [ ] Method documentation (XML comments)
- [ ] Test case documentation

**Performance:**
- [ ] Response time <5 seconds per batch
- [ ] Handles 1000+ invoices per batch
- [ ] No memory leaks (dispose DB2 connections properly)

---

### Known Issues & Workarounds

| Issue | Symptom | Workaround |
|-------|---------|-----------|
| [Known issue 1] | [How it appears] | [Temporary fix] |
| [Known issue 2] | [How it appears] | [Temporary fix] |

---

### Blockers & Dependencies

**Dependencies:**
- MOVEX DB2 connection string (from User Secrets)
- MovexDbSettings configuration class
- MovexInvoice DTO model

**External Dependencies:**
- DB2 AS/400 server (MOVEX database with fpledg/fsledg/fgledg tables)

**Blocked By:**
- None (MovexDbSettings already created)

**Blocks:**
- Task 2.2 (MyInvoiceMapper) - depends on MovexInvoice being populated
- Task 3.1 (Integration tests) - needs real data source

---

### Code Review Checklist

**Before requesting review:**
- [ ] Code compiles without warnings
- [ ] All tests passing (`dotnet test`)
- [ ] Code coverage ≥80%
- [ ] No hardcoded credentials or secrets
- [ ] Proper error handling (try-catch where appropriate)
- [ ] Logging at Info/Warn/Error levels (no Debug in prod code)
- [ ] No unused imports or variables
- [ ] Follows ai/memory/05 naming conventions
- [ ] Method documentation present (XML comments)
- [ ] Error messages are user-friendly
- [ ] No TODO comments (completed or removed)

**For Code Reviewer:**
- [ ] Code follows ai/memory/05 patterns
- [ ] Error handling covers all cases
- [ ] No hardcoded values (use configuration)
- [ ] Logging is appropriate
- [ ] Tests are comprehensive
- [ ] Performance acceptable
- [ ] Security implications reviewed
- [ ] No tech debt introduced

---

### Daily Progress Update

**Monday, Feb 10:**
- [ ] Progress: 0% → 30%
- Completed: Set up IInvoiceDataSource
- In progress: Implement GetPendingInvoices
- Blockers: None
- Confidence: High
- Notes: [Any notes]

**Tuesday, Feb 11:**
- [ ] Progress: 30% → 100%
- Completed: All methods implemented, 12/12 tests passing
- In progress: Code review
- Blockers: None
- Confidence: High
- Notes: [Any notes]

---

### Post-Task Review

**Completed on:** [Date]  
**Actual Effort:** [X hours] (estimated 12 hours)  
**Variance:** [+/- X hours]  

**What went well:**
- [What worked well]
- [What was efficient]

**What could improve:**
- [What was difficult]
- [What took longer]

**Lessons learned:**
- [Key learning]
- [Best practice discovered]

---

## Template for Other Tasks

Copy the full template above for each task. Fill in:

1. **Basic Information:** ID, assignee, due date, effort
2. **Description:** What and why
3. **Requirements:** Detailed spec
4. **Implementation Plan:** Step-by-step breakdown
5. **Code Structure:** Files, methods, dependencies
6. **Test Plan:** All test cases
7. **Acceptance Criteria:** How to verify it's done
8. **Blockers:** Any dependencies or risks
9. **Daily Updates:** Track progress
10. **Post-Review:** Lessons learned

---

### Example: Task 2.2

**Task ID:** 2.2  
**Task Name:** Implement MyInvoiceMapper Service  
**Assignee:** Developer  
**Due Date:** Wednesday, Feb 12, 12 PM  
**Effort:** 12 hours  

[Fill out full template for this task]

---

### Example: Task 2.3

**Task ID:** 2.3  
**Task Name:** Implement MandatoryFieldsValidator  
**Assignee:** Developer  
**Due Date:** Wednesday, Feb 12, 5 PM  
**Effort:** 4 hours  

[Fill out full template for this task]

---

## How to Use This Template

**Before Starting:**
1. Copy this template
2. Fill in Task ID, name, assignee, due date
3. Write description & requirements
4. Create implementation plan (estimate hours)
5. Add test cases (estimate # of tests)
6. Share with team in sprint planning

**During Work:**
1. Update "Daily Progress Update" each morning standup
2. Update "Blockers & Dependencies" if things change
3. Check off "Implementation Plan" steps as completed
4. Track actual time spent

**When Complete:**
1. Run full test suite
2. Check code coverage
3. Request code review
4. Update acceptance criteria (all checked?)
5. Complete "Post-Task Review"
6. Merge code to main branch
7. Mark task as ✅ Complete

---

**Last Updated:** February 5, 2026  
**Owner:** Development Team  
**Template Version:** 1.0

