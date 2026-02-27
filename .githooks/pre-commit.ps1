# Pre-Commit Hook: Skills Audit Validation
# Ensures ai/memory/00-skills-audit.md exists before allowing commits with src/ files
#
# Installation (one-time setup):
#   git config core.hooksPath .githooks
#
# To bypass (emergency only):
#   git commit --no-verify

Write-Host "[PRE-COMMIT] Skills-First Architecture Validation" -ForegroundColor Cyan
Write-Host ""

# Check if we're committing any files in src/ or Services/ or Models/ etc.
$stagedFiles = git diff --cached --name-only --diff-filter=ACM

$implementationFiles = $stagedFiles | Where-Object {
    $_ -match '^src/' -or
    $_ -match '^Services/' -or
    $_ -match '^Models/' -or
    $_ -match '^Validators/' -or
    $_ -match '^Configuration/' -or
    $_ -match '^Controllers/'
}

if ($implementationFiles.Count -eq 0) {
    Write-Host "[OK] No implementation files in commit - skipping skills audit check" -ForegroundColor Green
    exit 0
}

Write-Host "[INFO] Found $($implementationFiles.Count) implementation file(s) in commit:" -ForegroundColor Yellow
$implementationFiles | ForEach-Object { Write-Host "   - $_" -ForegroundColor Gray }
Write-Host ""

# Check if skills audit exists
$skillsAuditPath = "ai/memory/00-skills-audit.md"

if (-not (Test-Path $skillsAuditPath)) {
    Write-Host "[BLOCKED] COMMIT BLOCKED" -ForegroundColor Red
    Write-Host ""
    Write-Host "You are committing implementation files without a skills audit!" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Required: $skillsAuditPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Action Required:" -ForegroundColor Cyan
    Write-Host "  1. Review centralized skills: C:\Projects\.github\skills\manifest.json" -ForegroundColor White
    Write-Host "  2. Consult available agents: C:\Projects\.github\agents\manifest.json" -ForegroundColor White
    Write-Host "  3. Create skills audit documenting which skills are used/needed" -ForegroundColor White
    Write-Host "  4. See ai/memory/rules.md for full guidance" -ForegroundColor White
    Write-Host ""
    Write-Host "Why? This prevents reinventing existing skills and ensures consistency." -ForegroundColor Gray
    Write-Host ""
    Write-Host "To bypass (NOT recommended): git commit --no-verify" -ForegroundColor DarkGray
    Write-Host ""
    exit 1
}

# Check if skills audit is populated (not just template placeholders)
$auditContent = Get-Content $skillsAuditPath -Raw

if ($auditContent -match "_______________" -and $auditContent -notmatch "RETROACTIVE") {
    Write-Host "[WARNING] Skills audit appears incomplete" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Found template placeholders (_______________) in $skillsAuditPath" -ForegroundColor Yellow
    Write-Host "Please complete the audit with actual skills information." -ForegroundColor Yellow
    Write-Host ""

    $response = Read-Host "Continue anyway? (y/N)"
    if ($response -ne "y" -and $response -ne "Y") {
        Write-Host ""
        Write-Host "[BLOCKED] Commit cancelled - please complete skills audit" -ForegroundColor Red
        exit 1
    }
}

Write-Host "[OK] Skills audit found: $skillsAuditPath" -ForegroundColor Green
Write-Host "[OK] Pre-commit validation passed" -ForegroundColor Green
Write-Host ""
exit 0
