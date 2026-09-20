<#
.SYNOPSIS
    Tests independent wallpapers on at least two real connected displays.
.DESCRIPTION
    Builds the native test harness and drives its own MainWindow through Apply, Apply all,
    Stop display, Stop all and startup restoration. Verifies actual Explorer-hosted HWNDs,
    monitor geometry, native pause and cleanup. Uses isolated settings; existing HYPNIX
    processes and user preferences are not changed. Temporary wallpapers are removed on exit.
    No screenshots of the user's desktop are captured.
    Pass MediaToolsDirectory and VideoPath to include a real mixed video/shader test and
    FFmpeg process cleanup. If omitted, that optional scenario is reported as skipped.
#>
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$OutDir,
    [string]$MediaToolsDirectory,
    [string]$VideoPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'Tests\Hypnix.NativeSmoke\Hypnix.NativeSmoke.csproj'
if (-not $OutDir) { $OutDir = Join-Path $projectRoot 'artifacts\e2e-display-wallpapers' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if ([bool]$MediaToolsDirectory -xor [bool]$VideoPath) {
    throw 'Provide both -MediaToolsDirectory and -VideoPath to test a video wallpaper.'
}

Push-Location $projectRoot
try {
    & dotnet build $project -c $Configuration --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Desktop E2E build failed with exit code $LASTEXITCODE." }
    $runArguments = @('run', '--project', $project, '-c', $Configuration, '--no-build', '--', $OutDir, '--displays-desktop')
    if ($MediaToolsDirectory -and $VideoPath) {
        $runArguments += @([System.IO.Path]::GetFullPath($MediaToolsDirectory), [System.IO.Path]::GetFullPath($VideoPath))
    }
    & dotnet @runArguments
    if ($LASTEXITCODE -ne 0) { throw "Display desktop E2E failed with exit code $LASTEXITCODE. See $OutDir." }
    Write-Host "Display desktop E2E passed. Report: $(Join-Path $OutDir 'display-desktop-report.json')"
}
finally { Pop-Location }
