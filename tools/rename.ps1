# Turns the template into an app (00-shared §2.3): pwsh tools/rename.ps1 -Name VramBouncer -Display "VRAM Bouncer"
# Replaces the name in file contents (namespaces, ci.yml APP, update URL), sets the display name, drops the
# template-only block from the READMEs and renames files and folders. Run once, on a clean checkout.
param(
  [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$')][string]$Name,
  [string]$Display = $Name
)
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path $PSScriptRoot
Set-Location $root
$old = 'Tray' + 'App'   # split so that `git grep` finds nothing in this script after the rename (§8 item 5)
if (-not (Test-Path "src/$old")) { throw "src/$old not found: already renamed?" }
git rm -q tools/new-repos.ps1   # template-only: the fan-out runs once, from the template

# Contents: most specific first (repo URL, display name), then the identifier
foreach ($f in git ls-files) {
  if ($f -match '\.(png|ico|gif|jpg)$') { continue }
  $path = Join-Path $root $f
  $t = [IO.File]::ReadAllText($path)
  $n = $t.Replace("DisplayName = `"$old`"", "DisplayName = `"$Display`"").
          Replace("<Product>$old</Product>", "<Product>$Display</Product>").
          Replace("<AssemblyTitle>$old</AssemblyTitle>", "<AssemblyTitle>$Display</AssemblyTitle>").
          Replace("<Description>$old</Description>", "<Description>$Display</Description>").
          Replace("${old}Template", $Name).Replace($old, $Name)
  $n = [regex]::Replace($n, '(?s)\r?\n?', '')
  if ($n -cne $t) { [IO.File]::WriteAllText($path, $n) }
}

# Files and folders whose name contains the old name, deepest first so a parent moves after its children
$paths = foreach ($f in git ls-files) {
  $parts = $f -split '/'
  for ($i = 0; $i -lt $parts.Count; $i++) { if ($parts[$i].Contains($old)) { $parts[0..$i] -join '/' } }
}
$paths | Sort-Object -Unique | Sort-Object { ($_ -split '/').Count } -Descending | ForEach-Object {
  $leaf = ($_ -split '/')[-1]
  git mv $_ ($_.Substring(0, $_.Length - $leaf.Length) + $leaf.Replace($old, $Name))
}

Write-Host "Renamed to $Name ($Display). Check: git grep $old"
