# README GIF (00-shared §7), 8 s, width 800, 15 fps, target ≤ 3 MB. Run from the repo root.
#   Window GIF: pwsh tools/gif.ps1 [-Lang ru]           the app window found via --rect-file
#   Scene GIF:  pwsh tools/gif.ps1 -Scene [-Lang ru]    fixed 1280×720 area, centre of DISPLAY1 by default;
#               only in the owner's slot (Do not disturb, plain wallpaper, nothing personal on screen)
# Takes the desktop lock C:\Demo\desktop.lock (00-shared §9.6): waits while another session holds it, never removes it.
param(
  [ValidateSet('en', 'ru')][string]$Lang = 'en',
  [switch]$Scene,
  [int]$X = 1080, [int]$Y = 344, [int]$W = 1280, [int]$H = 720,
  [int]$Seconds = 8,
  [int]$Fps = 15, [int]$Width = 800,   # over 3 MB: -Fps 12 or -Width 720
  [string]$Out = "docs/demo-$Lang.gif"
)
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
$App = (Get-ChildItem src -Directory | Select-Object -First 1).Name
dotnet build "src/$App" -c Release | Out-Null
$exe = "src/$App/bin/Release/net10.0-windows/$App.exe"
$raw = "$env:TEMP\$App-raw.mkv"; $pal = "$env:TEMP\$App-pal.png"   # own names: sessions record in parallel
$rect = "$env:TEMP\$App-rect.txt"; Remove-Item $rect -ErrorAction Ignore
$gif = $Out
New-Item -ItemType Directory (Split-Path $gif) -Force | Out-Null

$lock = 'C:\Demo\desktop.lock'
while ($true) {
  try { New-Item $lock -Value $App -ErrorAction Stop | Out-Null; break }
  catch { if (-not (Test-Path $lock)) { throw }; Write-Host "desktop.lock is held by $(Get-Content $lock -ErrorAction Ignore), waiting..."; Start-Sleep 10 }
}
try {
  $proc = Start-Process $exe -PassThru -ArgumentList '--demo', '--autoplay', '--lang', $Lang, '--theme', 'dark', '--size', '1000x620', '--rect-file', $rect
  $deadline = (Get-Date).AddSeconds(30)
  while (-not (Test-Path $rect)) {
    if ((Get-Date) -gt $deadline -or $proc.HasExited) { throw 'the app did not write --rect-file' }
    Start-Sleep -Milliseconds 100
  }
  if (-not $Scene) { $X, $Y, $W, $H = (Get-Content $rect) -split ' ' }
  ffmpeg -y -loglevel error -f gdigrab -framerate 30 -draw_mouse ([int][bool]$Scene) -offset_x $X -offset_y $Y -video_size "${W}x${H}" `
    -t $Seconds -i desktop -c:v libx264rgb -crf 0 -preset ultrafast $raw
  $proc | Stop-Process -ErrorAction Ignore
} finally { Remove-Item $lock }

$f = "fps=$Fps,scale=${Width}:-1:flags=lanczos"
ffmpeg -y -loglevel error -i $raw -vf "$f,palettegen=stats_mode=diff" $pal
ffmpeg -y -loglevel error -i $raw -i $pal -map_metadata -1 `
  -lavfi "$f[v];[v][1:v]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" $gif
Remove-Item $raw, $pal
$mb = (Get-Item $gif).Length / 1MB
Write-Host ("{0}: {1:N2} MB{2}" -f $gif, $mb, $(if ($mb -gt 3) { ' (over 3 MB: try -Fps 12 or -Width 720)' }))
