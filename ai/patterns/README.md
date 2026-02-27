# Code Patterns

**Project:** MyInvois-Service
**Purpose:** Reusable code patterns and examples
**Last Updated:** 2026-02-17

---

## 📂 Pattern Organization

### Core Patterns

1. **[skills-first.md](skills-first.md)** - How to check and use centralized skills
2. **[audit-logging.md](audit-logging.md)** - Workspace-compliant audit logging
3. **[configuration.md](configuration.md)** - Strongly-typed configuration
4. **[error-handling.md](error-handling.md)** - MyInvois error classification
5. **[validation.md](validation.md)** - Validator pattern (collect all errors)

---

## 🎯 Quick Reference

### When to Use Which Pattern

| Task | Pattern | File |
|------|---------|------|
| Starting new feature | Skills-first check | [skills-first.md](skills-first.md) |
| Logging submissions | Audit logging | [audit-logging.md](audit-logging.md) |
| Loading settings | Configuration | [configuration.md](configuration.md) |
| Handling API errors | Error handling | [error-handling.md](error-handling.md) |
| Validating invoices | Validation | [validation.md](validation.md) |

---

## 📝 Pattern Format

Each pattern file includes:
- **Purpose** - What problem it solves
- **When to Use** - Scenarios where this applies
- **Code Example** - Complete, working example
- **Anti-Patterns** - What NOT to do
- **Related** - Links to related patterns/docs

---

## 🔗 Related Documentation

- [CLAUDE.md](../../CLAUDE.md) - Quick reference
- [Development Workflow](../workflows/development.md) - Complete workflow
- [Pre-Commit Checklist](../checklists/pre-commit.md) - Quality gates
