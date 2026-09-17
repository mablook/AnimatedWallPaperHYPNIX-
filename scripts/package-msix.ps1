#requires -Version 7.0
# Builds an MSIX package for the Microsoft Store / sideload channel. This is SEPARATE from the
# Velopack website installer (scripts/package-release.ps1). The app binary is identical; the Velopack
# auto-updater self-disables inside MSIX (UpdateManager.IsInstalled is false), because the Store
# manages updates.
#
#   ./scripts/package-msix.ps1 -Version 1.2.3            # unsigned .msix (Store signs on submission)
#   ./scripts/package-msix.ps1 -Version 1.2.3 -SelfSign  # + self-signed for local sideload testing
#
# Needs the Windows 10/11 SDK (makeappx.exe, signtool.exe). For the Store, replace the Identity in
# packaging/msix/AppxManifest.xml with the one assigned in Partner Center; do not self-sign for
# submission (Partner Center signs).
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$SelfSign,
    [string]$CertSubject = 'CN=Marcelo Bossle',
    [string]$OutputDir = 'artifacts/msix'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    # MSIX versions are strictly 4-part numeric with a zero revision; no SemVer pre-release suffix.
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be x.y.z (no pre-release for MSIX): $Version" }
    $msixVersion = "$Version.0"

    $sdkBin = Get-ChildItem 'C:/Program Files (x86)/Windows Kits/10/bin' -Directory |
        Where-Object { $_.Name -match '^10\.' } | Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
    if (-not $sdkBin) { throw 'makeappx.exe not found; install the Windows 10/11 SDK.' }
    $makeappx = Join-Path $sdkBin 'makeappx.exe'
    $signtool = Join-Path $sdkBin 'signtool.exe'

    # Self-contained publish into a clean layout (MSIX ships the runtime; full-trust keeps real paths).
    $staging = [IO.Path]::GetFullPath((Join-Path $OutputDir 'staging'), $projectRoot)
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    dotnet publish AnimatedWallPaper.csproj -c Release -p:PublishProfile=win-x64-self-contained `
        "-p:PublishDir=$staging/" "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    # Lay out the manifest (with the version substituted) and the Store visual assets.
    (Get-Content -Raw packaging/msix/AppxManifest.xml).Replace('{VERSION}', $msixVersion) |
        Set-Content (Join-Path $staging 'AppxManifest.xml') -Encoding utf8
    # Logos live in StoreAssets to avoid colliding with the app's own published Assets folder.
    Copy-Item packaging/msix/Assets (Join-Path $staging 'StoreAssets') -Recurse -Force

    $output = [IO.Path]::GetFullPath($OutputDir, $projectRoot)
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $msix = Join-Path $output "HYPNIX-$Version-x64.msix"
    & $makeappx pack /o /d $staging /p $msix
    if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
    Write-Output "MSIX created: $msix"

    if ($SelfSign) {
        # Local sideload only. The cert subject must match the manifest Publisher.
        $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey } | Select-Object -First 1
        if (-not $cert) {
            $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject `
                -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        }
        & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix
        if ($LASTEXITCODE -ne 0) { throw 'signtool sign failed.' }
        $cer = Join-Path $output 'HYPNIX-selfsign.cer'
        Export-Certificate -Cert $cert -FilePath $cer | Out-Null
        Write-Output "Signed for sideload. Trust once (admin): Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
        Write-Output "Then install: Add-AppxPackage '$msix'"
    } else {
        Write-Output 'Unsigned package (Partner Center signs on Store submission). Use -SelfSign to sideload-test.'
    }
} finally { Pop-Location }
