# Lemon Squeezy Test mode validation — 2026-09-21

Result: the test purchase produced a licence accepted by the application's actual
Lemon Squeezy provider. Test mode is not a live release approval.

Follow-up, 2026-09-23: the published Live configuration is now included in MSIX
1.1.1.0, and the actual packaged licensing classes passed the Live API lifecycle
with an issued EUR 0-order license. Activation, online instance validation,
DPAPI persistence, new-process restore and final deactivation all passed; the
isolated trial start was preserved. See [the Live build record](STORE_LIVE_BUILD_20260923.md).
The rest of this report retains the 2026-09-21 Test-mode evidence and its then-pending
release checks. WACK/installed-MSIX validation remains separate and pending.

## Browser checkout

- The checkout explicitly displayed **Test mode is currently enabled**.
- Product: HYPNIX — Animated Wallpapers, one-time purchase, EUR 3.99.
- Portugal billing with fictitious customer data also displayed EUR 3.99 total.
- Official insufficient-funds test card: checkout displayed the expected rejection.
- Official successful test card: confirmation displayed “Thank you for purchasing HYPNIX!”
  and the English activation instructions with hello@mablook.com.
- The owner performed both final Pay clicks after automatic approval review required handoff.
- The owner supplied the issued test key. It is not included in this report or source.
- The confirmation button currently reads **View Order**.
- Receipt email delivery and its links were not independently inspected.

## Actual License API

Used the production application client/provider against api.lemonsqueezy.com with
the issued Test mode key, Store 479529, Product 1377284, Variant 2151696.
The isolated runner never touched the installed application's licence state.

- Wrong product identity rejected before consuming an activation.
- Activation and subsequent validation returned owned access.
- Repeated activation reused the persisted instance.
- A new provider loaded DPAPI-encrypted state and validated the stored instance.
- Deactivation cleared the local key and released the instance.
- Deactivation preserved the original trial start.
- The key activated again after releasing its slot.
- Final cleanup deactivated the test instance. No test activation was left behind.

Evidence: `artifacts/lemon-remote-20260921-user.log`.
The initial sandbox attempt was blocked by socket restrictions; the successful
run used the user's Windows session and network access. The temporary plaintext
key file was removed in a finally block.

## Local regression

- 334 unit tests passed: `artifacts/lemon-tests/lemon-testmode.trx`.
- Native WPF licence regression passed with simulated API responses: key entry,
  checkout does not unlock access, trial expiry stops playback, seven-day offline
  boundary, online recovery, revocation, deactivation and preference preservation.
  UI captures: `artifacts/lemon-testmode-ui-20260921`.
- Build completed with zero warnings and zero errors.
- `RequireCommerceConfiguration` rejects publication with the confirmed Test mode
  configuration. Normal builds remain available.

## Application release checks recorded on 2026-09-21

Scope clarified by the owner on 2026-09-21: Lemon Squeezy manages purchases,
payments, receipts and the commercial operation. The historical browser observations
above remain evidence; they do not create an obligation to test or implement the
provider's financial services. See [responsibilities](LEMON_SQUEEZY.md#responsibility-boundary--owner-direction-2026-09-21).

- Verify the supplied Live product IDs and hosted checkout URL in the app, then
  set `LemonSqueezyMode` to `Live` only after confirming that configuration.
- Verify the app's handling of licence activation limits, invalid/revoked keys,
  offline use, network errors and recovery, alongside activation/deactivation.
  Use controlled fixtures and provider-issued keys; a live payment is not an
  application acceptance requirement.
- Rebuild and validate the final MSIX/installers with the live configuration;
  the older packages predate this integration.
- Complete public policies and Microsoft Store submission requirements.

## Repeating the API test

Build Hypnix.NativeSmoke, put the test receipt key in a temporary private file,
set process environment variable `HYPNIX_TEST_LICENSE_FILE` to that path, and run
the harness with an isolated output folder followed by `--lemon-remote` from the
repository root. The runner requires `LemonSqueezyMode=Test`. Remove the plaintext
file afterwards. If network cleanup fails, retain encrypted `remote-license.dat`
for recovery; do not assume the remote slot was released.

Official test procedure: https://docs.lemonsqueezy.com/help/getting-started/test-mode
