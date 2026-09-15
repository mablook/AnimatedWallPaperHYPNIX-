#requires -Version 7.0
param(
    [string]$OutputRoot = 'artifacts/distribution',
    [string]$PublishedDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $revision = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source commit.' }
    $sourceDirty = [bool](git status --porcelain)
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $packageName = "HYPNIX-win-x64-$($revision.Substring(0, 7))-$stamp"
    $output = [IO.Path]::GetFullPath($OutputRoot, $projectRoot)
    $staging = Join-Path $output $packageName
    if (Test-Path -LiteralPath $staging) { throw "Output already exists: $staging" }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    if ($PublishedDirectory) {
        $published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
        Copy-Item -Path (Join-Path $published '*') -Destination $staging -Recurse
    } else {
        dotnet publish AnimatedWallPaper.csproj -c Release -p:PublishProfile=win-x64-self-contained `
            "-p:OutDir=$output/build-$stamp/" "-p:PublishDir=$staging/"
        if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    }

    foreach ($required in @('HYPNIX.exe', 'HYPNIX.dll', 'HYPNIX.deps.json', 'HYPNIX.runtimeconfig.json',
        'Hypnix.EffekseerBridge.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Missing runtime file: $required" }
    }
    $runtime = Get-Content -Raw (Join-Path $staging 'HYPNIX.runtimeconfig.json') | ConvertFrom-Json
    if (@($runtime.runtimeOptions.includedFrameworks).Count -ne 2) {
        throw 'The package must include the .NET and Windows Desktop runtimes.'
    }
    $deps = Get-Content -Raw (Join-Path $staging 'HYPNIX.deps.json') | ConvertFrom-Json
    if ($deps.runtimeTarget.name -notlike '*/win-x64') { throw 'Expected a Windows x64 package.' }

    # Compare every currently tracked renderer/asset copied by the publish profile.
    $assetPaths = git ls-files -- Assets Shaders NativeBin
    foreach ($relative in $assetPaths) {
        $destination = if ($relative -eq 'NativeBin/Hypnix.EffekseerBridge.dll') {
            Join-Path $staging 'Hypnix.EffekseerBridge.dll'
        } else { Join-Path $staging $relative }
        $requiredAsset = $relative.StartsWith('Shaders/') -or $relative.StartsWith('Assets/Effects/') -or
            $relative.StartsWith('NativeBin/') -or
            ($relative.StartsWith('Assets/Wallpapers/') -and $relative -match '/(wallpaper\.json|CREDITS\.txt|preview\.(png|jpg)|background\.png|[^/]+-state\.png)$')
        if (!$requiredAsset) { continue }
        if (!(Test-Path -LiteralPath $destination)) { throw "Missing packaged asset: $relative" }
        if ((Get-FileHash -LiteralPath $relative).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Packaged asset differs from source: $relative"
        }
    }

    $kinds = @('BuiltIn', 'VisualizerDemo', 'AethelisVisualizer', 'AethelisFlameBurst', 'FlamethrowerRingV2',
        'SpectralBloom', 'NeonRibbons', 'LiquidOrbs', 'EventHorizon')
    $catalog = @(Get-ChildItem (Join-Path $staging 'Assets/Wallpapers') -Filter wallpaper.json -Recurse |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json } |
        Where-Object { $_.kind -in $kinds -and !$_.hidden })
    foreach ($kind in $kinds) {
        if (@($catalog | Where-Object kind -eq $kind).Count -ne 1) { throw "Missing or duplicate built-in: $kind" }
    }

    Copy-Item -LiteralPath LICENSE -Destination (Join-Path $staging 'HYPNIX-LICENSE.txt')
    Copy-Item -LiteralPath docs/DISTRIBUTION.md -Destination (Join-Path $staging 'START-HERE.md')
    Copy-Item -LiteralPath docs/licenses -Destination (Join-Path $staging 'licenses') -Recurse

    # Preserve the license/notice files provided by the resolved NuGet runtime packs and libraries.
    $assets = Get-Content -Raw obj/project.assets.json | ConvertFrom-Json
    $packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
    $dependencies = foreach ($library in $deps.libraries.PSObject.Properties) {
        if ($library.Name.StartsWith('HYPNIX/')) { continue }
        $packageKey = $library.Name -replace '^runtimepack\.', ''
        $packageFolder = $null
        foreach ($root in $packageRoots) {
            $candidate = Join-Path $root $packageKey.ToLowerInvariant()
            if (Test-Path -LiteralPath $candidate) { $packageFolder = $candidate; break }
        }
        if (!$packageFolder) { throw "Cannot locate dependency notices: $packageKey" }
        $noticeFolder = Join-Path $staging "licenses/packages/$($packageKey.Replace('/', '-'))"
        New-Item -ItemType Directory -Path $noticeFolder -Force | Out-Null
        $nuspec = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' | Select-Object -First 1
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName
        Copy-Item -LiteralPath $nuspec.FullName -Destination $noticeFolder
        Get-ChildItem -LiteralPath $packageFolder -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\..+)?$' |
            Copy-Item -Destination $noticeFolder
        [ordered]@{ package = $packageKey; license = $metadata.package.metadata.license.InnerText;
            copyright = $metadata.package.metadata.copyright; repository = $metadata.package.metadata.repository.url }
    }
    $manifest = [ordered]@{
        formatVersion = 1; package = $packageName; sourceCommit = $revision; sourceWorkingTreeDirty = $sourceDirty
        createdUtc = [DateTime]::UtcNow.ToString('o'); runtime = 'win-x64'; selfContained = $true
        applicationVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $staging 'HYPNIX.dll')).ProductVersion
        frameworks = $runtime.runtimeOptions.includedFrameworks; wallpapers = @($catalog.id); dependencies = @($dependencies)
    }
    $manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $staging 'build-info.json') -Encoding utf8
    $inventory = @(Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\', '/');
            bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $inventory | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $staging 'file-manifest.json') -Encoding utf8
    $zip = Join-Path $output "$packageName.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    "$hash  $packageName.zip" | Set-Content "$zip.sha256" -Encoding ascii
    Write-Output "Package: $zip"
    Write-Output "SHA256: $hash"
    Write-Output "Validated $($catalog.Count) built-in wallpapers and $($inventory.Count) packaged files."
} finally { Pop-Location }
