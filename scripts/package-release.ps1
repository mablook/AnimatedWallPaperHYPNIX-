#requires -Version 7.0
# Builds a Velopack release (installer + update feed) for HYPNIX.
#
#   ./scripts/package-release.ps1 -Version 1.2.3
#
# Output (default artifacts/releases): HYPNIX-win-Setup.exe (per-user installer), the full and delta
# .nupkg packages, and releases.win.json (the update feed UpdateManager reads). Pack a newer version
# into the same folder to produce a delta and let an installed build update to it.
#
# Requires the 'vpk' dotnet tool matching the Velopack package version:
#   dotnet tool install -g vpk --version 1.2.0
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir = 'artifacts/releases',
    [string]$Channel = 'win'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Version must be SemVer (e.g. 1.2.3 or 1.2.3-beta.1): $Version"
    }
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "The 'vpk' tool is not installed. Run: dotnet tool install -g vpk --version 1.2.0"
    }

    # Self-contained publish into a clean folder (Velopack needs individual files, not single-file).
    $publish = Join-Path $projectRoot 'artifacts/release-publish'
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    dotnet publish AnimatedWallPaper.csproj -c Release -p:PublishProfile=win-x64-self-contained `
        "-p:PublishDir=$publish/" "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'HYPNIX.exe'))) { throw 'Published HYPNIX.exe not found.' }

    $releases = [IO.Path]::GetFullPath($OutputDir, $projectRoot)
    New-Item -ItemType Directory -Path $releases -Force | Out-Null

    # packId is intentionally distinct from the "HYPNIX" user-data folder: Velopack installs to
    # %LocalAppData%\HypnixWallpaper, so it never touches settings/presets/backgrounds in
    # %LocalAppData%\HYPNIX (install/update/uninstall leave user data intact; the friendly name and
    # shortcuts stay "HYPNIX" via --packTitle).
    vpk pack -u HypnixWallpaper -v $Version -p $publish -e HYPNIX.exe -o $releases -c $Channel `
        --packTitle HYPNIX --packAuthors 'Marcelo Bossle' -i (Join-Path $projectRoot 'Assets/Brand/hypnix-v2.ico')
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

    Write-Output "Release $Version packed to $releases"
    Get-ChildItem -LiteralPath $releases | Sort-Object Name | Select-Object Name, @{N='KB';E={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize
} finally { Pop-Location }
