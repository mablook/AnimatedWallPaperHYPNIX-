# Development handoff — 2026-09-21

The working application includes independent monitor wallpapers, simultaneous live
previews and Lemon Squeezy licensing. Test-mode commerce has passed checkout and
actual License API validation. Public distribution is still pending.

Owner scope clarification, 2026-09-21: Lemon Squeezy manages purchases, payments,
receipts, taxes, refunds and chargebacks. Our engineering focus is the functioning
application, including correct integration with the licence API. Do not turn
provider financial operations into HYPNIX implementation or acceptance tasks.
See [responsibilities](LEMON_SQUEEZY.md#responsibility-boundary--owner-direction-2026-09-21).

## Changes included

- Product positioning now centers on **Relax. Have fun. Be cool.**: relaxing,
  enjoying music through real-time visuals and giving the desktop personal style.
  Repository sales guidance is recorded; public copy and a music-reactive demo
  remain launch tasks.
- Foreground monitor callbacks return to the owning WPF Dispatcher even when the
  monitor is constructed before the WPF synchronization context exists. This
  addresses the cross-thread crash observed while changing monitor backgrounds.
- Independent wallpapers and settings remain associated with each display.
  A simultaneous preview grid shows real displays or simulates three/four displays;
  each simulated display can choose its wallpaper and aspect ratio. Simulation
  does not apply wallpapers to the desktop.
- Living Fire distributes flame sources according to the render surface aspect
  ratio, including ultrawide previews. Native checks cover preview and render output.
- The actual app now uses Lemon Squeezy across distribution channels, with a
  15-day local trial, encrypted licence state, activation/deactivation and a
  seven-day offline allowance. Opening checkout alone does not unlock the app.
- Test-mode configuration blocks publishing. Public product identifiers are in
  `packaging/Commerce.props`; no merchant API key is needed in the desktop app.
- Test harnesses cover WPF licence controls, actual remote licence lifecycle,
  monitor previews, ultrawide fire and MSIX installation/update/uninstallation.

## Evidence and operating instructions

| Area | Repository document |
| --- | --- |
| Product positioning, benefit-led copy and sales demos | [Sales strategy](MARKETING_STRATEGY.md) |
| Commerce configuration, design and launch steps | [Lemon Squeezy](LEMON_SQUEEZY.md) |
| Checkout, actual API and local test results | [Commerce test report](LEMON_SQUEEZY_TEST_REPORT.md) |
| English policy amendments, not yet published/adopted | [Policy drafts](LEMON_SQUEEZY_POLICY_UPDATES.md) |
| Monitor/crash/preview/ultrawide validation | [Per-monitor validation](PER_MONITOR_VALIDATION.md) |
| Historical MSIX/WACK evidence | [Store readiness](STORE_READINESS.md) |
| Outstanding distribution work | [Release checklist](RELEASE_PENDING.md) |
| Product screenshots and provenance | [Screenshot set](product-screenshots/README.md) |

Latest local validation: 334 unit tests passed, native WPF licence regression
passed, and the native harness built without warnings/errors. The issued test
licence passed actual API activation, repeat activation without a new instance,
restoration, validation, deactivation and reactivation. Final cleanup released the
test slot. The key and customer/order data are excluded from source control.

The test checkout displays EUR 3.99. EUR 5.00 was an earlier proposal. Final live
pricing and device activation limits are provider-side configuration inputs. Store 479529, Product
1377284 and Variant 2151696 are the tested configuration, not approved live values.

Local logs, test reports, encrypted state and installers remain in ignored
`artifacts/` folders. Committed reports summarize that evidence; their local paths
will not exist automatically in a fresh clone. Product screenshots are committed
separately so they remain available from a fresh checkout.

## Before release

Verify the supplied Live licence configuration and the app's licence lifecycle,
including invalid/limit responses, network failure and recovery. Rebuild and
validate the exact final packages: startup/window/tray, wallpaper/audio reaction,
monitors, settings, pause/resume/stop, installation and clean-machine checks.
Product policies, customer licence terms and Partner Center settings remain
distribution work. Earlier EXE/MSI/MSIX packages and WACK evidence predate the
latest licence integration and do not establish readiness for the final package.
