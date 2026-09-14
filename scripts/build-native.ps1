param(
    [string]$EffekseerSource,
    [switch]$FetchEffekseer,
    [switch]$UpdateRuntime,
    [string]$CMake = 'cmake'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$pinnedCommit = '11e7a852692332f4fe187cdb90af6afa12318c69'
$buildRoot = Join-Path $projectRoot 'artifacts/native'
function Invoke-BuildTool([string[]]$Arguments) {
    # Some Windows hosts inherit both Path and PATH; MSBuild rejects that environment.
    $info = [System.Diagnostics.ProcessStartInfo]::new($CMake)
    $info.UseShellExecute = $false
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $variables = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        $key = $entry.Key.ToUpperInvariant()
        if ($key -eq 'PATH' -and $variables.ContainsKey($key)) { $variables[$key] += ';' + $entry.Value }
        else { $variables[$key] = $entry.Value }
    }
    $info.Environment.Clear()
    foreach ($entry in $variables.GetEnumerator()) { $info.Environment[$entry.Key] = $entry.Value }
    $process = [System.Diagnostics.Process]::Start($info)
    $process.WaitForExit()
    $result = $process.ExitCode
    $process.Dispose()
    if ($result -ne 0) { throw "CMake failed with exit code $result." }
}
if (!$EffekseerSource) {
    $EffekseerSource = Join-Path $projectRoot 'artifacts/dependencies/Effekseer'
    if (!(Test-Path -LiteralPath (Join-Path $EffekseerSource 'CMakeLists.txt'))) {
        if (!$FetchEffekseer) { throw 'Supply -EffekseerSource or explicitly use -FetchEffekseer to download the pinned source.' }
        git clone --no-checkout https://github.com/effekseer/Effekseer.git $EffekseerSource
        if ($LASTEXITCODE -ne 0) { throw 'Effekseer clone failed.' }
        git -C $EffekseerSource checkout --detach $pinnedCommit
        if ($LASTEXITCODE -ne 0) { throw 'Effekseer checkout failed.' }
    }
}
$EffekseerSource = (Resolve-Path -LiteralPath $EffekseerSource).Path
$actualCommit = git -C $EffekseerSource rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $pinnedCommit) { throw "Effekseer must be at commit $pinnedCommit." }
$sourceChanges = git -C $EffekseerSource status --porcelain --untracked-files=no
if ($sourceChanges) { throw 'Effekseer tracked source must be clean for a reproducible build.' }
Invoke-BuildTool @('-S', (Join-Path $projectRoot 'Native/EffekseerBridge'), '-B', $buildRoot, '-A', 'x64', "-DEFFEKSEER_SOURCE_DIR=$EffekseerSource")
Invoke-BuildTool @('--build', $buildRoot, '--config', 'Release', '--target', 'Hypnix.EffekseerBridge', '--parallel', '4')
$runtime = Join-Path $buildRoot 'bin/Hypnix.EffekseerBridge.dll'
if ($UpdateRuntime) { Copy-Item -LiteralPath $runtime -Destination (Join-Path $projectRoot 'NativeBin/Hypnix.EffekseerBridge.dll') -Force }
Get-FileHash -LiteralPath $runtime -Algorithm SHA256
