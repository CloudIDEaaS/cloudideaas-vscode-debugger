# Restore .gitignore from HEAD (discard local uncommitted changes).
param(
  [string]$GitIgnorePath = ".gitignore"
)
if (Test-Path $GitIgnorePath) {
  Write-Host "Restoring $GitIgnorePath from HEAD (discard local changes)..." -ForegroundColor Yellow
  git restore $GitIgnorePath 2>$null
  if ($LASTEXITCODE -eq 0) { Write-Host "Restored." -ForegroundColor Green } else { Write-Host "git restore failed; try 'git checkout -- .gitignore'." -ForegroundColor Red }
} else {
  Write-Host "$GitIgnorePath not found." -ForegroundColor Red
}
