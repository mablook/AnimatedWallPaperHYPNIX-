<#
.SYNOPSIS
    End-to-end desktop smoke test for HYPNIX. Drives the real, built application through
    Windows UI Automation, attaches each wallpaper type to the live Windows desktop,
    exercises system-audio reactivity, and captures the desktop for visual attestation.

.DESCRIPTION
    Unlike Tests/Hypnix.NativeSmoke (which uses hidden parent windows and never touches
    Explorer), this script attaches wallpapers to the real desktop exactly as a user would:
    it clicks Start, switches wallpapers, plays audio into the default render endpoint so the
    WASAPI-loopback visualizers react, and screenshots the desktop with all windows minimized.
    On completion it presses Stop, restores the user's settings from a backup, and terminates
    the app. The user's settings.json is backed up and always restored (even on failure).

    MUST be run in Windows PowerShell 5.1 (the UIAutomation client assemblies are .NET
    Framework GAC assemblies and are not available under PowerShell 7 / .NET Core).

    Requires: a built HYPNIX.exe, Direct3D 11 hardware, an active interactive desktop
    session, an audio render endpoint, and ffmpeg/ffprobe (auto-detected or via -FfmpegDir).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\e2e-desktop-smoke.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\e2e-desktop-smoke.ps1 -Configuration Release -SkipVideo
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Exe,
    [string]$FfmpegDir,
    [string]$OutDir,
    [switch]$SkipVideo,
    [switch]$RefreshMedia
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Run this script in Windows PowerShell 5.1; UIAutomation client assemblies are unavailable under PowerShell 7.'
}

$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $Exe)    { $Exe    = Join-Path $projectRoot "bin\$Configuration\net8.0-windows10.0.19041.0\HYPNIX.exe" }
if (-not $OutDir) { $OutDir = Join-Path $projectRoot 'artifacts\e2e-desktop' }
if (-not (Test-Path -LiteralPath $Exe)) { throw "HYPNIX.exe not found at '$Exe'. Build it first (dotnet build -c $Configuration)." }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- resolve ffmpeg -------------------------------------------------------------------------
function Resolve-FfmpegDir {
    param([string]$Hint)
    if ($Hint -and (Test-Path -LiteralPath (Join-Path $Hint 'ffmpeg.exe'))) { return (Resolve-Path $Hint).Path }
    $cmd = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($cmd) { return (Split-Path $cmd.Source -Parent) }
    $winget = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    if (Test-Path -LiteralPath $winget) {
        $hit = Get-ChildItem $winget -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) { return (Split-Path $hit.FullName -Parent) }
    }
    return $null
}
$FfmpegDir = Resolve-FfmpegDir -Hint $FfmpegDir
if (-not $FfmpegDir) { throw 'ffmpeg not found. Install it (winget install Gyan.FFmpeg) or pass -FfmpegDir.' }
$ffmpeg = Join-Path $FfmpegDir 'ffmpeg.exe'
Write-Host "ffmpeg: $ffmpeg"

# --- test media (generated once, reused unless -RefreshMedia) --------------------------------
$video = Join-Path $OutDir 'testclip.mp4'
$wav   = Join-Path $OutDir 'pink.wav'
if ($RefreshMedia -or -not (Test-Path $video)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc=size=640x360:rate=30:duration=6' `
              -f lavfi -i 'sine=frequency=220:duration=6' -shortest -pix_fmt yuv420p $video
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed to generate test video.' }
}
if ($RefreshMedia -or -not (Test-Path $wav)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'anoisesrc=d=8:c=pink:a=0.7' -ac 2 -ar 48000 -sample_fmt s16 $wav
    if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed to generate pink-noise wav.' }
}

# --- UI Automation plumbing -----------------------------------------------------------------
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
$Auto = [Windows.Automation.AutomationElement]
$TS   = [Windows.Automation.TreeScope]
$settings = Join-Path $env:LOCALAPPDATA 'HYPNIX\settings.json'
$backup   = "$settings.e2ebak"
$shell    = New-Object -ComObject Shell.Application
$script:player = $null
$proc = $null
$failures = New-Object System.Collections.Generic.List[string]

function Find-ByAutoId($root, $id) { $root.FindFirst($TS::Descendants, (New-Object Windows.Automation.PropertyCondition($Auto::AutomationIdProperty, $id))) }
function Invoke-El($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Get-GalleryItems($root) {
    $g = Find-ByAutoId $root 'WallpaperGallery'
    if (-not $g) { throw 'WallpaperGallery not found.' }
    $g.FindAll($TS::Descendants, (New-Object Windows.Automation.PropertyCondition($Auto::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)))
}
function Select-Item($root, $idx) {
    (Get-GalleryItems $root).Item($idx).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Capture($name) {
    $shell.MinimizeAll(); Start-Sleep -Milliseconds 1100          # deterministic desktop reveal
    $vs  = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($vs.Location, [System.Drawing.Point]::Empty, $vs.Size)
    $path = Join-Path $OutDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    $shell.UndoMinimizeALL(); Start-Sleep -Milliseconds 700
    Write-Host "  captured -> $name"
}
function Audio-On  { $script:player = New-Object System.Media.SoundPlayer $wav; $script:player.PlayLooping() }
function Audio-Off { if ($script:player) { $script:player.Stop(); $script:player.Dispose(); $script:player = $null } }

try {
    # Ensure a settings file exists (first run creates one), then back it up.
    if (-not (Test-Path $settings)) {
        $seed = Start-Process -FilePath $Exe -PassThru; Start-Sleep -Seconds 6
        Stop-Process -Id $seed.Id -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
    }
    if (-not (Test-Path $settings)) { throw "settings.json was not created at '$settings'." }
    Copy-Item $settings $backup -Force

    # Inject a deterministic test config: never auto-pause + a video card pointing at our clip.
    $cfg = Get-Content $settings -Raw | ConvertFrom-Json
    $cfg.AppPauseMode = 0
    $cfg.SelectedWallpaperId = 'built-in-ambient'
    $cfg | Add-Member -NotePropertyName DisplayWallpapers -NotePropertyValue @{} -Force
    $cfg.MediaToolsDirectory = $FfmpegDir
    $cfg.Videos = @([pscustomobject]@{ Id = 'video:e2e'; Path = $video; Title = 'E2E Test Clip' })
    ($cfg | ConvertTo-Json -Depth 8) | Set-Content $settings -Encoding UTF8
    Write-Host "settings injected (AppPauseMode=Never, +video card); backup at $backup"

    # Launch and attach automation.
    $proc = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 6
    $cond = New-Object Windows.Automation.PropertyCondition($Auto::ProcessIdProperty, $proc.Id)
    $root = $null
    for ($i = 0; $i -lt 20 -and -not $root; $i++) { $root = $Auto::RootElement.FindFirst($TS::Children, $cond); if (-not $root) { Start-Sleep -Milliseconds 300 } }
    if (-not $root) { throw 'HYPNIX window not found via UI Automation.' }
    $count = (Get-GalleryItems $root).Count
    Write-Host "window ready; gallery items = $count"

    Capture '00-baseline.png'

    Select-Item $root 0
    Invoke-El (Find-ByAutoId $root 'StartButton'); Write-Host 'clicked Start'
    Start-Sleep -Seconds 3
    Capture '01-ambient.png'

    Select-Item $root 1; Invoke-El (Find-ByAutoId $root 'StartButton'); Start-Sleep -Seconds 2; Audio-On; Start-Sleep -Seconds 3
    Capture '02-visualizer-audio.png'; Audio-Off

    Select-Item $root 2; Invoke-El (Find-ByAutoId $root 'StartButton'); Start-Sleep -Seconds 2; Audio-On; Start-Sleep -Seconds 3
    Capture '03-aethelis-audio.png'; Audio-Off

    Select-Item $root 3; Invoke-El (Find-ByAutoId $root 'StartButton'); Start-Sleep -Seconds 2; Audio-On; Start-Sleep -Seconds 3
    Capture '04-fireburst-audio.png'; Audio-Off

    if (-not $SkipVideo -and $count -ge 6) {
        Select-Item $root ($count - 1); Invoke-El (Find-ByAutoId $root 'StartButton'); Start-Sleep -Seconds 5
        Capture '05-video.png'
    }

    Invoke-El (Find-ByAutoId $root 'StopButton'); Write-Host 'clicked Stop'
    Start-Sleep -Seconds 2
    Capture '06-stopped.png'
}
catch { $failures.Add($_.Exception.Message) }
finally {
    Audio-Off
    if ($proc) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1
    if (Test-Path $backup) { Copy-Item $backup $settings -Force; Remove-Item $backup -Force; Write-Host 'settings restored from backup' }
    $left = [bool](Get-Process -Name HYPNIX -ErrorAction SilentlyContinue)
    if ($left) { $failures.Add('HYPNIX process still running after cleanup.') }
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host "E2E-DESKTOP: PASS  (captures in $OutDir)" -ForegroundColor Green
    exit 0
} else {
    Write-Host 'E2E-DESKTOP: FAIL' -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
