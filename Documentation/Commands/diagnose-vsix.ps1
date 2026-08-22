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
$tracked = git ls-files -- $FilePath 2>$null
if ([string]::IsNullOrEmpty($tracked)) {
  Write-Host "not tracked in working tree"
} else {
  Write-Host $tracked
}

Write-Host "`n3) Status for that path:" -ForegroundColor Cyan
git status --porcelain -- $FilePath

Write-Host "`n4) Search history for that filename (may be slow):" -ForegroundColor Cyan
$base = [System.IO.Path]::GetFileName($FilePath)
$historyMatches = git rev-list --all --objects | Select-String -Pattern $base -SimpleMatch
if ($historyMatches) {
  $historyMatches
} else {
  Write-Host "not found in history by basename"
}

Write-Host "`n5) Commits that reference that path (if any):" -ForegroundColor Cyan
git log --all --pretty=format:"%H %an %ad" --name-only | Select-String -Pattern $base -SimpleMatch -Context 1,0

Write-Host "`n6) All local refs (branches + tags):" -ForegroundColor Cyan
git for-each-ref --format="%(refname:short)" refs/heads refs/tags
