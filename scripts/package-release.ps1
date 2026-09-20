#requires -Version 7.0
# Builds a Velopack release (installer + update feed) for HYPNIX.
#
#   ./scripts/package-release.ps1 -Version 1.2.3
#
# Output (default artifacts/releases): HypnixWallpaper-win-Setup.exe and .msi (per-user installers), the full and delta
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
    if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "EXE/MSI releases require a stable x.y.z version: $Version"
    }
    $parts = $Version.Split('.') | ForEach-Object { [long]$_ }
    if ($parts[0] -gt 255 -or $parts[1] -gt 255 -or $parts[2] -gt 65535) { throw 'Version exceeds Windows Installer limits (255.255.65535).' }
    if ($Channel -ne 'win') { throw 'HYPNIX currently reads the win update channel; other channels are not supported.' }
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "The 'vpk' tool is not installed. Run: dotnet tool install -g vpk --version 1.2.0"
    }

    # Self-contained publish into a clean folder (Velopack needs individual files, not single-file).
    $work = Join-Path $projectRoot ('artifacts/package-work/' + [Guid]::NewGuid().ToString('N'))
    $publish = Join-Path $work 'publish'
    dotnet publish AnimatedWallPaper.csproj -c Release -p:PublishProfile=win-x64-self-contained `
        "-p:PublishDir=$publish/" "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'HYPNIX.exe'))) { throw 'Published HYPNIX.exe not found.' }
    & (Join-Path $PSScriptRoot 'copy-distribution-notices.ps1') -PublishedDirectory $publish | Out-Null

    $releases = [IO.Path]::GetFullPath($OutputDir, $projectRoot)
    New-Item -ItemType Directory -Path $releases -Force | Out-Null
    # Never expose the raw MSI in the deliverable folder: validation must succeed first.
    $packed = Join-Path $work 'packed'
    New-Item -ItemType Directory -Path $packed -Force | Out-Null
    Get-ChildItem -LiteralPath $releases -File | Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -in @('releases.win.json', 'RELEASES') } |
        Copy-Item -Destination $packed

    # packId is intentionally distinct from the "HYPNIX" user-data folder: Velopack installs to
    # %LocalAppData%\HypnixWallpaper, so it never touches settings/presets/backgrounds in
    # %LocalAppData%\HYPNIX (install/update/uninstall leave user data intact; the friendly name and
    # shortcuts stay "HYPNIX" via --packTitle).
    vpk pack -u HypnixWallpaper -v $Version -p $publish -e HYPNIX.exe -o $packed -c $Channel --msi --instLocation PerUser `
        --packTitle HYPNIX --packAuthors 'Marcelo Bossle' -i (Join-Path $projectRoot 'Assets/Brand/hypnix-v2.ico')
    if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }
    & (Join-Path $PSScriptRoot 'fix-msi-package.ps1') -Path (Join-Path $packed "HypnixWallpaper-$Channel.msi")
    $runtime = Get-Content (Join-Path $publish 'HYPNIX.runtimeconfig.json') -Raw | ConvertFrom-Json
    [ordered]@{ version = $Version; sourceCommit = (git rev-parse HEAD).Trim(); sourceWorkingTreeDirty = [bool](git status --porcelain);
        createdUtc = [DateTime]::UtcNow.ToString('o'); runtime = 'win-x64'; frameworks = $runtime.runtimeOptions.includedFrameworks;
        publishedDirectory = $publish; signing = 'unsigned'; publicReleaseReady = $false } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $packed 'build-info.json')
    Get-ChildItem -LiteralPath $packed -File | Copy-Item -Destination $releases -Force
    Get-ChildItem -LiteralPath $releases -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name |
        ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name } |
        Set-Content (Join-Path $releases 'SHA256SUMS.txt') -Encoding ascii

    Write-Output "Release $Version packed to $releases"
    Get-ChildItem -LiteralPath $releases | Sort-Object Name | Select-Object Name, @{N='KB';E={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize
} finally { Pop-Location }
