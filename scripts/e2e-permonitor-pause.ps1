<#
.SYNOPSIS
    Live multi-monitor attestation for the HYPNIX per-monitor pause rule.

.DESCRIPTION
    Verifies, on the real desktop with two displays, that "Active display only" + "Maximized or fullscreen
    apps" behaves as specified:
      1. An app covering one monitor freezes ONLY that monitor; a clean monitor keeps animating.
      2. Apps covering all monitors freeze all monitors.
      3. Minimizing until a monitor is clean makes it animate again.
    It also confirms that transparent/tool overlays (e.g. the NVIDIA GeForce overlay) do NOT pause a monitor.

    Proof is twofold: authoritative internal logs ("Foreground state changed ... Covered=[...]" and
    "Native host monitor pause changed. Monitors=...") plus a frame-diff capture of clean monitors (covered
    monitors cannot be captured because the covering window is on top, so those are proven from the logs).

    The user's settings.json is backed up and always restored. MUST run in Windows PowerShell 5.1 (the
    UIAutomation client assemblies are unavailable under PowerShell 7 / .NET Core).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\e2e-permonitor-pause.ps1
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run this script in Windows PowerShell 5.1; UIAutomation client assemblies are unavailable under PowerShell 7.'
}

$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $Exe) { $Exe = Join-Path $projectRoot "bin\$Configuration\net8.0-windows\HYPNIX.exe" }
if (-not (Test-Path -LiteralPath $Exe)) { throw "HYPNIX.exe not found at '$Exe'. Build it first (dotnet build -c $Configuration)." }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$screens = [System.Windows.Forms.Screen]::AllScreens
if ($screens.Count -lt 2) {
    Write-Host "PER-MONITOR RULE: SKIP (needs two displays; found $($screens.Count))." -ForegroundColor Yellow
    exit 0
}

$Auto = [Windows.Automation.AutomationElement]
$TS   = [Windows.Automation.TreeScope]
$settings = Join-Path $env:LOCALAPPDATA 'HYPNIX\settings.json'
$backup   = "$settings.permonbak"
$log      = Join-Path $env:LOCALAPPDATA 'HYPNIX\logs\wallpaper.log'
$shell    = New-Object -ComObject Shell.Application
$proc = $null
$covers = @{}
$fail = New-Object System.Collections.Generic.List[string]
$pass = New-Object System.Collections.Generic.List[string]

function Find-ByAutoId($root, $id) { $root.FindFirst($TS::Descendants, (New-Object Windows.Automation.PropertyCondition($Auto::AutomationIdProperty, $id))) }
function Invoke-El($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Select-Item($root, $idx) {
    $g = Find-ByAutoId $root 'WallpaperGallery'
    $items = $g.FindAll($TS::Descendants, (New-Object Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
    $items.Item($idx).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Pump([int]$ms) { $end = (Get-Date).AddMilliseconds($ms); while ((Get-Date) -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 } }
function Log-Count { if (Test-Path $log) { (Get-Content $log).Count } else { 0 } }
function Log-Since([int]$n) { if (Test-Path $log) { Get-Content $log | Select-Object -Skip $n } else { @() } }
function New-Cover([int]$idx) {
    $b = $screens[$idx].Bounds
    $f = New-Object System.Windows.Forms.Form
    $f.Text = "COVER-$idx"; $f.BackColor = [System.Drawing.Color]::DarkRed
    $f.StartPosition = 'Manual'
    $f.Location = New-Object System.Drawing.Point(($b.X + 60), ($b.Y + 60))
    $f.Size = New-Object System.Drawing.Size(500, 360)
    $f.Show(); $f.Activate(); $f.WindowState = 'Maximized'
    $covers[$idx] = $f
    Pump 400
}
function Get-Pixels($bmp) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    ,$bytes
}
function Grab($idx) {
    $b = $screens[$idx].Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $g.Dispose()
    $bmp
}
function Animation-Fraction($idx) {
    $a = Grab $idx; $ba = Get-Pixels $a
    Pump 700
    $b = Grab $idx; $bb = Get-Pixels $b
    $a.Dispose(); $b.Dispose()
    $diff = 0; $tot = 0
    for ($i = 0; $i -lt $ba.Length; $i += 16) { $tot++; if ([math]::Abs([int]$ba[$i] - [int]$bb[$i]) -gt 12) { $diff++ } }
    [math]::Round($diff / [math]::Max(1, $tot), 4)
}
function Check($name, $cond) { if ($cond) { $pass.Add($name) } else { $fail.Add($name) } }

try {
    if (-not (Test-Path $settings)) {
        $seed = Start-Process -FilePath $Exe -PassThru; Start-Sleep -Seconds 6
        Stop-Process -Id $seed.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
    }
    if (-not (Test-Path $settings)) { throw "settings.json was not created at '$settings'." }
    Copy-Item $settings $backup -Force
    $cfg = Get-Content $settings -Raw | ConvertFrom-Json
    $cfg.AppPauseMode = 2; $cfg.PausePerMonitor = $true; $cfg.PauseOnBattery = $false
    $cfg.SelectedWallpaperId = 'built-in-ambient'
    ($cfg | ConvertTo-Json -Depth 8) | Set-Content $settings -Encoding UTF8
    Write-Host "settings: AppPauseMode=2, PausePerMonitor=true, wallpaper=ambient (backup at $backup)"

    $proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 6
    $cond = New-Object Windows.Automation.PropertyCondition($Auto::ProcessIdProperty, $proc.Id)
    $root = $null
    for ($i = 0; $i -lt 20 -and -not $root; $i++) { $root = $Auto::RootElement.FindFirst($TS::Children, $cond); if (-not $root) { Start-Sleep -Milliseconds 300 } }
    if (-not $root) { throw 'HYPNIX window not found via UI Automation.' }
    Select-Item $root 0
    Invoke-El (Find-ByAutoId $root 'StartButton')
    Write-Host 'clicked Start (ambient)'
    Pump 3000
    try { $root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Minimized') } catch {}

    # --- Phase A: clean desktop -> both monitors animate, nothing paused ---
    $mark = Log-Count
    $shell.MinimizeAll()
    Pump 3500
    $a = Log-Since $mark
    Check 'A: detection reports Covered=[] (clean)' ([bool]($a | Select-String 'Covered=\[\];' | Select-Object -Last 1))
    $fa0 = Animation-Fraction 0; $fa1 = Animation-Fraction 1
    Write-Host "PhaseA animation: mon0=$fa0 mon1=$fa1"
    Check 'A: monitor 0 animates when clean (overlay ignored)' ($fa0 -gt 0.005)
    Check 'A: monitor 1 animates when clean' ($fa1 -gt 0.005)

    # --- Phase B: cover monitor 0 -> only mon0 pauses; mon1 keeps animating (Rule 1) ---
    $mark = Log-Count
    New-Cover 0
    Pump 3500
    $b = Log-Since $mark
    $covB = ($b | Select-String 'Foreground state changed' | Select-Object -Last 1).Line
    $pauseB = ($b | Select-String 'Native host monitor pause changed' | Select-Object -Last 1).Line
    Check 'B: detection reports Covered=[0]' ($covB -match 'Covered=\[0\];')
    Check 'B: host pauses Monitors=0 only' ($pauseB -match 'Monitors=0$')
    $fb1 = Animation-Fraction 1
    Write-Host "PhaseB clean monitor 1 animation: $fb1"
    Check 'B: monitor 1 (clean) keeps animating [Rule 1]' ($fb1 -gt 0.005)

    # --- Phase C: cover monitor 1 too -> both pause (Rule 2) ---
    $mark = Log-Count
    New-Cover 1
    Pump 3500
    $c = Log-Since $mark
    Check 'C: detection reports Covered=[0,1]' ([bool]($c | Select-String 'Covered=\[0,1\]' | Select-Object -Last 1))
    Check 'C: host pauses Monitors=0,1 (all) [Rule 2]' ([bool]($c | Select-String 'Native host monitor pause changed\. Monitors=0,1$' | Select-Object -Last 1))

    # --- Phase D: minimize both -> both animate again (Rule 3) ---
    $mark = Log-Count
    foreach ($f in $covers.Values) { $f.WindowState = 'Minimized' }
    Pump 3500
    $d = Log-Since $mark
    Check 'D: detection reports Covered=[] (clean)' ([bool]($d | Select-String 'Covered=\[\];' | Select-Object -Last 1))
    Check 'D: host resumes Monitors=none [Rule 3]' ([bool]($d | Select-String 'Native host monitor pause changed\. Monitors=none$' | Select-Object -Last 1))
    $fd0 = Animation-Fraction 0; $fd1 = Animation-Fraction 1
    Write-Host "PhaseD animation: mon0=$fd0 mon1=$fd1"
    Check 'D: monitor 0 animates again [Rule 3]' ($fd0 -gt 0.005)
    Check 'D: monitor 1 animates again [Rule 3]' ($fd1 -gt 0.005)
}
catch { $fail.Add("EXCEPTION: $($_.Exception.Message)") }
finally {
    foreach ($f in $covers.Values) { try { $f.Close(); $f.Dispose() } catch {} }
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1
    if (Test-Path $backup) { Copy-Item $backup $settings -Force; Remove-Item $backup -Force; Write-Host 'settings restored from backup' }
    if (Get-Process -Name HYPNIX -ErrorAction SilentlyContinue) { $fail.Add('HYPNIX still running after cleanup') }
}

Write-Host ''
$pass | ForEach-Object { Write-Host "  PASS  $_" -ForegroundColor Green }
if ($fail.Count -eq 0) {
    Write-Host "PER-MONITOR RULE: PASS ($($pass.Count) checks)" -ForegroundColor Green
    exit 0
} else {
    $fail | ForEach-Object { Write-Host "  FAIL  $_" -ForegroundColor Red }
    Write-Host 'PER-MONITOR RULE: FAIL' -ForegroundColor Red
    exit 1
}
