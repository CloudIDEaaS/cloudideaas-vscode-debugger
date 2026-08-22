<#
Remove all *.vsix files from history using BFG Repo-Cleaner.
WARNING: This rewrites history. Requires Java and BFG installed.
Set $RepoUrl to your remote repo URL.
#>
param(
  [string]$RepoUrl = "https://github.com/CloudIDEaaS/cloudideaas-vscode-debugger.git"
)

Write-Host "Cloning mirror from $RepoUrl..." -ForegroundColor Yellow
git clone --mirror $RepoUrl repo.git
if ($LASTEXITCODE -ne 0) { Write-Host "Clone failed." -ForegroundColor Red; exit 1 }

Set-Location repo.git
Write-Host "Running BFG to delete '*.vsix' files from history..." -ForegroundColor Yellow
bfg --delete-files '*.vsix'
if ($LASTEXITCODE -ne 0) { Write-Host "BFG failed or not installed." -ForegroundColor Red; exit 1 }

Write-Host "Cleaning reflog and GC..." -ForegroundColor Yellow
git reflog expire --expire=now --all
git gc --prune=now --aggressive

Write-Host "Force-pushing cleaned history (WARNING: rewrites history)..." -ForegroundColor Red
git push --force
Write-Host "Done."
