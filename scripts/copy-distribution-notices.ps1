#requires -Version 7.0
# Shared by every distribution channel; resolve notices from the actual published dependency map.
param(
    [Parameter(Mandatory)][string]$PublishedDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $published 'HYPNIX-LICENSE.txt')
$noticeRoot = Join-Path $published 'licenses'
New-Item -ItemType Directory -Path $noticeRoot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs/licenses') -File | Copy-Item -Destination $noticeRoot -Force
$assets = Get-Content -Raw (Join-Path $projectRoot 'obj/project.assets.json') | ConvertFrom-Json
$deps = Get-Content -Raw (Join-Path $published 'HYPNIX.deps.json') | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($library in $deps.libraries.PSObject.Properties) {
    if ($library.Name.StartsWith('HYPNIX/')) { continue }
    $packageKey = $library.Name -replace '^runtimepack\.', ''
    $packageFolder = $null
    foreach ($root in $packageRoots) {
        $candidate = Join-Path $root $packageKey.ToLowerInvariant()
        if (Test-Path -LiteralPath $candidate) { $packageFolder = $candidate; break }
    }
    if (!$packageFolder) { throw "Cannot locate dependency notices: $packageKey" }
    $noticeFolder = Join-Path $noticeRoot "packages/$($packageKey.Replace('/', '-'))"
    New-Item -ItemType Directory -Path $noticeFolder -Force | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $packageFolder -Filter '*.nuspec' | Select-Object -First 1
    if (!$nuspec) { throw "Cannot locate dependency metadata: $packageKey" }
    [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName
    Copy-Item -LiteralPath $nuspec.FullName -Destination $noticeFolder -Force
    Get-ChildItem -LiteralPath $packageFolder -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\..+)?$' |
        Copy-Item -Destination $noticeFolder -Force
    # Return the inventory for the portable package's build-info.json.
    [ordered]@{ package = $packageKey; license = $metadata.package.metadata.license.InnerText;
        copyright = $metadata.package.metadata.copyright; repository = $metadata.package.metadata.repository.url }
}
