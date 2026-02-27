# Git Hooks Setup for Skills-First Architecture

This directory contains Git hooks that enforce **SRX Skills-Based Architecture** compliance.

## 📋 Available Hooks

### `pre-commit` - Skills Audit Validation

**Purpose:** Ensures `ai/memory/00-skills-audit.md` exists before committing implementation files.

**Triggers on commits containing:**
- `src/` files
- `Services/` files
- `Models/` files
- `Validators/` files
- `Configuration/` files
- `Controllers/` files

**Validation:**
1. Checks if `ai/memory/00-skills-audit.md` exists
2. Warns if audit contains template placeholders
3. Blocks commit if audit is missing

**Bypass (emergency only):**
```powershell
git commit --no-verify
```

---

## 🚀 One-Time Setup

### Step 1: Configure Git to Use Custom Hooks Directory

Run this command **once** in your repository:

```powershell
git config core.hooksPath .githooks
```

**Verification:**
```powershell
git config core.hooksPath
# Should output: .githooks
```

### Step 2: Ensure Hook is Executable (Linux/Mac only)

On Windows, this is automatic. On Linux/Mac:

```bash
chmod +x .githooks/pre-commit
```

---

## ✅ Testing the Hook

### Test 1: Commit without implementation files (should pass)

```powershell
# Create a non-implementation file
New-Item -Path "docs/test.md" -Value "Test" -Force
git add docs/test.md
git commit -m "test: add documentation"

# Expected: ✅ Hook passes (no implementation files)
```

### Test 2: Commit implementation file without skills audit (should fail)

```powershell
# Create an implementation file
New-Item -Path "src/Test.cs" -Value "// Test" -Force
git add src/Test.cs
git commit -m "feat: add test"

# Expected: ❌ Hook blocks commit with message about missing skills audit
```

### Test 3: Create skills audit and retry (should pass)

```powershell
# Create skills audit
Copy-Item "ai/memory/00-skills-audit.md" -Destination "ai/memory/00-skills-audit.md" -Force
git add ai/memory/00-skills-audit.md
git add src/Test.cs
git commit -m "feat: add test with skills audit"

# Expected: ✅ Hook passes
```

---

## 🔧 Troubleshooting

### Hook doesn't run

**Problem:** Git doesn't execute hooks in `.githooks/`

**Solution:** Ensure `git config core.hooksPath .githooks` is set

**Verification:**
```powershell
git config --get core.hooksPath
```

### PowerShell script execution blocked

**Problem:** `pre-commit: cannot be loaded because running scripts is disabled`

**Solution:** Set PowerShell execution policy (one-time):

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Hook runs but doesn't detect files

**Problem:** Hook runs but doesn't validate implementation files

**Solution:** Ensure files are staged with `git add`

**Debug:**
```powershell
# Show staged files
git diff --cached --name-only

# Should show your src/ files
```

---

## 🚨 Emergency Bypass

**Only use when absolutely necessary** (e.g., hotfix deployment):

```powershell
git commit --no-verify -m "emergency: critical fix"
```

**⚠️ WARNING:** Bypassing the hook violates skills-first architecture and creates technical debt.

---

## 📚 See Also

- [ai/memory/rules.md](../ai/memory/rules.md) - Full architecture rules
- [ai/memory/00-skills-audit.md](../ai/memory/00-skills-audit.md) - Skills audit template
- [C:\Projects\.github\ARCHITECTURE.md](../../.github/ARCHITECTURE.md) - Centralized architecture

---

**Last Updated:** 2026-02-06  
**Status:** Active
