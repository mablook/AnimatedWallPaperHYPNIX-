param([switch]$Validate)
$ErrorActionPreference = 'Stop'
$oceanRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$oceanOutput = Join-Path $oceanRoot ('artifacts/ocean-preview/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$oceanBin = Join-Path $oceanOutput 'bin'
Push-Location $oceanRoot
try {
    dotnet build Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj -c Release "-p:OutDir=$oceanBin/" -p:UseSharedCompilation=false -p:OceanPreview=true
    if ($LASTEXITCODE -ne 0) { throw 'Ocean preview build failed.' }
    $oceanRunner = Join-Path $oceanBin 'Hypnix.NativeSmoke.dll'
    if ($Validate) {
        dotnet $oceanRunner $oceanOutput --ocean-only
        if ($LASTEXITCODE -ne 0) { throw 'Ocean native validation failed.' }
        dotnet $oceanRunner (Join-Path $oceanOutput 'volumes') --ocean-volumes
        if ($LASTEXITCODE -ne 0) { throw 'Ocean volumetric validation failed.' }
        dotnet $oceanRunner (Join-Path $oceanOutput 'reflection') --ocean-reflection
        if ($LASTEXITCODE -ne 0) { throw 'Ocean reflection tracking validation failed.' }
        dotnet $oceanRunner (Join-Path $oceanOutput 'cloud-motion') --ocean-cloud-motion
        if ($LASTEXITCODE -ne 0) { throw 'Ocean cloud motion validation failed.' }
    }
    # A WinExe preview has no console to hide. It is an explicitly interactive window.
    Start-Process -FilePath (Join-Path $oceanBin 'Hypnix.NativeSmoke.exe') -ArgumentList @(('"{0}"' -f $oceanOutput), '--show-ocean') -WorkingDirectory $oceanRoot -WindowStyle Normal
    Write-Output "Ocean preview opened. Captures: $oceanOutput"
} finally { Pop-Location }
