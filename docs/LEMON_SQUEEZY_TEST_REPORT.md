# Lemon Squeezy Test mode validation — 2026-09-21

Result: the test purchase produced a licence accepted by the application's actual
Lemon Squeezy provider. Test mode is not a live release approval.

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

## Remaining release checks

- Decide whether EUR 3.99 or the previously proposed EUR 5.00 is the final price.
- Copy/configure the product in Live mode, verify the new IDs and checkout, then
  set `LemonSqueezyMode` to `Live` only after confirming the live configuration.
- Verify merchant approval, live purchase, receipt delivery, real device activation
  limit, refund/chargeback revocation handling and support recovery.
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
