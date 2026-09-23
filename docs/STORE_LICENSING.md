# Microsoft Store licensing

> Historical implementation, superseded on 2026-09-21. Production HYPNIX uses
> [Lemon Squeezy licensing](LEMON_SQUEEZY.md) across all channels. The Microsoft
> purchase/trial setup below is retained for legacy regression context and is not
> a current configuration instruction or launch requirement.

HYPNIX uses Windows.Services.Store for the MSIX channel. The Store supplies the license,
trial expiration and localized purchase price. Configure **4.99 EUR, one-time purchase**
and **15 days, full-featured trial** in Partner Center for product `9MTRP976K91M`.
The application does not set the catalog price or trial duration itself.

## Customer experience

- Trial: all features available, remaining days rounded up, discrete purchase banner.
- Expired: stop desktop playback and live previews, close the editor, retain settings,
  imported wallpapers and presets. Show purchase and Check license actions.
- Purchased: remove trial banner, show ownership in App settings, restore access without
  automatically applying a wallpaper.
- Unknown license: ask the customer to connect and check their Microsoft Store account.
  A network error does not discard a previously verified in-memory valid license;
  an expired trial never gains extra time because of an error.
- Cancelled or failed purchases leave the license unchanged. A successful purchase dialog
  must be followed by an active non-trial Store license before unlocking.

The license is checked at startup, activation, Store license-change events and every five
minutes. A separate one-second timer enforces expiration while the app is hidden in the
tray. The running process uses monotonic elapsed time to prevent clock rollback extending
the trial. Windows manages persistent offline license data; HYPNIX does not create an
editable install-date file or saved unlock flag.

Unpackaged EXE/MSI/portable builds retain their existing behavior. This implementation
does not impose a separate local trial on the direct-download channel.

## Local verification and demonstration

```powershell
dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj
dotnet run --project Tests/Hypnix.NativeSmoke -- artifacts/store-trial --license-only
dotnet run --project Tests/Hypnix.NativeSmoke -- artifacts/store-trial --show-license-demo
```

The demo has isolated settings and a companion window for selecting 15 days, last day,
expired and purchased states. Buy simulates a confirmed purchase. The wallpaper preview
uses the real animated renderer, and Apply now changes the selected desktop monitor
through the production playback pipeline. Only the commerce/license provider is fake.
Close the companion window to exit and release the demonstration's wallpapers.
The fake provider exists only in the test executable; no demo switch ships in HYPNIX/MSIX.

Validation on 2026-09-20: 217 unit tests passed, including 14 licensing tests. Dedicated WPF
checks passed for expiration during hidden-window playback, preserved settings, denied
expired Apply, confirmed-purchase unlock without autoplay, countdown labels, compact/wide
screens and unavailable-license recovery. Captures are under `artifacts/store-trial`.
Existing settings and responsive-library smoke checks also passed.

## Remaining Store validation

Upload the updated MSIX and configure pricing/trial in Partner Center. Test a Store-delivered
build through a private audience/flight with eligible test accounts: acquire trial, retrieve
the localized price, cancel purchase, complete purchase, refresh an already-owned license,
and exercise offline/restart behavior and trial expiration. These real commerce/license
flows have not been validated by the local fake-provider tests. Windows may prevent an
already-expired trial from launching; the in-app expired page also handles an open process
whose trial expires during use.

References:
- https://learn.microsoft.com/en-us/windows/uwp/monetize/implement-a-trial-version-of-your-app
- https://learn.microsoft.com/en-us/windows/uwp/monetize/in-app-purchases-and-trials
