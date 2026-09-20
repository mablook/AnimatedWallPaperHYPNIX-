# Distribution validation — 2026-09-20

**Decision: ready for further controlled testing; not ready for public release.**

## Fresh retest with hardened packaging

The second run on 2026-09-20 rebuilt every installer in fresh staging directories. Evidence and full
user-data backups are in `artifacts/distribution-retest-20260920-103219/`.

- 191 automated tests passed again; 42 native/GPU checks passed, including actual FFmpeg video
  first frame, 15 FPS playback, pause/resume and decoder shutdown.
- The new `scripts/test-distribution-install.ps1` passed all seven steps: EXE install/update/uninstall
  and MSI install/repair/upgrade/uninstall. Every boundary checked every backed-up data file and a canary.
  Repair was verified by deleting the installed application DLL and checking its restored SHA-256.
- The read-only MSI gate accepts the corrected package and rejects the original destructive package
  **before installation**. Both local packaging and CI use the same gate.
- The installed EXE was checked through the actual interface: Start reached `Running · display 2 paused`,
  settings opened and closed, and Stop reached `Stopped` with native-host destruction in the log.
  The installed app again downloaded update 1.0.1 from the local feed.
- EXE/MSI uninstall removed the test application; EXE cleanup also verified removal of Desktop and
  Start Menu shortcuts. Original settings and all three referenced backgrounds remain byte-identical.
  This run caused no data-loss incident; diagnostic logs grew from the tests.
- Release checksums, full/delta feed SHA-256 and sizes, and portable runtime/native/license payloads passed.
- NuGet's current vulnerability query reported no vulnerable direct or transitive packages. This query
  does not audit native source or the full bundled runtime for every possible vulnerability.
- New unsigned Store and self-signed sideload MSIX packages were generated. Certificate trust remains
  the installation gate; this session is not elevated and Windows Sandbox is not installed.

The delivery folder for version 1.0.0 is `release-1.0.0/`, containing the EXE, corrected MSI, portable ZIP,
full update package, feed, build metadata and `SHA256SUMS.txt`. Version 1.0.1 is test-only. The metadata
explicitly records `publicReleaseReady: false`; no trusted production signature or public feed is configured.
Do not distribute the surrounding audit directory, which contains private data backups.

Packaging now retains unique working directories instead of recursively clearing a shared publish folder.
The raw Velopack MSI stays in staging until corrected and validated. Stable versions are checked against
Windows Installer limits, and unsupported update channels are rejected. The release workflow now runs
regression tests plus real install/repair/uninstall tests before uploading deliverables. That workflow has
been updated locally, but its execution on GitHub has not been observed in this session.

The first run and its incident are retained below for traceability.

Source: `41a1574280c117de1aaeee5dfd169d680dcc7c03` plus the packaging corrections in this working tree.
Product version remains `1.0.0`. Version `1.0.1` was built only for local update tests; nothing was published.
Host: Windows 11 x64, build 26200, NVIDIA GeForce RTX 5070 Ti. PowerShell 7.6.5,
.NET SDK 10.0.300, Velopack 1.2.0 and Windows SDK 10.0.26100.0.

## Results

| Check | Result |
| --- | --- |
| Automated regression suite | 191 passed, 0 failed, 0 skipped |
| Published native/GPU smoke | Exit 0; 41 PASS checks, all 13 gallery wallpapers and renderer lifecycles |
| EXE silent install | Exit 0; installed under `%LocalAppData%\HypnixWallpaper`; settings byte-identical |
| EXE launch | Responsive HYPNIX window; native renderer and audio capture initialized |
| Local update feed | App downloaded 1.0.1; updater applied it with exit 0; new version launched |
| EXE uninstall | Exit 0; executable removed; settings unchanged by uninstall |
| Corrected MSI install | Exit 0; correct per-user directory; settings unchanged |
| Corrected MSI launch | Responsive HYPNIX window, version 1.0.1 |
| MSI repair | `msiexec /fa /qn`: exit 0 |
| MSI upgrade | `msiexec /i /qn`: 1.0.0 → 1.0.1, exit 0; settings unchanged |
| Corrected MSI uninstall | Exit 0; executable removed; all 5 user-data files checked remained byte-identical |
| MSIX unsigned pack | `makeappx pack` succeeded; runtime/native payload and notices present |
| MSIX test signing | `signtool sign` succeeded with a self-signed certificate |
| MSIX installation | **Blocked:** `Add-AppxPackage` returned 0x800B0109 (test certificate not trusted) |
| Portable package | 13 wallpapers; 675 files validated; every ZIP entry verified against file-manifest SHA-256 |

The update test compared initial and final settings and observed a selected-wallpaper change while the UI
was running; it does not establish byte-identical settings over that entire interactive session. The separate
silent MSI upgrade and installer/uninstaller boundary checks did preserve settings. Final cleanup confirmed
the settings file matches the pre-test backup.

## Defects found and corrected

1. EXE and MSIX packaging omitted the checked-in third-party licenses and dependency notices.
   `copy-distribution-notices.ps1` now supplies all three packaging scripts and the release workflow.
2. Velopack 1.2.0's MSI places `INSTALLFOLDER` directly under `TARGETDIR`. A silent install reported success
   but wrote to `C:\HYPNIX`. The MSI correction makes the default `%LocalAppData%\HypnixWallpaper`.
3. The MSI's `RustAppId` was the display title `HYPNIX`, not package ID `HypnixWallpaper`. Its uninstall
   cleanup removed `%LocalAppData%\HYPNIX`. The correction uses `HypnixWallpaper`, keeping user data separate.

**Test incident:** uninstalling the original defective MSI removed the local HYPNIX data directory.
`settings.json` was recovered byte-for-byte from the pre-test backup. All three background images referenced
by the settings were recovered from their source files in Pictures/Screenshots and `artifacts/e2e-desktop`;
the recovered copies match those sources by SHA-256. The library had reported zero packages before the test.
Prior diagnostic logs were not recovered, and there was no full pre-test inventory to establish whether other
unreferenced files existed. A full backup of the recovered data is now retained with the test evidence.
Corrected MSI install/repair/upgrade/uninstall was then retested successfully.

`fix-msi-package.ps1` is mandatory after `vpk pack --msi` in local builds and CI. It refuses signed packages:
apply this correction before signing. Revisit it on Velopack upgrades. MSI and EXE are alternative per-user
installers; mixing both installer types over the same existing installation has not been validated.

## Test artifacts

All files are under `artifacts/distribution-audit-20260920/` (git-ignored):

- `final-release/HypnixWallpaper-win-Setup.exe` — corrected packaging, 1.0.0, unsigned.
- `final-release/HypnixWallpaper-win.msi` — corrected MSI, 1.0.0, unsigned.
- `msix/HYPNIX-1.0.0-x64.msix` — unsigned Store candidate, placeholder identity.
- `msix-sideload/HYPNIX-1.0.0-x64.msix` and `HYPNIX-selfsign.cer` — local tests only.
- Adjacent `.sha256` files and `packages.json` — sizes, SHA-256 and signature status.
- `results/regression.trx`, `native-smoke.log`, MSI logs and the `*-result.json` / lifecycle JSON records.
- `user-data-backup/`, `settings-before.json`, `background-recovery.json` — private recovery evidence;
  **do not distribute the audit directory**.
- `unsafe-original-msi/DO-NOT-INSTALL.msi` — defective original retained as evidence only.

EXE/MSI test installations were removed after validation. The MSIX never installed. A test signing
certificate was created in CurrentUser/My; it was not added to a trusted certificate store.
Checksums are integrity checks, not code signatures.

## Remaining release gates

- Production signing for EXE, MSI and application binaries; integrate MSI signing after correction.
- Real public update host and replacement of `https://updates.example.com/hypnix/win`; validate updates
  over that host. The tests used a local folder only.
- End-user terms: the current repository license grants no use rights without a separate written agreement.
- Store account/product identity and assets, full-trust justification, WACK and certification.
- MSIX sideload trust/install/launch/uninstall validation on an appropriately administered test machine.
- Clean Windows machine without a .NET SDK and the physical desktop/audio/monitor/DPI regression matrix.

References: [Velopack installer documentation](https://docs.velopack.io/packaging/installer) and
[Microsoft's MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide).
The MSI findings above come from the actual 1.2.0 package and lifecycle logs, not assumptions from documentation.
