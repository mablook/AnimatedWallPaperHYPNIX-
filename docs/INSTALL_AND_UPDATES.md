# HYPNIX installation and updates

HYPNIX has two independent distribution channels from one codebase:

1. **Website installer** — a per-user [Velopack](https://velopack.io) installer with in-app
   auto-update, hosted on a public release website.
2. **Microsoft Store** — an MSIX package with Store-managed updates.

The GitHub repository is **private**, so it is not used as an end-user feed (a private repo's
Releases require authentication). Both channels ship the same binary; the Velopack auto-updater
self-disables inside the Store/MSIX build (`UpdateManager.IsInstalled` is false there), so a Store
build never tries to update itself.

---

## 1. Website installer (Velopack)

### What the user gets
- **`HypnixWallpaper-win-Setup.exe`** — per-user installer (no admin). Installs to
  `%LocalAppData%\HypnixWallpaper`, creates Desktop/Start-Menu shortcuts named **HYPNIX**, runs the app.
- **Automatic updates** — on launch an installed build checks the release website in the background,
  downloads deltas silently, shows a tray balloon and a **"Restart to update HYPNIX to <version>"**
  tray item, and applies the update on the user's restart. A failed/offline check is logged and
  ignored, so it never disrupts a running wallpaper.

### User data is separate from the install
Install lives in `%LocalAppData%\HypnixWallpaper`; settings, presets, imported backgrounds and logs
live in `%LocalAppData%\HYPNIX`. The Velopack package id is `HypnixWallpaper` specifically so
install/update/uninstall never touch user data. This was verified on Windows: a real silent install
left `settings.json` byte-identical, and the full **install 1.0.0 → feed 1.0.1 → download → ready**
cycle worked via a local feed.

### Point the app at your release host
The updater reads a **public web feed**, not GitHub. Set `UpdateFeedUrl` in
`Services/UpdateService.cs` to the folder URL where you host releases (it must serve
`releases.win.json` and the `.nupkg` files). Any static host works: a web server/CDN, Amazon S3 or
Azure Blob Storage.

### Build a release
Requires PowerShell 7 and the Velopack CLI matching the NuGet package:

```powershell
dotnet tool install -g vpk --version 1.2.0
./scripts/package-release.ps1 -Version 1.2.3
```

Output in `artifacts/releases`: `HypnixWallpaper-win-Setup.exe`, the full and delta `.nupkg`, a
portable ZIP, and `releases.win.json` (the feed). Copy those files to your release host to publish.
To build a delta, first place the current live `.nupkg` + feed in that folder (or `vpk download http
--url <host> -o artifacts/releases`), then pack the newer version into it.

The [Release workflow](../.github/workflows/release.yml) builds this on GitHub Actions and uploads
the `releases` folder as a **build artifact** (it does not publish to the private repo's Releases).
Download that artifact and copy it to your host. Set the `RELEASE_FEED_URL` repository variable to
enable delta builds in CI.

### Test the install → update cycle locally
The updater honors `HYPNIX_UPDATE_FEED` (a local folder) instead of the website:

1. `./scripts/package-release.ps1 -Version 1.0.0`, run `HypnixWallpaper-win-Setup.exe --silent`.
2. Confirm `%LocalAppData%\HypnixWallpaper\current\HYPNIX.exe` and that `%LocalAppData%\HYPNIX`
   (settings) is untouched.
3. `./scripts/package-release.ps1 -Version 1.0.1` into the same `artifacts/releases`.
4. Launch the installed app with `HYPNIX_UPDATE_FEED` set to that folder. The log records
   `Update downloaded and ready: 1.0.1`; the tray offers the restart.
5. Uninstall: `%LocalAppData%\HypnixWallpaper\current\Update.exe --uninstall --silent`. User data in
   `%LocalAppData%\HYPNIX` remains.

---

## 2. Microsoft Store (MSIX)

The Store requires MSIX; Velopack's format is not accepted there, and the Store manages updates (the
in-app updater stays off). Packaging is separate under `packaging/msix/` and `scripts/package-msix.ps1`.

```powershell
./scripts/package-msix.ps1 -Version 1.2.3            # unsigned .msix (Store signs on submission)
./scripts/package-msix.ps1 -Version 1.2.3 -SelfSign  # signed for local sideload testing
```

Needs the Windows 10/11 SDK (`makeappx.exe`, `signtool.exe`). Output: `artifacts/msix/HYPNIX-<ver>-x64.msix`.

### Manifest and full trust
`packaging/msix/AppxManifest.xml` declares `EntryPoint="Windows.FullTrustApplication"` and the
`runFullTrust` restricted capability, which the app needs to host the wallpaper on the shell
(`SetParent` to Progman/WorkerW). This capability is reviewed during Store certification (wallpaper
apps are allowed, but expect review). Logos live in `StoreAssets\` to avoid colliding with the app's
own `Assets\` folder.

The `Identity` (Name/Publisher) and the friendly name are placeholders. **For the Store, replace them
with the identity assigned in Partner Center** (Product → Product identity) and do not self-sign —
Partner Center signs. Bump `<Version>` in the csproj; the MSIX version is `x.y.z.0`.

### Validate locally (sideload)
`-SelfSign` signs with a self-signed certificate whose subject matches the manifest `Publisher`, and
exports `HYPNIX-selfsign.cer`. To install it (requires an elevated shell once):

```powershell
Import-Certificate -FilePath artifacts/msix/HYPNIX-selfsign.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage artifacts/msix/HYPNIX-<ver>-x64.msix
```

`makeappx pack` (manifest/schema/logo validation) and `signtool sign` were verified locally; the
final `Add-AppxPackage` needs administrator rights to trust the self-signed certificate. Run the
Windows App Certification Kit (`appcert.exe`, admin) before submitting to catch Store issues early.

### Submit
Upload the `.msix` (unsigned) to Partner Center. The Store validates, signs, and distributes it, and
handles updates for installed users.

---

## Code signing (both channels)

The Velopack installer/app are **not code-signed**, so Windows SmartScreen warns on first
run/install until a code-signing certificate is configured (`vpk pack --signTemplate ...`, or
`--azureTrustedSignFile` for Azure Trusted Signing). The Store signs its MSIX itself. Signing does
not change functionality, only first-run trust.

## Portable ZIP

The no-install, hash-verified portable build (`scripts/package-portable.ps1`, see
[DISTRIBUTION](DISTRIBUTION.md)) still exists for validation. All builds share the same
`%LocalAppData%\HYPNIX` user data under one Windows account, so quit one before running another.
