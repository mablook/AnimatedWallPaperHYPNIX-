# Release pending items

Current commerce plan (2026-09-21): free Microsoft Store distribution, Lemon Squeezy
one-time licences, app-managed 15-day trial. Public identifiers and checkout URL
are configured for confirmed Test mode; publication is blocked. Live configuration
and live commerce tests remain pending.
Earlier Store-purchase requirements and package evidence below are
historical; see [the current checklist](LEMON_SQUEEZY.md).

Tracks what is still required before a public release, per channel. EXE and classic MSI lifecycle
tests passed on 2026-09-20 after correcting two MSI packaging defects. MSIX packing and test signing
passed; installation was blocked by certificate trust (0x800B0109). See the
[distribution test report](DISTRIBUTION_TEST_REPORT.md) and [install and updates](INSTALL_AND_UPDATES.md).

Latest Store-candidate audit: [Store readiness](STORE_READINESS.md). A fresh 1.1.0 MSIX
includes the current fixes; its extracted binaries passed 43 native checks, WPF
layout/license/multi-preview checks and real two-monitor video/shader E2E. Subsequent
installed-MSIX lifecycle and full local WACK checks passed; real Store licensing,
Store certification and clean-machine validation remain pending.

Fresh retest: 191 regression tests, 42 native/video checks and all seven automated EXE/MSI lifecycle
steps passed. The original destructive MSI is now rejected before installation. New build scripts emit
checksums and isolate staging, and CI requires tests before uploading installers. These changes do not
replace the external release gates below.

## Both channels

- [x] **Independent monitor wallpapers (1.1.0)** — selected-display Apply, separate Apply all,
  independent settings, pause/stop and persisted restoration. 262 unit tests plus WPF
  and actual two-monitor shader/video E2E checks passed. See [validation](PER_MONITOR_VALIDATION.md).

- [ ] **Code signing certificate** (SmartScreen). Builds are unsigned, so Windows warns on first
  run/install. Acquire a code-signing certificate and wire it in:
  - Velopack: `vpk pack --signTemplate "signtool sign ... {{file}}"` (or `--azureTrustedSignFile`).
  - MSIX: the Store signs its own package; for sideload use `-SelfSign` (test only).
  - MSI: run `fix-msi-package.ps1` before signing; never distribute the uncorrected Velopack MSI.
- [ ] **End-user terms** — the current repository `LICENSE` grants no right to use the software
  without a separate written agreement. Decide and provide the intended end-user license for public distribution.
- [ ] **Clean-machine validation** — test the installers on a Windows x64 machine without a .NET SDK,
  plus the real desktop/audio/mixed-DPI regression matrix. Developer-machine smoke tests are not a substitute.
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

- [ ] **Store listing support contact** — use `hello@mablook.com`, the confirmed HYPNIX
  support email. The Partner Center listing still needs to be updated with this contact.

- [x] **Store licensing implementation (1.0.1)** — full-featured trial, remaining-days banner,
  purchase, expiration enforcement and paid unlock. 217 unit tests and WPF license/layout
  checks pass. See [Store licensing](STORE_LICENSING.md).
- [ ] **Price, trial and real Store testing** — set EUR 4.99 and 15 days in Partner Center;
  validate a Store-delivered private build with real trial/purchase/offline licenses.

- [x] **Partner Center account and name reservation** — HYPNIX, Store ID `9MTRP976K91M`.
- [x] **Official identity** in `packaging/msix/AppxManifest.xml`: Name `Mablook.HYPNIX`,
  Publisher `CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72`, PublisherDisplayName `Mablook`.
  Expected package family: `Mablook.HYPNIX_pc2mes91x5ay8`.
  Do **not** self-sign for submission — Partner Center signs.
- [ ] **Additional logo scales** — only base sizes are generated (44/150/50/310). Add scaled variants
  (scale-125/150/200/400 and target-size icons) for a polished Store listing.
- [x] **Windows App Certification Kit** — the 1.1.0 candidate passed the full local
  Desktop Bridge/Centennial run overall with WACK 10.0.26100.7705: 23 individual
  passes and one optional `Blocked executables` finding for review. See [evidence](STORE_READINESS.md).
- [ ] **`runFullTrust` justification** — the wallpaper hosting (`SetParent` to Progman/WorkerW) needs
  the full-trust capability, which is reviewed at certification. Prepare the rationale (wallpaper apps
  are allowed, but review is expected).
- [x] **Final sideload validation** — signed test copies installed, upgraded from 1.0.1
  to 1.1.0, launched with actual package identity and uninstalled. Data preservation
  and removal of temporary certificate trust were verified. Store commerce remains untested.

## Tooling / environment

- [x] **Local packaging tools** — PowerShell 7.6.5, .NET SDK 10.0.300, Velopack 1.2.0 and Windows SDK
  10.0.26100.0 were available and used successfully on 2026-09-20. The application still targets .NET 8.

## Other known follow-ups (not release blockers)

- Effekseer fire effects (Fire Burst, Flamethrower Ring V2) still opt out of the palette,
  size/position and background controls; extending them means re-authoring the effect. See
  [UI architecture](UI_ARCHITECTURE.md).
- Physical multi-monitor/DPI matrix, high-contrast and screen-reader passes remain release-time
  validation. See [testing guide](TESTING_AND_REGRESSION_GUIDE.md) and
  [visual settings plan](VISUAL_SETTINGS_DESIGN_PLAN.md).
