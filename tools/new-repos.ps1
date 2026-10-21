# Fan-out (00-shared §5, §9.1): 8 private repos from the template, one after another: clone, rename, build, push.
#   pwsh tools/new-repos.ps1 -Root C:\Users\oneno\tools
# Only after the template is frozen and the owner said yes. Safe to re-run: existing repos and folders are skipped.
param([Parameter(Mandatory)][string]$Root)
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
$apps = [ordered]@{
  PingWhen = 'PingWhen'; VramBouncer = 'VRAM Bouncer'; Lazarus = 'Lazarus'; EdgeTuck = 'Edge Tuck'
  Sidechain = 'Sidechain'; ChangeLedger = 'Change Ledger'; Reclaimer = 'Reclaimer'; DownloadsLifecycle = 'Downloads Lifecycle'
}
foreach ($App in $apps.Keys) {
  $dir = Join-Path $Root $App
  if (Test-Path $dir) { Write-Host "$App`: $dir exists, skipped"; continue }
  & { $PSNativeCommandUseErrorActionPreference = $false; gh repo view "lojekq/$App" *> $null }
  if ($LASTEXITCODE -ne 0) { gh repo create "lojekq/$App" --private --template lojekq/TrayAppTemplate | Out-Null }
  do {   # while the template is being copied the repo is empty (409): too early to clone
    Start-Sleep 5
    & { $PSNativeCommandUseErrorActionPreference = $false; gh api "repos/lojekq/$App/commits?per_page=1" *> $null }
  } until ($LASTEXITCODE -eq 0)
  git clone -q "https://github.com/lojekq/$App" $dir
  Push-Location $dir
  try {
    git config user.name (git -C $PSScriptRoot config user.name)
    git config user.email (git -C $PSScriptRoot config user.email)
    pwsh -NoProfile -File tools/rename.ps1 -Name $App -Display $apps[$App]
    dotnet build -c Release | Out-Null
    git add -A
    git commit -q -m "Rename TrayApp to $App"
    git push -q
    Write-Host "$App`: ok"
  } finally { Pop-Location }
}
