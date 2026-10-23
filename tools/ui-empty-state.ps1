# UI check of the empty Changes page (owner's report on 1.0.2): pwsh tools/ui-empty-state.ps1 from the repo root.
# A Debug build with CHANGELEDGER_DATA_DIR on an empty folder in C:\Demo, so real data is never read or written and an
# installed copy keeps running. Checks: the message and its button are centred on the page at two window sizes;
# the button takes a snapshot, after which the timeline has a day and the first-snapshot message replaces the empty one.
# Takes the desktop lock C:\Demo\desktop.lock (00-shared §9.6). Exit code 0 = passed.
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace UiCheck -Name W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);'
$A = [Windows.Automation.AutomationElement]; $TS = [Windows.Automation.TreeScope]
$App = (Get-ChildItem src -Directory | Select-Object -First 1).Name
dotnet build "src/$App" -c Debug | Out-Null
$exe = "src/$App/bin/Debug/net10.0-windows/$App.exe"
$data = 'C:\Demo\changeledger-ui-check'
Remove-Item $data -Recurse -Force -ErrorAction Ignore
$failed = 0
function Check($ok, $what) { Write-Host "$(if ($ok) { 'PASS' } else { 'FAIL' }) $what"; if (-not $ok) { $script:failed++ } }
function Find($root, [scriptblock]$where) { $root.FindAll($TS::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object -FilterScript $where | Select-Object -First 1 }
function Centred($w, $size) {
  $frame = (Find $w { $_.Current.ClassName -eq 'Frame' }).Current.BoundingRectangle # the NavigationView frame that holds the page
  $text = (Find $w { $_.Current.Name -like 'No snapshots yet*' }).Current.BoundingRectangle
  $btn = Find $w { $_.Current.Name -eq 'Snapshot now' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button }
  $b = $btn.Current.BoundingRectangle
  $dx = [int](($b.Left + $b.Right) / 2 - ($frame.Left + $frame.Right) / 2)
  $dy = [int](($text.Top + $b.Bottom) / 2 - ($frame.Top + $frame.Bottom) / 2) # the page keeps a 16 px bottom margin: -8 is centred
  Check ([Math]::Abs($dx) -le 12 -and [Math]::Abs($dy) -le 24) "empty state centred at $size (dx=$dx, dy=$dy)"
  $btn
}

$lock = 'C:\Demo\desktop.lock'
while ($true) {
  try { New-Item $lock -Value $App -ErrorAction Stop | Out-Null; break }
  catch { if (-not (Test-Path $lock)) { throw }; Write-Host "desktop.lock is held by $(Get-Content $lock -ErrorAction Ignore), waiting..."; Start-Sleep 10 }
}
$env:CHANGELEDGER_DATA_DIR = $data
$p = $null
try {
  $p = Start-Process $exe -PassThru -ArgumentList '--page', 'changes', '--lang', 'en', '--size', '1183x700'
  $w = $null
  for ($i = 0; $i -lt 60 -and -not $w; $i++) { Start-Sleep -Milliseconds 500; $w = $A::RootElement.FindFirst($TS::Children, [Windows.Automation.PropertyCondition]::new($A::ProcessIdProperty, $p.Id)) }
  if (-not $w) { throw 'no window' }
  Start-Sleep 3
  $null = Centred $w '1183x700'
  [UiCheck.W]::SetWindowPos([IntPtr]$w.Current.NativeWindowHandle, [IntPtr]::Zero, 100, 60, 1500, 900, 0x0004) | Out-Null # SWP_NOZORDER
  Start-Sleep 2
  $btn = Centred $w '1500x900'
  $btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  $file = $null
  for ($i = 0; $i -lt 60 -and -not $file; $i++) { Start-Sleep -Milliseconds 500; $file = Get-ChildItem "$data\snapshots" -Filter *.json.gz -ErrorAction SilentlyContinue | Select-Object -First 1 }
  Check ([bool]$file) "the button took a snapshot$(if ($file) { " ($($file.Name))" })"
  Start-Sleep 3
  $list = Find $w { $_.Current.ClassName -eq 'ListBox' }
  $days = if ($list) { $list.FindAll($TS::Children, [Windows.Automation.Condition]::TrueCondition).Count } else { 0 }
  Check ($days -gt 0) "the timeline is not empty ($days day(s))"
  Check ([bool](Find $w { $_.Current.Name -like 'The first snapshot*' })) 'the first-snapshot message replaced the empty one'
} finally {
  if ($p -and -not $p.HasExited) { $p | Stop-Process }
  Remove-Item Env:CHANGELEDGER_DATA_DIR
  Remove-Item $lock
  Start-Sleep 1
  Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}
exit $failed
