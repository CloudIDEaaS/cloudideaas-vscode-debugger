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
