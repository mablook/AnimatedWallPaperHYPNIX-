# HYPNIX — Microsoft Store distribution and Lemon Squeezy licensing

## Product identifiers — owner reference, 2026-09-23

Identifiers and one-time prices supplied by the owner:

| Identifier | Just Fits Pro | HYPNIX |
| --- | --- | --- |
| Lemon Squeezy Store ID | `479529` | `479529` |
| Product ID | `1381996` | `1382043` |
| Variant ID | `2159154` | `2159220` |
| Microsoft Store ID | `9N93RTWN38DF` | `9MTRP976K91M` |
| One-time price | €2,99 | €3,99 |

`packaging/Commerce.props` now uses the published **Live** HYPNIX product and
checkout `https://mablook.lemonsqueezy.com/checkout/buy/a117a3f9-3a8c-4c78-804c-3ea051f29a89`.
The one-time license has no expiry and allows five activations. Version 1.1.1
is the Microsoft Store build for this configuration. See
[the Live build record](STORE_LIVE_BUILD_20260923.md) for package checks and limits.
The earlier Test configuration and its validation remain recorded below as history.

Live follow-up on 2026-09-23: the exact 1.1.1 MSIX licensing classes passed
activation, online validation, encrypted persistence, restoration in a new process,
and deactivation using an issued EUR 0 Live-order license. One activation, five
validations and one deactivation all returned HTTP 200. The isolated entitlement
was cleared and the original trial start preserved. This did not install the MSIX
or test WACK/Store certification; receipt email delivery is unconfirmed. See
[the dated evidence](STORE_LIVE_BUILD_20260923.md#live-license-lifecycle-follow-up--2026-09-23).

## Historical implementation status — 2026-09-21

Status: implementation prepared on 2026-09-21. The owner supplied Store ID
`479529`, Product ID `1377284`, Variant ID `2151696`, and checkout URL
`https://mablook.lemonsqueezy.com/checkout/buy/116a9d7d-9a17-4fd3-97d6-8769347438c5`.
These were saved in `packaging/Commerce.props` at the time. The owner confirmed Test mode;
the browser checkout also displays its Test mode banner. `LemonSqueezyMode` is
then set to `Test`, blocking publication while allowing normal builds and tests.
The checkout displays EUR 3.99, rather than the originally proposed EUR 5.00.
One approved test purchase and one declined test payment were verified in the
browser. The owner performed the final payment clicks. The issued test key passed
real License API activation, validation, persistence and deactivation checks.
No live purchase, merchant portal change, or Store submission was made.
Previous MSIX candidates use Microsoft commerce and
must NOT be submitted for this model.

Configuration verification on 2026-09-21: all 334 unit tests passed, including
both supported checkout paths and rejected malformed/unsafe URLs
(`artifacts/lemon-tests/lemon-checkout.trx`). The MSBuild
`RequireCommerceConfiguration` initially accepted the supplied identifiers and URL.
After Test mode was confirmed, an explicit publication guard was added.
These earlier checks verified local configuration only. Subsequent Test mode
checkout and real API results are recorded in [the test report](LEMON_SQUEEZY_TEST_REPORT.md).

## Responsibility boundary — owner direction, 2026-09-21

Lemon Squeezy manages the commercial operation. HYPNIX development owns a correctly
working application and its integration with the provider's licensing API.

| Area | Owner | HYPNIX engineering scope |
| --- | --- | --- |
| Checkout, purchases, payment processing, receipts, taxes, refunds and chargebacks | Lemon Squeezy | Open the configured hosted checkout; keep payment processing outside the app. |
| Licence issuance and provider-side licence status/activation allowance | Lemon Squeezy | Consume the License API and correctly handle its results. |
| Trial, activation UI, licence persistence, validation, deactivation and offline behavior | HYPNIX | Implement and test the application's complete licence lifecycle. |
| Wallpapers, real-time audio reaction, monitors, settings, stability and installation | HYPNIX | Ensure the actual application works correctly across supported scenarios. |

Checkout correctness, receipt delivery and the provider's financial operations
are not HYPNIX engineering acceptance tests. Do not add a payment backend, receipt
service, tax logic, refund processing or financial webhooks to this project's
backlog. Price and provider account/product settings are external configuration
inputs, not application features to implement.

The boundary still includes a working checkout link and the app's response to
valid, invalid, expired or revoked licences, activation-limit errors, network
failures and recovery. A provider-returned invalid licence must be handled
correctly regardless of the commercial reason. Opening or returning from checkout
never proves entitlement. The app does not decide whether a refund occurred or
how Lemon Squeezy translates a commercial event into licence status.

## Provider product configuration reference

These fields and messages record the provider setup and proposed product copy.
They are not a HYPNIX-owned checkout, billing or receipt implementation.

| Field | Value |
| --- | --- |
| Name | HYPNIX — Animated Wallpapers for Windows |
| Description | Bring your desktop to life with animated wallpapers, audio-reactive effects and independent backgrounds for each monitor. One-time purchase. |
| Pricing | Single payment |
| Price | Test checkout shows EUR 3.99; EUR 5.00 was the initial proposal. Final offer is provider-side configuration supplied for launch. |
| Generate license keys | Enabled |
| License length | Unlimited |
| Activation limit | Five activations, confirmed for the published Live product on 2026-09-23 |
| Additional variants | None required |
| Storefront visibility | Keep hidden until launch; this is not a substitute for test mode |
| Support | hello@mablook.com |

Keep test purchases in Lemon Squeezy Test mode. A hidden live product can still
be purchased via its checkout link. Product IDs for test and live configurations
must be kept separate. Confirm final IDs from the relevant mode, not from memory.

Confirmation modal title: **Thank you for purchasing HYPNIX!**

Confirmation message:

> Thank you for purchasing HYPNIX. Install HYPNIX from the Microsoft Store, then
> open App settings → Your license and enter the license key shown in your receipt.
> Keep your key private. For help, contact hello@mablook.com.

Observed confirmation button: **View Order**. Proposed launch button: **Install HYPNIX**. Add the verified public Microsoft Store
listing URL when it is available; do not direct customers to an unpublished app.

Email receipt note:

> Thank you for purchasing HYPNIX! Your license key is included in this receipt.
> Install HYPNIX from the Microsoft Store, then open App settings → Your license
> and enter your key to activate. Keep your key private. Need help? Contact
> hello@mablook.com.

Receipt button: **View order and license key**.
Receipt link: <https://app.lemonsqueezy.com/my-orders>.

## Configuration and runtime

Fill `packaging/Commerce.props` with the public Store ID, Product ID, Variant ID
and hosted HTTPS `/buy/` or `/checkout/buy/` checkout URL. They are embedded in the assembly. No
merchant API key belongs in source, build output, environment overrides in the
installed app, URLs, or user settings. `dotnet publish` and packaging scripts
that call it fail while required configuration is missing, malformed, or
`LemonSqueezyMode` is not `Live`. Current mode is `Live`.

All production channels (MSIX, EXE/MSI, portable) now use the same Lemon Squeezy
provider. A free Store acquisition does not unlock paid features. The legacy
Microsoft provider remains for its regression tests and is not selected by the app.

The app starts a 15-day local trial at first licensing initialization, with no
card and no automatic charge. Expiration stops playback and keeps preferences.
The Lemon Squeezy subscription-trial setting is not used for this one-time product.

Buying opens the external checkout after an explicit click. The app identifies
Lemon Squeezy and never collects payment details. Returning from checkout does
not grant access. Customers enter the key from the receipt; the public License API
validates product identity before activation and validates the exact saved instance.

Keys, instance IDs, installation ID and time checkpoints are stored using Windows
DPAPI for the current user in `%LocalAppData%/HYPNIX/licensing/license-479529-1382043-2159220.dat`.
On first use, the legacy `license.dat` supplies trial/install history; the scoped
file accepts only its matching product entitlement and leaves the legacy file intact.
No customer name/email is retained from API responses. The instance name uses a
random installation ID, not the Windows username, computer name or hardware serial.
License request failures log only exception type, never response bodies or keys.

Confirmed licenses have a **7-day offline allowance**, capped by any actual key
expiration. Foreground, startup and periodic checks renew it after successful
validation. Explicit invalidation immediately clears it. Deactivate this PC releases
the server instance and clears the local key without restarting the original trial.
Windows profiles are separate activations; multiple monitors are not.

Security limits: a desktop local trial is not server-authoritative. Removing all
local state, restoring backups, using another Windows profile, or patching the
binary can bypass local controls. DPAPI protects at-rest data but is not an
anti-tampering boundary against the same Windows user. Strong account-wide trials
would require a backend/account service. Do not claim this implementation prevents
trial resets or license sharing absolutely. No such server was deployed.

Activation timeout after the provider creates an instance, or local storage failure,
can leave an orphaned device slot; support must be able to remove it. Uninstalling
does not make an online deactivation request. Recommend deactivation before moving
to a different PC or uninstalling. Corrupt state fails closed instead of silently
starting a new trial; support recovery is required.

## Partner Center and launch checklist

- Keep the existing identity `Mablook.HYPNIX` and Store ID `9MTRP976K91M`.
- Configure free acquisition in the Store, with no paid base-app license or
  Microsoft-managed trial. Describe the 15-day in-app trial and external one-time
  purchase, price range, activation limit and periodic connectivity requirement.
- Declare third-party commerce during submission. Verify the applicable account
  type and business verification requirements; previous advice that an individual
  account always suffices must not be treated as release clearance.
- Provide reviewers a dedicated working license and activation instructions through
  certification notes, not the public description or repository.
- Publish English privacy/terms pages with the Lemon Squeezy data flow. The prior
  Microsoft-only policy text is no longer accurate. Draft amendments are in
  `LEMON_SQUEEZY_POLICY_UPDATES.md`; publication/adoption remains pending.
- Reconcile the repository's current proprietary LICENSE with customer usage rights
  before release. This integration does not silently change the legal grant.
- Live configuration and checkout were verified in the final 1.1.1 MSIX. The
  packaged provider accepted the issued Live HYPNIX license on 2026-09-23.
  Provider-side financial operations follow the responsibility boundary above.
- Verify application behavior for activation-limit responses, invalid/disabled
  licences, API failures, restart/offline use and recovery. Use controlled licence
  fixtures and provider-issued keys; payment processing and receipt delivery are
  outside this acceptance scope. Unit/UI fixtures and both historical Test-mode
  and final-payload Live activation/restoration/deactivation checks passed.
- The Live MSIX is built; test actual Store acquisition, re-run installed package checks/WACK
  and certification on that exact package. Earlier WACK results are historical.

## Historical local verification — 2026-09-21

Verified on 2026-09-21:

- Release build: zero warnings/errors.
- Unit regression suite: **334 passed**, including checkout URL regressions.
  Latest report: `artifacts/lemon-tests/lemon-testmode.trx`.
- WPF Lemon Squeezy flow: passed, latest screenshots in `artifacts/lemon-testmode-ui-20260921`.
- Test purchase key against the actual License API: passed; evidence in
  `artifacts/lemon-remote-20260921-user.log` and the committed test report.
- Legacy Microsoft-provider UI regressions: passed in
  `artifacts/lemon-store-regression.log` (compatibility only, not live Store commerce).
- Settings/library responsive UI regressions: passed in
  `artifacts/lemon-layout-regression.log`. These fixtures now explicitly inject a
  test licence rather than depending on unrestricted unpackaged production builds.
- Publication with missing configuration: blocked as intended. A non-Lemon checkout
  URL also fails the publication validation target.

At the time of the 2026-09-21 checks, no new distributable had been produced. The previous MSIX remained unchanged and
did not contain this integration. Live configuration and final-package licensing
validation were pending then; the 2026-09-23 build and Live follow-up above supersede that status.

`dotnet test Tests/Hypnix.Tests/Hypnix.Tests.csproj -c Release` exercises the actual
provider through an injected HTTP handler: trial restart/expiry, identity and
instance checks, activation limit, cache deadline, revocation, offline restart,
clock rollback, deactivation and DPAPI persistence. No paid licenses are consumed.

Build `Tests/Hypnix.NativeSmoke/Hypnix.NativeSmoke.csproj`, then run its executable
from the repository with `artifacts/lemon-e2e --lemon-license`. This drives the WPF
purchase/activation controls, real service/client and encrypted isolated state,
with simulated HTTP and desktop sessions. Screenshots are saved beside the report.
It is not a live-payment or installed-MSIX test.

## Primary references

- [Microsoft Store policies, section 10.8](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies#108-financial-transactions)
- [License API](https://docs.lemonsqueezy.com/api/license-api)
- [Product identity and instance validation](https://docs.lemonsqueezy.com/guides/tutorials/license-keys)
- [License generation and limits](https://docs.lemonsqueezy.com/help/licensing/generating-license-keys)
- [Lemon Squeezy subscription trials](https://docs.lemonsqueezy.com/help/products/free-trials)
