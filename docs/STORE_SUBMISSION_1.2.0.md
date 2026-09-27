# HYPNIX 1.2.0 — Microsoft Store submission record

## Final local preparation — 25 September 2026

- Updated English description, short description, features, release notes and private reviewer notes are ready in [STORE_COPY_1.2.0.md](STORE_COPY_1.2.0.md).
- The [privacy policy](PRIVACY_POLICY.md) now describes the three audio sources, saved device selection and microphone meter/test lifecycle. That revision was dated 25 September 2026; the linked policy was subsequently updated on 27 September to describe development-session logs.
- The owner confirmed completion of manual testing on 25 September 2026. This is owner-reported acceptance; no detailed per-device test matrix was supplied.
- The exact Store candidate passed a new local installed-package lifecycle test: install 1.1.1, upgrade to 1.2.0 with LocalState preservation, launch a responsive window with the real 1.2.0 package identity, and uninstall.
- Existing HYPNIX data was backed up and seven pre-existing files checked; non-log files remained unchanged. Test certificates and package were removed, independently verified after the run. The unsigned upload SHA-256 remains unchanged.
- Evidence: `artifacts/store-audit-120/installed-lifecycle/result.json` and `independent-cleanup.json`. This was a signed test copy on the development PC, not Store delivery or a clean-machine test. WACK was not repeated; the existing 1.2.0 PASS report remains applicable to the unchanged payload.
- The preceding audit reran 380 unit tests and five native checks on the packaged DLL. See `artifacts/store-audit-120/RELATORIO.md` for that audit's original scope; its privacy/manual/install findings are followed up here.
- Partner Center was not accessed or changed. These files are prepared for the owner to apply; no submission or publication was performed.

Build and validation performed on 2026-09-25 for the Sound/audio-source, device-feedback and
wallpaper-settings feature release. Commit: `fc3726d` (`Add audio source selection, device feedback
and clearer wallpaper settings`). Version bumped `1.1.2 -> 1.2.0` in `AnimatedWallPaper.csproj`.

Environment: .NET SDK 8.0.421 (app targets net8.0-windows10.0.19041.0), Windows 10/11 SDK
`10.0.26100.0` (makeappx/signtool/appcert), PowerShell 7. Commerce is **Live** in
`packaging/Commerce.props`, so the publication guard (`RequireCommerceConfiguration`) passes.

## Packages produced

Both are self-contained (bundle the .NET runtime) with ReadyToRun, built from the same commit.
They live under `artifacts/` which is **git-ignored** (the binaries are not committed; this record is).

| Purpose | Path | Size | Signing | SHA-256 |
| --- | --- | --- | --- | --- |
| **Store submission** | `artifacts/msix/HYPNIX-1.2.0-x64.msix` | ~128 MB | **Unsigned** (Partner Center signs on submission) | `6ACECABC7C998E21EE56CAF610DBBFCBF11A185EFFCD2A16B950BC948DB4A765` |
| Local sideload test | `artifacts/msix-selfsign/HYPNIX-1.2.0-x64.msix` | ~128 MB | Self-signed (test only) | `BC51B31DBC72767B64864F56C802409D95CA9C72C6B7207E7246FF854D2BD0FD` |

Package identity (verified inside the `.msix`): `Name=Mablook.HYPNIX`,
`Publisher=CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72`, `Version=1.2.0.0`, `ProcessorArchitecture=x64`.
This matches the official Partner Center identity (Store ID `9MTRP976K91M`).

Self-sign certificate (test only): `artifacts/msix-selfsign/HYPNIX-selfsign.cer`,
`Subject=CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72`, `Thumbprint=621B6D3DB21F685873F56BE275420D729583D25E`,
valid until 2027-09-25. It also remains in `Cert:\CurrentUser\My` for future re-signing.

### How they were built

```powershell
# Store package (unsigned; upload this one to Partner Center)
pwsh ./scripts/package-msix.ps1 -Version 1.2.0

# Local sideload package (self-signed; DO NOT submit this one)
pwsh ./scripts/package-msix.ps1 -Version 1.2.0 -SelfSign -OutputDir artifacts/msix-selfsign
```

> Use the **unsigned** package from `artifacts/msix/` for this submission. The self-signed
> copy is a local testing artifact; Microsoft handles Store package signing.

## WACK (Windows App Certification Kit) result

Run against the self-signed sideload package with `appcert.exe` (SDK `10.0.26100.0`). WACK requires
elevation; it installed, tested and uninstalled the package automatically.

- **OVERALL_RESULT: PASS**
- Full report: `artifacts/msix-selfsign/WACK-1.2.0-report.xml` (~8 MB, git-ignored).
- One **optional** finding (does not fail the local WACK overall result; Microsoft certification is separate):
  - Requirement 25 *Package sanity test* -> test **"Blocked executables"** (`OPTIONAL=TRUE`) = FAIL.
  - Cause: HYPNIX legitimately references process-launch APIs — `ffmpeg`/`ffprobe` for local video
    wallpapers and `Process.Start` to open the library/diagnostics folders and the Lemon Squeezy
    checkout URL in the browser. This is the same optional finding recorded in prior 1.1.x WACK runs
    and is expected for a `runFullTrust` desktop app; retain the report for the submission.

To reproduce (elevated PowerShell):

```powershell
$appcert = "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe"
& $appcert reset
& $appcert test `
  -appxpackagepath "artifacts\msix-selfsign\HYPNIX-1.2.0-x64.msix" `
  -reportoutputpath "artifacts\msix-selfsign\WACK-1.2.0-report.xml"
```

## Local sideload test (optional, before submitting)

Requires an **elevated** PowerShell (trusting a cert to the machine store and installing a
self-signed MSIX both need admin). This validates the packaged app on this PC; it is not required
for the Store submission.

```powershell
# 1) Trust the test certificate (machine-wide), once
Import-Certificate -FilePath 'artifacts\msix-selfsign\HYPNIX-selfsign.cer' `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2) Install the signed package
Add-AppxPackage 'artifacts\msix-selfsign\HYPNIX-1.2.0-x64.msix'

# 3) Launch from Start menu ("HYPNIX") and run the 1.2.0 test pass
#    (Sound: audio source + device lists + mic meter; Wallpaper settings button; tray audio source)

# 4) Uninstall when done
Get-AppxPackage -Name Mablook.HYPNIX | Remove-AppxPackage

# 5) Remove the test trust (optional cleanup)
Remove-Item 'Cert:\LocalMachine\TrustedPeople\621B6D3DB21F685873F56BE275420D729583D25E'
```

Current state after this run: the app is **not** installed (WACK uninstalled it), and **no** test
certificate was left in `Cert:\LocalMachine\TrustedPeople`.

## Partner Center submission checklist

1. Create a new submission for HYPNIX (Store ID `9MTRP976K91M`).
2. **Packages** -> upload `artifacts/msix/HYPNIX-1.2.0-x64.msix` (the unsigned one). Partner Center
   signs it with the product certificate on ingestion.
3. Confirm the commercial model: free acquisition, no Microsoft-managed trial/paid base licence,
   with clear disclosure of the in-app 15-day trial and the external one-time Lemon Squeezy purchase.
4. **Certification notes (private)** — include reviewer activation instructions and the
   `runFullTrust` justification (draft in `docs/STORE_READINESS.md`): the app uses Win32 full trust to
   host the wallpaper on the shell (`SetParent` to Progman/WorkerW) and launches `ffmpeg` locally for
   video wallpapers. Attach/keep the WACK report.
5. Apply the English listing/positioning and submit for certification.

## Reviewer / certification notes

- **runFullTrust**: required to render a live wallpaper behind the desktop icons. HYPNIX finds
  `Progman`, spawns `WorkerW` via message `0x052C`, and `SetParent`s its render window under the
  shell (`Services/DesktopWorker.cs`). GPU visualizers present via DXGI; classic ones via a GDI
  swap chain on the Windows 11 raised desktop.
- **Process launch**: `ffmpeg`/`ffprobe` (user-provided media tools) decode local videos; `Process.Start`
  opens local folders and the external checkout page. No silent background execution.
- **Audio (new in 1.2.0)**: microphone capture is opt-in via the explicit **Audio source** choice and
  is analyzed transiently in memory only — never recorded, saved or transmitted. A denied/missing
  microphone is non-blocking. See `docs/PRIVACY_POLICY.md` and `docs/PRODUCT_DIRECTION.md`.

## Manual validation follow-up

The owner reported that manual tests were completed on 25 September 2026, after the audit
listed notification-bell behavior, physical microphone/device feedback and the three audio-source
modes as outstanding. Record these as owner-reported acceptance, not independent automated
proof of every hardware or permission scenario. No further manual test request is pending in
this preparation task. Broader compatibility coverage remains tracked in `docs/RELEASE_PENDING.md`.
