# Create all helper PowerShell command files for VSIX cleanup.
param(
  [string]$TargetDir = "C:\CloudIDEaaS\develop\VSCodeDebugger\Documentation\Commands"
)

$files = @{
"diagnose-vsix.ps1" = @'
# Diagnostics for the VSIX problem. Run from repo root in PowerShell.
param(
  [string]$FilePath = "Extension/cloudideaas-vscode-debugger/cloudideaas-vscode-debugger-win32-x64-0.1.0.vsix"
)

Write-Host "Repo root:" (git rev-parse --show-toplevel) -ForegroundColor Cyan
Write-Host "`nRemotes:" -ForegroundColor Cyan
git remote -v

Write-Host "`n1) .gitignore match (empty = no match):" -ForegroundColor Cyan
git check-ignore -v -- $FilePath 2>$null

Write-Host "`n2) Is file tracked in working tree / index:" -ForegroundColor Cyan
git ls-files -- $FilePath || Write-Host "not tracked in working tree"

Write-Host "`n3) Status for that path:" -ForegroundColor Cyan
git status --porcelain -- $FilePath

Write-Host "`n4) Search history for that filename (may be slow):" -ForegroundColor Cyan
$base = [System.IO.Path]::GetFileName($FilePath)
git rev-list --all --objects | Select-String -Pattern $base -SimpleMatch -Quiet
if ($LASTEXITCODE -ne 0) {
  git rev-list --all --objects | Select-String -Pattern $base -SimpleMatch
} else {
  Write-Host "found in history (quiet) - run without -Quiet to see details"
}

Write-Host "`n5) Commits that reference that path (if any):" -ForegroundColor Cyan
git log --all --pretty=format:"%H %an %ad" --name-only | Select-String -Pattern $base -SimpleMatch -Context 1,0

Write-Host "`n6) All local refs (branches + tags):" -ForegroundColor Cyan
git for-each-ref --format="%(refname:short)" refs/heads refs/tags
'@

"untrack-vsix.ps1" = @'
# Untrack the specific VSIX in the working tree/index, keep local file.
param(
  [string]$FilePath = "Extension/cloudideaas-vscode-debugger/cloudideaas-vscode-debugger-win32-x64-0.1.0.vsix"
)

# Ensure we are in repo root
Write-Host "Running git ls-files for $FilePath" -ForegroundColor Cyan
$tracked = git ls-files -- $FilePath 2>$null
if ($tracked) {
  Write-Host "File is tracked. Removing from index (keeps local copy)..." -ForegroundColor Yellow
  git rm --cached -- $FilePath
  git commit -m "Stop tracking VSIX: $FilePath"
  Write-Host "Pushing commit to origin..." -ForegroundColor Yellow
  git push
  Write-Host "Done."
} else {
  Write-Host "File not tracked in this working tree: $FilePath" -ForegroundColor Green
  Write-Host "If push still fails the blob may be in history or in another ref/branch."
}
'@

"remove-vsix-history-filter-repo.ps1" = @'
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
'@

"remove-vsix-history-bfg.ps1" = @'
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
'@

"fix-gitignore.ps1" = @'
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
'@

"restore-gitignore.ps1" = @'
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
'@
}

# create directory
if (-not (Test-Path $TargetDir)) {
  New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}

foreach ($name in $files.Keys) {
  $full = Join-Path $TargetDir $name
  $content = $files[$name]
  $content | Out-File -FilePath $full -Encoding UTF8 -Force
  Write-Host "Created: $full"
}

Write-Host "`nAll files created in $TargetDir. Run the diagnostic script first:" -ForegroundColor Green
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$TargetDir\diagnose-vsix.ps1`""