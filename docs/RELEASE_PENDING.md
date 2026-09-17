# Release pending items

Tracks what is still required before a public release, per channel. The installer, in-app
auto-update and the Microsoft Store MSIX package are implemented and validated locally (see
[install and updates](INSTALL_AND_UPDATES.md)); the items below need decisions, accounts, secrets or
an elevated/PS7 machine that are outside this repository.

## Both channels

- [ ] **Code signing certificate** (SmartScreen). Builds are unsigned, so Windows warns on first
  run/install. Acquire a code-signing certificate and wire it in:
  - Velopack: `vpk pack --signTemplate "signtool sign ... {{file}}"` (or `--azureTrustedSignFile`).
  - MSIX: the Store signs its own package; for sideload use `-SelfSign` (test only).
- [ ] **Bump `<Version>`** in `AnimatedWallPaper.csproj` for every release (SemVer). It flows to the
  Velopack package version and the MSIX `x.y.z.0` version.

## Website installer (Velopack)

- [ ] **Set `UpdateFeedUrl`** in `Services/UpdateService.cs` to the real public release host. It is
  currently the placeholder `https://updates.example.com/hypnix/win`, so the auto-update check is a
  safe no-op until set.
- [ ] **Provision the release host** (static site / CDN / Amazon S3 / Azure Blob) that serves
  `releases.win.json` and the `.nupkg` files.
- [ ] **`RELEASE_FEED_URL` repo variable** — set it so the Release workflow can pull the live release
  and build deltas in CI. Without it, CI produces full packages only.
- [ ] **Publish the first release** — run `scripts/package-release.ps1 -Version 1.0.0` (or the Release
  workflow) and copy `artifacts/releases/*` to the host. Verify a real installed build updates from
  the host, not only from a local `HYPNIX_UPDATE_FEED`.

## Microsoft Store (MSIX)

- [ ] **Partner Center account** and reserve the app name.
- [ ] **Replace the `Identity`** (Name/Publisher) in `packaging/msix/AppxManifest.xml` with the
  identity assigned in Partner Center (Product → Product identity). Do **not** self-sign for
  submission — Partner Center signs.
- [ ] **Additional logo scales** — only base sizes are generated (44/150/50/310). Add scaled variants
  (scale-125/150/200/400 and target-size icons) for a polished Store listing.
- [ ] **Windows App Certification Kit** — run `appcert.exe` (elevated) before submitting to catch
  Store certification issues early.
- [ ] **`runFullTrust` justification** — the wallpaper hosting (`SetParent` to Progman/WorkerW) needs
  the full-trust capability, which is reviewed at certification. Prepare the rationale (wallpaper apps
  are allowed, but review is expected).
- [ ] **Final sideload validation** — `makeappx pack` and `signtool sign` were verified locally;
  `Add-AppxPackage` needs an elevated shell to trust the self-signed cert. Run once on an admin
  machine: `Import-Certificate ... Cert:\LocalMachine\TrustedPeople` then `Add-AppxPackage`.

## Tooling / environment

- [ ] **PowerShell 7** — `scripts/package-release.ps1` and `scripts/package-msix.ps1` require PS7
  (repo convention). Build machines and the GitHub `windows-2022` runner have it; validate on a PS7
  host if your local machine only has Windows PowerShell 5.1.

## Other known follow-ups (not release blockers)

- Effekseer fire effects (Fire Burst, Flamethrower Ring V2) still opt out of the palette,
  size/position and background controls; extending them means re-authoring the effect. See
  [UI architecture](UI_ARCHITECTURE.md).
- Physical multi-monitor/DPI matrix, high-contrast and screen-reader passes remain release-time
  validation. See [testing guide](TESTING_AND_REGRESSION_GUIDE.md) and
  [visual settings plan](VISUAL_SETTINGS_DESIGN_PLAN.md).
