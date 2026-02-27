# Initialize Skills-First Architecture Enforcement
# Run this script once to enable pre-commit hooks

Write-Host "🚀 Initializing Skills-First Architecture Enforcement" -ForegroundColor Cyan
Write-Host ""

# Check if .git exists
if (-not (Test-Path ".git")) {
    Write-Host "⚠️  This is not a git repository" -ForegroundColor Yellow
    $initGit = Read-Host "Initialize git repository? (y/N)"
    
    if ($initGit -eq "y" -or $initGit -eq "Y") {
        git init
        Write-Host "✅ Git repository initialized" -ForegroundColor Green
    } else {
        Write-Host "❌ Skipping - git required for hooks" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "📋 Configuring Git hooks..." -ForegroundColor Cyan

# Configure hooks path
git config core.hooksPath .githooks

# Verify configuration
$hooksPath = git config --get core.hooksPath

if ($hooksPath -eq ".githooks") {
    Write-Host "✅ Git hooks path configured: .githooks" -ForegroundColor Green
} else {
    Write-Host "❌ Failed to configure hooks path" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🧩 Checking skills audit..." -ForegroundColor Cyan

# Check if skills audit exists
if (Test-Path "ai/memory/00-skills-audit.md") {
    Write-Host "✅ Skills audit exists: ai/memory/00-skills-audit.md" -ForegroundColor Green
} else {
    Write-Host "⚠️  Skills audit not found" -ForegroundColor Yellow
    
    # Check if template exists
    if (Test-Path "ai/memory/00-skills-audit-template.md") {
        Write-Host ""
        $createAudit = Read-Host "Create from template? (y/N)"
        
        if ($createAudit -eq "y" -or $createAudit -eq "Y") {
            Copy-Item "ai/memory/00-skills-audit-template.md" "ai/memory/00-skills-audit.md"
            Write-Host "✅ Created ai/memory/00-skills-audit.md from template" -ForegroundColor Green
            Write-Host ""
            Write-Host "📝 TODO: Complete the skills audit before creating implementation files" -ForegroundColor Yellow
            Write-Host "   1. Review C:\Projects\.github\skills\manifest.json" -ForegroundColor White
            Write-Host "   2. Review C:\Projects\.github\agents\manifest.json" -ForegroundColor White
            Write-Host "   3. Fill out ai/memory/00-skills-audit.md with actual project info" -ForegroundColor White
        }
    } else {
        Write-Host "❌ Template not found: ai/memory/00-skills-audit-template.md" -ForegroundColor Red
        Write-Host "   Create skills audit manually before creating implementation files" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "🧪 Testing hooks..." -ForegroundColor Cyan

# Test if hooks are executable
if (Test-Path ".githooks/pre-commit") {
    Write-Host "✅ Pre-commit hook found" -ForegroundColor Green
    
    # On Linux/Mac, check if executable
    if ($IsLinux -or $IsMacOS) {
        $permissions = (Get-Item ".githooks/pre-commit").UnixMode
        if ($permissions -match "x") {
            Write-Host "✅ Hook is executable" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Making hook executable..." -ForegroundColor Yellow
            chmod +x .githooks/pre-commit
            Write-Host "✅ Hook is now executable" -ForegroundColor Green
        }
    }
} else {
    Write-Host "❌ Pre-commit hook not found: .githooks/pre-commit" -ForegroundColor Red
}

Write-Host ""
Write-Host "✅ Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📚 Next steps:" -ForegroundColor Cyan
Write-Host "   1. Complete ai/memory/00-skills-audit.md (if not done)" -ForegroundColor White
Write-Host "   2. Create implementation files (src/, Services/, etc.)" -ForegroundColor White
Write-Host "   3. Commit changes - hook will validate skills audit exists" -ForegroundColor White
Write-Host ""
Write-Host "📖 See .githooks/README.md for more details" -ForegroundColor Gray
Write-Host ""
