#Requires -Version 7.4
<#
Sign a draft release and publish it (plan 00-shared §4.4, item 4). The owner runs it, not CI: Certum asks for a code from the phone.
Before: sign in to SimplySign Desktop; $env:CERTUM_THUMBPRINT is set; the repo has vars.SIGN = 1, so CI made a draft.
Run from the repo root:  pwsh tools/sign-release.ps1 v0.2.0
#>
param([Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true

if (-not $env:CERTUM_THUMBPRINT) { throw 'CERTUM_THUMBPRINT is not set (certificate thumbprint, see §4.4 item 3.5)' }
$signtool = (Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1).FullName
if (-not $signtool) { throw 'signtool.exe not found: install the Windows SDK (§4.4 item 3.5)' }

$d = Join-Path $env:TEMP "sign-$Tag-$(Get-Random)"
gh release download $Tag -D $d
$zip = Get-ChildItem $d -Filter '*-fd.zip'
$app = $zip.Name -replace '-\d.*$'                      # PingWhen-0.2.0-win-x64-fd.zip → PingWhen
Expand-Archive $zip.FullName "$d\fd"
$files = @((Get-ChildItem $d -Filter '*.exe').FullName) + "$d\fd\$app.exe"
& $signtool sign /sha1 $env:CERTUM_THUMBPRINT /fd sha256 /tr http://time.certum.pl /td sha256 @files
& $signtool verify /pa @files
Remove-Item $zip.FullName; Compress-Archive "$d\fd\*" $zip.FullName
Get-ChildItem $d -File | Where-Object Name -ne 'SHA256SUMS.txt' | Get-FileHash -Algorithm SHA256 |
  ForEach-Object { '{0}  {1}' -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) } | Set-Content "$d\SHA256SUMS.txt"
gh release upload $Tag (Get-ChildItem $d -File).FullName --clobber
gh release edit $Tag --draft=false
Remove-Item $d -Recurse -Force
"$app $Tag signed and published"
