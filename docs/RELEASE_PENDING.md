# Release pending items

## Development candidate — 1.2.2, 27 September 2026

The owner installs the package and performs desktop E2E manually. The 1.2.1 manual
pass reproduced notification-bell flicker with the configuration window open;
**Hide preview stabilizes the bell**. Installed-build logs verified 1.2.1 and the
Discard preview swap chain, so that mitigation did not resolve the defect.

The 1.2.2 candidate retains the startup/update and async renderer developments,
build/source identification and per-session development logs. It renders GPU previews
offscreen and presents their pixels with GDI, without a window DXGI swap chain.
380 unit tests and the native graphics smoke passed, including pixel-exact animated
GPU-to-GDI preview checks. On 27 September 2026 the owner explicitly confirmed that
the notification-bell issue is resolved in 1.2.2. The installed session confirms
OffscreenGdi is active. See the
[consolidated release record](DEVELOPMENT_RELEASE_1.2.2.md) and
[development workflow](DEVELOPMENT_RELEASE_WORKFLOW.md).

## Latest Store release preparation — 1.2.0, 25 September 2026

The owner confirmed manual tests are complete. Current Store copy and the updated privacy
policy are prepared locally in [STORE_COPY_1.2.0.md](STORE_COPY_1.2.0.md) and
[PRIVACY_POLICY.md](PRIVACY_POLICY.md). The 1.2.0 installed-MSIX lifecycle passed:
1.1.1 installation, upgrade preserving LocalState, responsive launch with package identity,
and uninstall/temporary-certificate cleanup. The 380-test regression and WACK overall PASS
are documented in [the current submission record](STORE_SUBMISSION_1.2.0.md).
Partner Center was not accessed or changed during this preparation. Older portal observations
below are historical, not a statement of current publication status.

The owner's manual-test confirmation is accepted for this release preparation. It does not
provide a detailed hardware matrix or establish testing on a clean machine, additional GPUs,
Windows versions, or Store delivery.

Reviewed against repository documents, configuration and code on 2026-09-21.
Partner Center preparation and the checkout's visible Test-mode status were
subsequently inspected; see the [submission record](PARTNER_CENTER_SUBMISSION_20260921.md).

Current commerce plan: free Microsoft Store distribution, Lemon Squeezy one-time
licences and an app-managed 15-day trial. On 2026-09-23, `packaging/Commerce.props`
was updated to the published **Live** product (479529 / 1382043 / 2159220), and
version 1.1.1 was prepared for the Microsoft Store. The publication guard remains
active for Test or malformed configurations. See [the Live build record](STORE_LIVE_BUILD_20260923.md).
The old EUR 4.99 Microsoft purchase/trial plan is superseded and is not a current
launch requirement. See [commerce setup and checks](LEMON_SQUEEZY.md).

Latest commerce validation, 2026-09-23: **337 unit tests** and WPF licence fixtures
passed. The actual classes from the final 1.1.1 MSIX also passed the **Live**
activation, online validation, DPAPI persistence, new-process restoration and
deactivation lifecycle. All seven API responses succeeded; the isolated activation
was released and its trial start preserved. See [the Live build record](STORE_LIVE_BUILD_20260923.md).
The earlier 2026-09-21 [Test-mode report](LEMON_SQUEEZY_TEST_REPORT.md) and
[development handoff](DEVELOPMENT_STATUS_20260921.md) remain historical evidence.

## Positioning and sales materials

- [x] **Product positioning recorded** — **Relax. Have fun. Be cool.** Lead with
  relaxing, enjoying music through real-time animations and expressing personal
  desktop style. Reusable copy and demo guidance are in [sales strategy](MARKETING_STRATEGY.md).
- [ ] **Apply the positioning to public channels** — website, Microsoft Store
  listing, Lemon Squeezy description and campaign copy. Include the confirmed
  trial/purchase terms alongside the experience-led message.
  The English Microsoft Store draft now contains this positioning and two
  screenshots; its listing section is Complete, but it is not published yet.
- [ ] **Record the music-reactive demo** — real desktop footage showing a quiet
  scene, reaction to music and a customization change. Existing
  [product screenshots](product-screenshots/README.md) are still images.

## Application reliability and licence integration — current engineering focus

Owner direction recorded on 2026-09-21: **Lemon Squeezy manages purchases and the
commercial operation; our responsibility is a correctly working HYPNIX app.**
Checkout/payment processing, receipts, taxes, refunds and chargebacks are not
HYPNIX development tasks or app-release acceptance tests. The app consumes licence
status from the provider. See the [responsibility boundary](LEMON_SQUEEZY.md#responsibility-boundary--owner-direction-2026-09-21).

- [x] **Final application regression (owner acceptance)** — owner confirmed manual
  testing complete on 2026-09-25. The release checklist covers startup with a visible window/tray,
  wallpaper playback and real-time music reaction, settings persistence,
  independent monitors, pause/resume/stop and recovery. Follow the
  [testing guide](TESTING_AND_REGRESSION_GUIDE.md) for future regression runs. This is
  owner-reported acceptance, complemented by 380 automated tests and local MSIX lifecycle checks.
- [x] **Windows notification bell flickers while HYPNIX is in the foreground** —
  **Resolved in 1.2.2, owner-confirmed on 2026-09-27.** Previously reproduced on 1.2.0 and 1.2.1;
  wallpaper playback with the configuration UI hidden is stable. This concerns the
  Windows notification/Do Not Disturb bell, not HYPNIX's tray icon. In 1.2.1,
  **Hide preview stabilizes the bell**, isolating the trigger to the live preview.
  Installed logs verify build `1.2.1-20260927-153229-9f59b9e5` and the Discard path.
  Historical mitigations (DXGI window-association flags in 1.2.0 and a BitBlt/Discard
  preview swap chain in 1.2.1) were insufficient. Earlier general manual acceptance
  must not be read as confirmation that this specific defect is fixed.
  Candidate 1.2.2 removes the preview's HWND swap chain entirely: GPU offscreen
  rendering feeds a reusable staging texture and a GDI blit. Desktop FlipDiscard is
  unchanged. Logs distinguish `Presentation=OffscreenGdi; WindowSwapChain=False`.
  Native graphics and pixel-transfer regression passed. The owner subsequently
  confirmed: “foi resolvida a do sino.” This closes the reported defect based on
  manual acceptance of 1.2.2; the shell's exact trigger remains unproven.
  Future regression: keep the preview visible, switch wallpapers and resize; then
  hide the preview and hide the app. Check bell stability, animation and playback.
  Broader hardware coverage remains separate from this specific confirmation.
  [Development evidence](DEVELOPMENT_RELEASE_WORKFLOW.md),
  [original screenshot](assets/notification-bell-flicker-20260925.png).
- [x] **Make wallpaper settings easier to recognize and find** — manual feedback
  on local 1.1.2, 2026-09-25: the label `Customize` did not explain what the button
  opens and its visual emphasis was too weak. Implemented: the button in the selection
  footer is renamed `Wallpaper settings` with a settings (gear) glyph, an accent border
  and a raised background for contrast, a tooltip + accessible help text "Adjust
  sensitivity, intensity and appearance", and `AutomationProperties.Name` updated to
  match. It stays in the always-visible footer (available with the preview closed), the
  accent-filled `Apply` remains the distinct primary action, and the label uses
  `TextTrimming` with a modest `MinWidth` so it degrades gracefully at compact widths
  and high DPI. The editor already shows only controls each wallpaper supports
  (SupportsColorTheme/Layout/Glow/Background/Sparks). Status: implemented; owner confirmed manual
  testing complete on 2026-09-25. Additional high-DPI configurations were not itemized.
  [User screenshot](assets/wallpaper-settings-button-feedback-20260925.png).
- [x] **Allow system audio, microphone or both as explicit reactive sources** —
  manual feedback on local 1.1.2, 2026-09-25: the old `React to microphone` mixed both
  inputs and depended on the parent toggle, so microphone-only could not be selected.
  Implemented: the Sound settings now offer one `Audio source` choice — `System audio`,
  `Microphone`, or `System audio + microphone` — beside the general `Audio reactive`
  on/off switch, mirrored by an `Audio source` submenu in the tray. The router
  (`AudioSpectrumRouter.SetSource`) opens only the selected sources; microphone-only
  never starts the system loopback. Switching releases the deselected source and clears
  its residual bands immediately. The selection persists (`AudioSource`, serialized as a
  readable name) and migrates existing files without changing behavior (old microphone
  on = both; off = system; master off stays off); new profiles default to system audio.
  A missing/denied microphone keeps retrying without silently switching sources, and the
  analysis stays local (no saved/transmitted audio). Covered by unit tests for the
  router source/device behavior and the settings migration. Status: implemented; owner confirmed
  manual testing complete on 2026-09-25. Individual source/device scenarios were not itemized
  separately; this acceptance is distinct from the automated source-selection checks.
  [User screenshot](assets/audio-source-selection-feedback-20260925.png).
- [x] **List audio devices and show live microphone feedback** — manual feedback
  on local 1.1.2, 2026-09-25, with Teams as the interaction reference. Implemented via a
  new `AudioDeviceService` (NAudio `MMDeviceEnumerator` + `IMMNotificationClient`): the
  Sound panel lists microphones by their Windows friendly names with a `System default
  (Name)` entry that follows Windows, marks the current default, and refreshes on
  connect/disconnect. Explicit selections persist by stable device id
  (`MicrophoneDeviceId`/`SystemAudioDeviceId`) and a chosen-but-absent device is kept as
  "unavailable" rather than silently switched. System-audio reaction offers the output
  devices separately from the microphone list. A new `AudioLevelMeter` drives a
  Teams-style segmented `LevelMeter` control from a microphone-only capture, so combined
  mode shows the mic's own level (system playback cannot fake it), with status text for
  "No microphone found", "Microphone unavailable", "Microphone access is blocked",
  "Listening" and "Sound detected". Enumeration/opening the list never captures: the
  meter runs only while the panel is visible and either the microphone is the active
  source or the user starts an explicit `Test`, and it is released when the test ends or
  the panel closes. Analysis is transient/local — nothing recorded, replayed or sent.
  Status: implemented; owner confirmed manual testing complete on 2026-09-25. Physical hotplug,
  permission and meter scenarios were not individually logged by the agent.
  [User reference](assets/audio-devices-level-feedback-20260925.png).
- [x] **Verify supplied Live configuration** — `packaging/Commerce.props` now uses
  the published Live product and checkout. All identifiers were verified inside
  the final 1.1.1 MSIX assembly; the Test-mode publication guard remains active.
  See [the build evidence](STORE_LIVE_BUILD_20260923.md).
- [x] **App licence code and fixture checks** — checkout without automatic unlock,
  trial/expiry, offline allowance, activation-limit/invalid/revoked responses,
  API failure/recovery and encrypted persistence passed in controlled unit/UI
  fixtures. The final MSIX's actual classes additionally passed activation,
  online validation, new-process restoration and deactivation with an issued
  Live license on 2026-09-23. Installed-MSIX UI and Store delivery checks remain
  separately pending below; no live-payment or refund-operation test was added.

## Product and distribution documentation

- [ ] **Publish/adopt the English privacy and terms updates** — the Lemon Squeezy
  data flow must be reflected in public policies. The updated [privacy policy](PRIVACY_POLICY.md)
  and [Store copy](STORE_COPY_1.2.0.md) are ready locally as of 2026-09-25; this task did not
  apply them to any public channel or inspect publication status. Terms
  [amendments](LEMON_SQUEEZY_POLICY_UPDATES.md) remain drafts.

## Both channels

- [x] **Independent monitor wallpapers (1.1.0)** — selected-display Apply, separate Apply all,
  independent settings, pause/stop and persisted restoration. 262 unit tests plus WPF
  and actual two-monitor shader/video E2E checks passed. See [validation](PER_MONITOR_VALIDATION.md).

- [ ] **End-user terms** — the current repository `LICENSE` grants no right to use the software
  without a separate written agreement. Decide and provide the intended end-user license for public distribution.
- [ ] **Clean-machine validation** — test the installers on a Windows x64 machine without a .NET SDK,
  plus the real desktop/audio/mixed-DPI regression matrix. Developer-machine smoke tests are not a substitute.
- [x] **Bump `<Version>`** — 1.2.0 in `AnimatedWallPaper.csproj` for the Sound/audio-source,
  device-feedback and wallpaper-settings feature release (was 1.1.1 for the prior Live build).
  Continue bumping for every release (SemVer). It flows to the Velopack package version and the
  MSIX `x.y.z.0` version. Confirm the next version against any existing submissions; do not reuse
  old example version numbers.
- [x] **Complete local installed-package lifecycle validation** — the 1.2.0 candidate
  passed installation/upgrade from 1.1.1, LocalState preservation, responsive launch
  with actual package identity, and uninstall on 2026-09-25. The test used signed copies
  and left the Store upload file unchanged. Clean-machine and Store-delivery checks
  remain distinct. See [the current submission record](STORE_SUBMISSION_1.2.0.md).

## Website installer (Velopack) — if this channel is released

- [ ] **Code signing certificate** (SmartScreen) — acquire and wire a trusted
  signature into direct-download EXE/MSI packaging. Run `fix-msi-package.ps1`
  before signing the MSI. Microsoft Store signing is handled separately by the Store.
- [ ] **Set `UpdateFeedUrl`** in `Services/UpdateService.cs` to the real public release host. It is
  currently the placeholder `https://updates.example.com/hypnix/win`, so the auto-update check is a
  safe no-op until set.
- [ ] **Provision the release host** (static site / CDN / Amazon S3 / Azure Blob) that serves
  `releases.win.json` and the `.nupkg` files.
- [ ] **`RELEASE_FEED_URL` repo variable** — set it so the Release workflow can pull the live release
  and build deltas in CI. Without it, CI produces full packages only.
- [ ] **Publish the first release** — run `scripts/package-release.ps1` with the confirmed
  next version (or use the Release workflow) and copy its output to the host. Verify a real installed build updates from
  the host, not only from a local `HYPNIX_UPDATE_FEED`.

## Microsoft Store (MSIX)

- [x] **Store listing support contact** — `hello@mablook.com` is saved in Properties
  and certification notes. Properties is Complete, with privacy policy supplied
  directly as text using the portal's supported alternative to a URL.

- [x] **Store price and English listing draft** — owner-confirmed EUR 0 acquisition;
  pricing and listing sections are Complete in Submission 1. Copy discloses the
  in-app trial and external purchase. See the [submission record](PARTNER_CENTER_SUBMISSION_20260921.md).

- [ ] **Configure the current commercial model** — free Store acquisition, no
  Microsoft-managed trial or paid base-app licence, and clear disclosure of the
  15-day in-app trial and external one-time purchase. Declare third-party commerce,
  confirm applicable account/business-verification requirements and include
  dedicated reviewer activation instructions in private certification notes.
- [ ] **Test Store delivery with Lemon Squeezy** — acquisition of the final Store
  build, local trial/expiry, checkout/activation, restart, offline allowance and
  recovery. The legacy Microsoft-provider tests do not validate this flow.

- [x] **Partner Center account and name reservation** — HYPNIX, Store ID `9MTRP976K91M`.
- [x] **Official identity** in `packaging/msix/AppxManifest.xml`: Name `Mablook.HYPNIX`,
  Publisher `CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72`, PublisherDisplayName `Mablook`.
  Expected package family: `Mablook.HYPNIX_pc2mes91x5ay8`.
  Do **not** self-sign for submission — Partner Center signs.
- [ ] **Additional logo scales** — only base sizes are generated (44/150/50/310). Add scaled variants
  (scale-125/150/200/400 and target-size icons) for a polished Store listing.
- [x] **WACK and local installed-MSIX validation on the final build** — 1.2.0 WACK
  overall PASS with one optional finding, identical tested/upload payloads, and a
  successful installed-package lifecycle. See [the evidence](STORE_SUBMISSION_1.2.0.md).
- [ ] **`runFullTrust` justification and certification** — review/adapt the
  [existing draft rationale](STORE_READINESS.md#draft-full-trust-explanation), include
  it in the submission and complete Store certification.

## Historical validation — retained as evidence

The 2026-09-20 EXE/MSI lifecycle tests passed after two MSI packaging fixes. The
earlier MSIX certificate-trust failure was resolved in subsequent signed-test
installation, upgrade and uninstall checks. The 1.1.0 candidate also passed 43
native checks, WPF/layout/multi-preview and real two-monitor video/shader checks.
Local WACK passed overall, with 23 individual passes and one optional
`Blocked executables` finding retained for review. These packages predate the
Lemon Squeezy integration. See [distribution evidence](DISTRIBUTION_TEST_REPORT.md)
and [Store evidence](STORE_READINESS.md).

Legacy Microsoft Store purchase/trial implementation and tests remain documented
in [Store licensing](STORE_LICENSING.md); they are not the current sales model.

## Tooling / environment

- [x] **Local packaging tools** — PowerShell 7.6.5, .NET SDK 10.0.300, Velopack 1.2.0 and Windows SDK
  10.0.26100.0 were available and used successfully on 2026-09-20. The application still targets .NET 8.

## Remaining manual validation

- Physical multi-monitor/DPI, hotplug/docking, sleep/wake, extended playback,
  additional GPUs/Windows 10, high-contrast and screen-reader passes remain
  release-time validation. Three/four monitors were simulated; only two physical
  monitors were exercised. See [testing guide](TESTING_AND_REGRESSION_GUIDE.md),
  [monitor validation](PER_MONITOR_VALIDATION.md) and
  [visual settings plan](VISUAL_SETTINGS_DESIGN_PLAN.md).

## Other known follow-ups (not release blockers)

- Effekseer fire effects (Fire Burst, Flamethrower Ring V2) still opt out of the palette,
  size/position and background controls; extending them means re-authoring the effect. See
  [UI architecture](UI_ARCHITECTURE.md).
- Playlists, web interactivity, a remote catalog and macOS remain future directions,
  not promises for the current launch. See [product direction](PRODUCT_DIRECTION.md)
  and [macOS study](MACOS_PORT_STUDY.md).

## Ocean integration regression — 2026-09-28

The owner reported notification-bell flicker again in the new Ocean integration. The earlier 1.2.2 acceptance remains historical. Ocean previews now use WPF composition with no preview HWND or window swap chain; manual bell acceptance is pending. See [implementation and regression evidence](OCEAN_USER_EXPERIENCE_IMPLEMENTATION.md).
