# Add a repo-root .gitignore entry to ignore all .vsix files, commit and push if changed.
param(
  [string]$Pattern = "**/*.vsix",
  [string]$GitIgnorePath = ".gitignore"
)

if (-not (Test-Path $GitIgnorePath)) {
  Write-Host "No .gitignore found at repo root, creating one." -ForegroundColor Yellow
  New-Item -Path $GitIgnorePath -ItemType File -Force | Out-Null
}

$exists = Select-String -Path $GitIgnorePath -Pattern ([regex]::Escape($Pattern)) -SimpleMatch -Quiet
if (-not $exists) {
  Add-Content -Path $GitIgnorePath -Value "`n$Pattern"
  git add $GitIgnorePath
  git commit -m "Ignore VSIX artifacts ($Pattern)"
  git push
  Write-Host ".gitignore updated and pushed." -ForegroundColor Green
} else {
  Write-Host "Pattern already present in $GitIgnorePath" -ForegroundColor Green
}
