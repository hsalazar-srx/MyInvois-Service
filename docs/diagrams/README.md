# Diagrams - Visual System Documentation

**Last Updated:** February 5, 2026  
**Status:** MyInvois-Service  
**Owner:** Documentation Lead

## Overview

This directory contains **Mermaid-based visual diagrams** to help understand MyInvois-Service architecture, processes, and integration flows. Diagrams are:
- ✅ Version-controllable (text-based, not images)
- ✅ GitHub-renderable (render natively in repos)
- ✅ Easy to maintain (edit markdown files)
- ✅ Useful for both humans and AI assistants

---

## Available Diagrams

### System & Architecture
- **[architecture.md](architecture.md)** - MyInvois-Service components, MOVEX integration, MyInvois submission flow
- **[deployment-topology.md](deployment-topology.md)** - Infrastructure, database, OAuth server, deployment topology

### Security & Data
- **[auth-flow.md](auth-flow.md)** - OAuth 2.0 authentication with MyInvois, token management, session flow
- **[data-flow.md](data-flow.md)** - Invoice data flow: M3 → Service → Validation → MyInvois → Audit Log

### Integration & Processes
- **[integration-sequence.md](integration-sequence.md)** - Monthly batch processing, MOVEX API, MyInvois API, error handling
- **[workflow-process.md](workflow-process.md)** - Finance workflow, batch submission, error recovery, audit trail

### Guidance
- **[decision-tree.md](decision-tree.md)** - Troubleshooting decisions, when to retry, when to escalate

---

## Using Diagrams

### For Developers
1. Start with **[architecture.md](architecture.md)** to understand system components
2. Review **[data-flow.md](data-flow.md)** to understand invoice data movement
3. Check **[integration-sequence.md](integration-sequence.md)** for batch processing details
4. Reference **[auth-flow.md](auth-flow.md)** when working on authentication

### For DevOps/Operations
1. **Start with [deployment-topology.md](deployment-topology.md)** for infrastructure
2. **Use [decision-tree.md](decision-tree.md)** for operational decisions
3. **Reference [workflow-process.md](workflow-process.md)** for process understanding

### For Finance Team
1. **Start with [workflow-process.md](workflow-process.md)** to see your workflow
2. **Review [architecture.md](architecture.md)** for high-level understanding
3. **Check [integration-sequence.md](integration-sequence.md)** for batch timeline

### For AI Assistants
1. **Read [architecture.md](architecture.md)** first for system context
2. **Use [decision-tree.md](decision-tree.md)** to determine if work should proceed
3. **Reference relevant diagrams** based on the task:
   - Auth work → [auth-flow.md](auth-flow.md)
   - Integration work → [integration-sequence.md](integration-sequence.md)
   - Process changes → [workflow-process.md](workflow-process.md)

---

## Mermaid Format & Tools

All diagrams use **[Mermaid](https://mermaid.js.org/)** syntax:
- ✅ Renders natively in GitHub
- ✅ Supported in VS Code with extensions
- ✅ Version-controllable (text-based)
- ✅ Exportable to PNG/SVG

### Color Conventions

| Color | Meaning | Usage |
|-------|---------|-------|
| 🟢 Green (#ccffcc) | Success, Complete | Successful submissions, completed steps |
| 🔴 Red (#ff0000) | Critical Stop, Failure | Errors, blocking issues, MyInvois rejections |
| 🟠 Orange (#ffcc66) | Warning, Retry | Validation issues, temporary failures, rate-limit |
| 🟡 Yellow (#ffff99) | Pending, Waiting | Queued items, pending approval |
| ⚪ Gray (default) | Normal flow | Standard operations, neutral states |

### Node Shapes
- **Rectangle:** Process, action, or step
- **Diamond:** Decision point or condition
- **Cylinder:** Database or data store
- **Document:** File, log, or report
- **Circle:** External system or actor

---

## Updating Diagrams

When system changes:
1. **Edit the Markdown file** with diagram code
2. **Test syntax** at [mermaid.live](https://mermaid.js.org/syntax/syntax.html)
3. **Update "Last Updated" date** at the top
4. **Document change** in `ai/evidence/change-impact.md`
5. **Review with team** before committing

Diagrams are version-controlled with code. Major changes go in `ai/evidence/decision-log.md`.

---

## Creating New Diagrams

Use this template structure:

```markdown
# [Diagram Title]

**Last Updated:** [DATE]  
**Status:** [Draft | Review | Production]  
**Owner:** [Role/Name]

## Purpose

[What this diagram shows and why it exists]

## [Diagram Name]

\`\`\`mermaid
---
config:
  theme: light
  layout: elk
  look: classic
---
[Your diagram code here]
\`\`\`

## Description

[Detailed explanation]

## Related Diagrams

- [Link to related diagram 1]
```

---

## Best Practices

### Keep Diagrams Clear
- ✅ One topic per diagram
- ✅ Clear, descriptive labels
- ✅ Include legends if needed
- ✅ Split complex diagrams

### Keep Diagrams Current
- ✅ Review quarterly
- ✅ Update after major changes
- ✅ Archive outdated diagrams
- ✅ Link related diagrams

---

## Tools & Resources

**Mermaid Documentation:** https://mermaid.js.org/  
**Mermaid Live Editor:** https://mermaid.live/  
**VS Code Extension:** Markdown Preview Mermaid Support

To export as PNG/SVG:
1. Copy diagram to mermaid.live
2. Click "Download" or "Export"
3. Save file

---

## FAQ

**Q: Which format should I use?**  
A: Use Mermaid for all diagrams. Use images only for screenshots or external diagrams.

**Q: How detailed should diagrams be?**  
A: Enough to understand, not so much that maintenance becomes a burden.

**Q: Can I reference diagrams in code?**  
A: Yes! Add comments with links:
```csharp
// See docs/diagrams/auth-flow.md for OAuth 2.0 details
// See docs/diagrams/integration-sequence.md for batch processing flow
```

**Q: What if a diagram becomes outdated?**  
A: Update it immediately and document the change. Archive old versions in git history.

---

## Related Documentation

- [README.md](../README.md) - Project overview
- [ai/rules.md](../../ai/rules.md) - AI operating rules
- [ai/memory/02-system-architecture.md](../../ai/memory/02-system-architecture.md) - Architecture decisions
- [ai/evidence/decision-log.md](../../ai/evidence/decision-log.md) - Decisions logged

---

**Maintenance Instructions:**
- Review diagrams when updating system architecture
- Keep sections current as system evolves
- Gather feedback from users quarterly
- Link diagrams from code and documentation
