# GitHub social preview 1280×640 (00-shared §7): pwsh tools/social.ps1 -Title "VRAM Bouncer" -Subtitle "Frees VRAM while you play"
# Uses docs/main-dark-en.png from shots.ps1. Upload by hand: Settings → General → Social preview (§5).
param(
  [Parameter(Mandatory)][string]$Title,
  [Parameter(Mandatory)][string]$Subtitle
)
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
if ("$Title$Subtitle" -match "['`:\\%]") { throw "no ' : \ % in the texts: drawtext would need escaping" }
$shot = 'docs/main-dark-en.png'
if (-not (Test-Path $shot)) { throw "$shot not found: run tools/shots.ps1 first" }
ffmpeg -y -loglevel error -f lavfi -i "color=c=0x1c1c1c:s=1280x640" -i $shot -frames:v 1 -filter_complex `
  ("[1]scale=1000:-1[s];[0][s]overlay=(W-w)/2:190," +
   "drawtext=fontfile='C\:/Windows/Fonts/segoeuib.ttf':text='$Title':fontsize=60:fontcolor=white:x=(w-text_w)/2:y=50," +
   "drawtext=fontfile='C\:/Windows/Fonts/segoeui.ttf':text='$Subtitle':fontsize=28:fontcolor=0xC8C8C8:x=(w-text_w)/2:y=130") `
  docs/social.png
oxipng -o 4 --strip safe -q docs/social.png
$kb = [int]((Get-Item docs/social.png).Length / 1KB)
Write-Host "docs/social.png: $kb KB$(if ($kb -ge 1024) { ' (GitHub needs < 1 MB)' })"
