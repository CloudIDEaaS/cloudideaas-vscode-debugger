<#
Remove a specific path from all history with git-filter-repo.
WARNING: This rewrites history. Coordinate with collaborators. You must have git-filter-repo installed.
Run this from anywhere; it makes a mirror clone and pushes cleaned history back to origin.
#>
param(
  [string]$PathToRemove = "Extension/cloudideaas-vscode-debugger/cloudideaas-vscode-debugger-win32-x64-0.1.0.vsix"
)

$origin = (git remote get-url origin) -replace "`n",""
if (-not $origin) {
  Write-Host "Cannot determine origin remote. Set remote or run from repo cloned from remote." -ForegroundColor Red
  exit 1
}

Write-Host "Cloning mirror from $origin (this creates repo-clean.git)..." -ForegroundColor Yellow
git clone --mirror $origin repo-clean.git
if ($LASTEXITCODE -ne 0) { Write-Host "Clone failed." -ForegroundColor Red; exit 1 }

Set-Location repo-clean.git
Write-Host "Running git-filter-repo to remove path: $PathToRemove" -ForegroundColor Yellow
git filter-repo --invert-paths --path $PathToRemove
if ($LASTEXITCODE -ne 0) {
  Write-Host "git-filter-repo failed. Ensure it's installed and try again." -ForegroundColor Red
  exit 1
}

Write-Host "Cleaning reflog and GC..." -ForegroundColor Yellow
git reflog expire --expire=now --all
git gc --prune=now --aggressive

Write-Host "Force-pushing cleaned history to origin (all branches + tags). WARNING: rewrites history." -ForegroundColor Red
git push --force --all
git push --force --tags

Write-Host "Done. Remove repo-clean.git when finished." -ForegroundColor Green
