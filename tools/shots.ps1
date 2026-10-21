# README screenshots (00-shared §7): pwsh tools/shots.ps1 [-Pages main,settings]
# Every page × en/ru × light/dark, rendered by the app itself at 2× (2200×1400) under --demo,
# into docs/<page>-<theme>-<lang>.png, then oxipng. Run from the repo root; the path must have no spaces.
param([string[]]$Pages = @('main', 'settings'))
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
$Pages = $Pages -split ','   # pwsh -File passes "main,settings" as one string
$App =(Get-ChildItem src -Directory | Select-Object -First 1).Name
dotnet build "src/$App" -c Release | Out-Null
$exe = "src/$App/bin/Release/net10.0-windows/$App.exe"
New-Item -ItemType Directory docs -Force | Out-Null

# Desktop lock (00-shared §9.6): the window is topmost and would get into another session's GIF
$lock = 'C:\Demo\desktop.lock'
while ($true) {
  try { New-Item $lock -Value $App -ErrorAction Stop | Out-Null; break }
  catch { if (-not (Test-Path $lock)) { throw }; Write-Host "desktop.lock is held by $(Get-Content $lock -ErrorAction Ignore), waiting..."; Start-Sleep 10 }
}
try {
  foreach ($lang in 'en', 'ru') { foreach ($theme in 'light', 'dark') { foreach ($p in $Pages) {
    $png = "$PWD\docs\$p-$theme-$lang.png"
    $proc = Start-Process $exe -PassThru -ArgumentList '--demo', '--lang', $lang, '--theme', $theme, '--page', $p,
      '--size', '1100x700', '--screenshot', $png
    if (-not $proc.WaitForExit(30000)) { $proc | Stop-Process; throw "timeout: $png" }
    if ($proc.ExitCode -ne 0) { throw "exit $($proc.ExitCode): $png" }
  }}}
} finally { Remove-Item $lock }
oxipng -o 4 --strip safe -q -r docs
Get-ChildItem docs -Filter *.png | Select-Object Name, @{ n = 'KB'; e = { [int]($_.Length / 1KB) } } | Format-Table -AutoSize
