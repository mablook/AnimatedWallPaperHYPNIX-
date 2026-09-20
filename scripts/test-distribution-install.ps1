#requires -Version 7.0
# Real per-user lifecycle tests. Refuses existing installs/running apps; backs up ALL user data.
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$UpgradeDirectory
)
$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory, (Get-Location).Path)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory for each installation test.' }
$msi = Join-Path $release 'HypnixWallpaper-win.msi'
$setup = Join-Path $release 'HypnixWallpaper-win-Setup.exe'
& (Join-Path $PSScriptRoot 'test-msi-package.ps1') -Path $msi
$upgrade = if ($UpgradeDirectory) { (Resolve-Path -LiteralPath $UpgradeDirectory).Path } else { $null }
if ($upgrade) { & (Join-Path $PSScriptRoot 'test-msi-package.ps1') -Path (Join-Path $upgrade 'HypnixWallpaper-win.msi') }
$installRoot = Join-Path $env:LOCALAPPDATA 'HypnixWallpaper'
$dataRoot = Join-Path $env:LOCALAPPDATA 'HYPNIX'
if (Get-Process HYPNIX -ErrorAction SilentlyContinue) { throw 'Quit HYPNIX before testing installers.' }
if (Test-Path -LiteralPath $installRoot) { throw "An installation directory already exists: $installRoot" }
foreach ($registry in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
    if (Get-ItemProperty "$registry\*" -ErrorAction SilentlyContinue | Where-Object DisplayName -eq 'HYPNIX') {
        throw 'An existing HYPNIX uninstall registration was found; refusing to replace it.'
    }
}
New-Item -ItemType Directory -Path $output | Out-Null
if (Test-Path -LiteralPath $dataRoot) { Copy-Item -LiteralPath $dataRoot -Destination (Join-Path $output 'user-data-backup') -Recurse }
$before = @(Get-ChildItem -LiteralPath $dataRoot -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($dataRoot, $_.FullName); hash = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
foreach ($file in $before) {
    if ((Get-FileHash -LiteralPath (Join-Path $output ('user-data-backup/' + $file.path))).Hash -ne $file.hash) { throw 'Backup verification failed.' }
}
$before | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $output 'user-data-before.json')
# A canary catches deletion even on an empty CI profile. Keep it until every boundary is checked.
New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
$canary = Join-Path $dataRoot ('installer-test-' + [Guid]::NewGuid().ToString('N') + '.txt')
[Guid]::NewGuid().ToString() | Set-Content -LiteralPath $canary
$canaryHash = (Get-FileHash -LiteralPath $canary).Hash
$results = [Collections.Generic.List[object]]::new()
$activeKind = $null
$activeMsi = $msi
function Assert-Data {
    foreach ($file in $before) {
        $path = Join-Path $dataRoot $file.path
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.hash) { throw "User data changed: $($file.path). Backup retained at $output" }
    }
    if (!(Test-Path -LiteralPath $canary) -or (Get-FileHash -LiteralPath $canary).Hash -ne $canaryHash) { throw 'Installer deleted or changed the user-data canary.' }
}
function Run-Checked([string]$Name, [string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw "$Name exceeded 60 seconds; inspect the process before another run." }
    if ($process.ExitCode -ne 0) { throw "$Name failed: exit $($process.ExitCode). Inspect logs at $output" }
    Assert-Data
    $results.Add([ordered]@{ check = $Name; exitCode = $process.ExitCode; userDataUnchanged = $true })
    Write-Output "PASS: $Name; all user data preserved."
}
function Invoke-Msi([string]$Name, [string]$Mode, [string]$Package) {
    Run-Checked $Name 'msiexec.exe' @($Mode, ('"' + $Package + '"'), '/qn', '/norestart', '/L*v', ('"' + (Join-Path $output "$Name.log") + '"'))
}
function Assert-Installed([string]$Version) {
    foreach ($file in @('current/HYPNIX.exe','current/HYPNIX.dll','current/coreclr.dll','current/PresentationFramework.dll','current/Hypnix.EffekseerBridge.dll','current/licenses/Effekseer.txt','Update.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $installRoot $file))) { throw "Missing installed file: $file" }
    }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $installRoot 'current/HYPNIX.dll')).ProductVersion.Split('+')[0]
    if ($actual -ne $Version) { throw "Expected installed version $Version, got $actual." }
}
function Assert-Removed {
    if (Test-Path -LiteralPath (Join-Path $installRoot 'current/HYPNIX.exe')) { throw 'Uninstall left the application executable.' }
    Assert-Data
}
$version = (Get-Content (Join-Path $release 'releases.win.json') -Raw | ConvertFrom-Json).Assets |
    Where-Object Type -eq 'Full' | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1 -ExpandProperty Version
$next = if ($upgrade) { (Get-Content (Join-Path $upgrade 'releases.win.json') -Raw | ConvertFrom-Json).Assets | Where-Object Type -eq 'Full' | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1 } else { $null }
try {
    $activeKind = 'exe'
    Run-Checked 'exe-install' $setup @('--silent')
    Assert-Installed $version
    if ($next) {
        Run-Checked 'exe-update' (Join-Path $installRoot 'Update.exe') @('--silent', 'apply', '--norestart', '--package', ('"' + (Join-Path $upgrade $next.FileName) + '"'))
        Assert-Installed $next.Version
    }
    Run-Checked 'exe-uninstall' (Join-Path $installRoot 'Update.exe') @('--silent', 'uninstall')
    Assert-Removed
    $activeKind = $null

    $activeKind = 'msi'
    Invoke-Msi 'msi-install' '/i' $msi
    Assert-Installed $version
    # Prove repair restores a missing installed application DLL, not just a zero exit code.
    $dll = Join-Path $installRoot 'current/HYPNIX.dll'
    $dllHash = (Get-FileHash -LiteralPath $dll).Hash
    Remove-Item -LiteralPath $dll
    Invoke-Msi 'msi-repair' '/fa' $msi
    if ((Get-FileHash -LiteralPath $dll).Hash -ne $dllHash) { throw 'Repair did not restore the original application DLL.' }
    if ($next) {
        $activeMsi = Join-Path $upgrade 'HypnixWallpaper-win.msi'
        Invoke-Msi 'msi-upgrade' '/i' $activeMsi
        Assert-Installed $next.Version
    }
    Invoke-Msi 'msi-uninstall' '/x' $activeMsi
    Assert-Removed
    $activeKind = $null
    $results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'results.json')
} finally {
    # Only remove the install made by this run; pre-existing installations were rejected above.
    if ($activeKind -eq 'msi') { Invoke-Msi 'cleanup-msi' '/x' $activeMsi }
    elseif ($activeKind -eq 'exe' -and (Test-Path -LiteralPath (Join-Path $installRoot 'Update.exe'))) {
        Run-Checked 'cleanup-exe' (Join-Path $installRoot 'Update.exe') @('--silent','uninstall')
    }
    Assert-Data
    Remove-Item -LiteralPath $canary
}
