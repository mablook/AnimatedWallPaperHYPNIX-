# Microsoft Store readiness — 2026-09-20

> Superseded for commerce on 2026-09-21: the app now uses Lemon Squeezy licences.
> The package and WACK evidence below predate this integration. Do not submit this
> candidate for the new sales model. See [Lemon Squeezy readiness](LEMON_SQUEEZY.md).

Decision: an updated candidate is prepared and its extracted binaries passed local
regression tests. It has not been uploaded or submitted. Public release readiness
has not been established; not every scenario has been tested.

## Candidate

- Package: `artifacts/store-readiness-20260920/msix/HYPNIX-1.1.0-x64.msix`
- Identity: `Mablook.HYPNIX`, version `1.1.0.0`, x64; official Mablook publisher.
- SHA-256: `B66324DCB2D5AC1B001692C10A56F3EA59F58848E2F52584645B71B4CC404B07`
- Rebuilt from the current working tree, including the foreground-thread crash fix,
  simultaneous previews, monitor format selection and Living Fire ultrawide fixes.
- Self-contained runtime, native renderer, Store assets and dependency notices are
  present. The test harness is absent from the MSIX. There is no local test signature.
- The existing `artifacts/store-submission/HYPNIX-1.1.0-x64.msix` predates these fixes.
  Use the candidate above for further validation. Confirm the version against any
  existing Partner Center submission before uploading; portal state was not inspected.

## Verified evidence

| Scenario | Result and scope |
| --- | --- |
| Unit regression | 302 passed in the latest source run; `artifacts/fire-preview-e2e/regression.trx` |
| MSIX packing and content | Passed identity, checksum, runtime, current shader/native hashes, test-harness exclusion and asset distribution-label checks |
| GPU/native smoke | 43 PASS checks on the candidate's extracted binaries; all 13 gallery wallpapers and native lifecycles |
| Living Fire | Full-width pixel coverage at 16:9, 21:9, 32:9 and portrait, including reduced flame sizes |
| WPF layout/settings | Library, preview, editor, presets, resets and responsive layouts passed on extracted binaries |
| Simulated monitors | Three/four independent previews, 16:9/21:9/32:9 format changes, small flames, resize, animation and cleanup passed |
| Real desktop | Two physical monitors, Living Fire/Kaleidoscope, simultaneous previews, video/shader, independent pause/replace/stop, restore and cleanup passed |
| Store-license UI | Trial, hidden-window expiration, purchase unlock and error recovery passed with a fake commerce provider |

Candidate-run logs and reports are under `artifacts/store-readiness-20260920/`.
The desktop report records `VideoSkipped: false`, with no remaining test HWNDs or
decoder processes. These runs exercised the actual published application binaries
and bundled runtime in an extracted copy with a test harness added only to that copy.
Those extracted-binary runs did not install the MSIX. The subsequent lifecycle run
below separately validated installation and actual package identity.

## Installed MSIX and WACK follow-up

`scripts/test-msix-lifecycle.ps1` ran with administrator approval, using separately
signed copies of the old 1.0.1 and current 1.1.0 packages. The original unsigned
Store candidate retained its SHA-256 above.

- Existing HYPNIX user data was backed up and verified before installation.
- Baseline 1.0.1 installed; upgrading to 1.1.0 preserved a LocalState canary.
- Activating the installed application produced a responsive window carrying the
  actual `Mablook.HYPNIX_1.1.0.0_x64__pc2mes91x5ay8` package identity.
- WACK 10.0.26100.7705 returned `OVERALL_RESULT="PASS"`, `PARTIAL_RUN="FALSE"`,
  application type `Centennial`, for version 1.1.0.0 on Windows 11 build 26200.
- Individual results: 23 PASS, one optional FAIL (`Blocked executables`). This
  scanner flags process-launch APIs and executable-name strings in HYPNIX, Velopack
  and bundled Microsoft/.NET libraries. It includes mixed-case string matches such
  as `REg` and `DnX`; these findings do not by themselves prove such commands execute.
  The optional finding remains recorded for certification review and must not be
  described as 24 clean individual passes or as a completed security audit.
- Uninstall passed. Existing non-log user files were unchanged. No test package or
  temporary certificates remain, confirmed independently after cleanup.

Evidence: `artifacts/store-readiness-20260920/msix-lifecycle-wack/result.json` and
`wack.xml`. The earlier `msix-lifecycle` attempt passed the lifecycle checks but WACK
failed on an unsigned-package reference; the corrected run reset WACK and explicitly
selected the signed candidate copy. The final script also checks the report's overall
result, identity and partial-run flag rather than equating exit code zero with a pass.
Backups and signed test copies in these audit folders must not be distributed.

## Remaining checks before public release

1. Installed-MSIX lifecycle and local WACK now pass on this development machine.
   Earlier EXE/MSI lifecycle results still apply to older direct-download installers.
2. Microsoft Store certification remains separate from the successful local WACK run.
3. Validate real Store acquisition, trial, localized price, cancelled/completed
   purchase, ownership, offline restart and expiration using Store-delivered builds.
4. Test clean Windows without the .NET SDK, additional GPU hardware, Windows 10,
   physical hotplug/docking, mixed DPI, sleep/wake and extended playback. Three/four
   monitors have been simulated; only two physical monitors were exercised here.
5. Complete/review the Partner Center listing: EUR 4.99, 15-day trial,
   `hello@mablook.com`, screenshots, privacy information and full-trust explanation.
   Portal configuration has not been inspected or changed during this audit.
6. Resolve the end-user terms: the bundled repository LICENSE currently says no
   right to use is granted except under a separate written agreement. No replacement
   customer license or published privacy URL was located in the project.

For a first, not-yet-public Store release, use a private audience to validate real
Store delivery before public availability. This still requires completing the
submission and passing certification; it does not replace the checks above.

## Draft full-trust explanation

HYPNIX is a WPF/Win32 desktop wallpaper application. It needs full-trust desktop
execution to create Direct3D wallpaper windows, attach them behind desktop icons
using the Windows desktop shell, enumerate monitors and react to display changes.
Audio-reactive effects analyze system playback through WASAPI loopback locally.
Local video playback uses FFmpeg selected by the user; media tools are not downloaded
automatically. Wallpapers and settings are selected and managed by the user.

References: [Microsoft beta testing and targeted distribution](https://learn.microsoft.com/en-us/windows/apps/publish/beta-testing-and-targeted-distribution),
[Windows App Certification Kit](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit).
