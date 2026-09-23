# HYPNIX 1.1.1 — Live Lemon Squeezy Microsoft Store build

Built on 2026-09-23 for Windows x64. This supersedes the pre-Lemon 1.1.0
Store candidate. Upload the unsigned MSIX; Microsoft signs it for Store delivery.
No Partner Center upload or submission was performed in this task.

## Package

- File: `artifacts/store-live-20260923/HYPNIX-1.1.1-x64.msix`
- Package / assembly version: `1.1.1.0`; project version: `1.1.1`.
- Size: 133,899,313 bytes.
- SHA-256: `4D1BFA8540C02682C4AACEEFF4D008CDB67392BA0DBF010B9C923846B4FA9884`.
- Identity: `Mablook.HYPNIX`.
- Publisher: `CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72`.
- Microsoft Store ID: `9MTRP976K91M`.
- Self-contained .NET 8 desktop runtime; x64; Windows 10 build 19041 or newer.

## Embedded commerce configuration

`packaging/Commerce.props` is now **Live**, with public Store ID `479529`,
Product ID `1382043`, Variant ID `2159220`, and checkout:

<https://mablook.lemonsqueezy.com/checkout/buy/a117a3f9-3a8c-4c78-804c-3ea051f29a89>

The provider product is the published EUR 3.99 one-time HYPNIX license, without
expiry and with five activations. The app validates all three product identifiers
and its activation instance using the Lemon Squeezy public License API.
No merchant API key or customer license key is embedded in the package.
Store acquisition remains free; the app provides the existing 15-day trial and
opens the hosted checkout. Only a valid license activation unlocks the paid use.

## Test-to-Live migration

Previously, a saved Test activation occupied the shared `license.dat` and blocked
entry of a different Live key. The app now uses a file scoped to the configured
store/product/variant. On first load it inherits the old installation identifier,
trial start and clock history, without granting an entitlement from another product.
The encrypted legacy file and its Test activation remain unchanged. The transition
does not deactivate old provider instances or restart the trial. An existing
activation for the same product is preserved. Deactivation of the new file does
not reimport the legacy activation.

## Verification

- Complete Release unit suite: **337 passed, zero failed or skipped**, including
  three new scope migration regressions and existing activation, invalid-product,
  activation-limit, expiry, instance, restart, offline, revoke and DPAPI checks.
- WPF licensing smoke: passed with isolated encrypted state and simulated HTTP;
  checkout, valid/invalid entry, trial expiry stopping playback, seven-day offline
  grace, recovery, revocation, deactivation, preferences and no autoplay checked.
- `scripts/package-msix.ps1 -Version 1.1.1 -OutputDir artifacts/store-live-20260923`
  succeeded with the existing publication guard; no build warnings or errors.
- Final MSIX unpacked successfully with MakeAppx. Manifest identity, version,
  architecture, runtime, native bridge and notices verified. All four commerce
  values were read from the **packaged HYPNIX.dll**; the new scoped migration type
  is present in that assembly.
- **683 payload files and 4,699 SHA-256 block hashes** match the MSIX block map.
- Publication guard accepts this Live config, and rejects Test mode, zero Product
  ID and a non-Lemon checkout URL. Overrides were command-local only.

Evidence is in `artifacts/store-live-20260923/validation.json`, `tests/hypnix-live.trx`,
`unit-tests-isolated.log`, `lemon-ui.log`, `package-msix.log`, `unpack.log`,
`guard-*.log`, and `lemon-ui/`. The adjacent `verify-package.ps1` rechecks package
contents after unpacking. The `.msix.sha256` sidecar identifies the upload artifact.

An initial unit build hit the already-running HYPNIX executable in the default
output directory. Tests were rerun successfully in an isolated output directory;
the user's running app was left intact.

## Live license lifecycle follow-up — 2026-09-23

After the build checks, an authorized EUR 0 Live order issued a licence.
Issuance was confirmed in the Lemon Squeezy dashboard. No card was charged.
Receipt email delivery has not been confirmed by the user; it is not claimed as
verified. Order and licence identifiers, keys and customer details are omitted.

The real licensing and DPAPI classes extracted from the exact MSIX above passed
the Live API lifecycle using isolated state under the Just Fits audit directory:

- One activation, five validations and one deactivation; all seven responses were
  HTTP 200 with the expected success flags.
- The persisted license instance received successful server validation. Repeating
  activation reused that instance without a second activation request.
- A new process restored the DPAPI state and obtained a successful online response
  for the same instance; this check could not pass from the offline cache alone.
- Final deactivation released the instance and cleared the isolated entitlement,
  while preserving the original isolated trial start.

The package and source were unchanged by this follow-up. The MSIX SHA-256 remains
`4D1BFA8540C02682C4AACEEFF4D008CDB67392BA0DBF010B9C923846B4FA9884`.
A harness-only assembly load-context correction resolved a preparation conflict
with the packaged System.Text.Json dependency; the two failed preparations made
zero API requests. They were not failed activation attempts.

Redacted evidence:

- [Activation](D:/JustFits/artifacts/live-license-test-2026-09-23/hypnix/activate.json)
- [New-process restore](D:/JustFits/artifacts/live-license-test-2026-09-23/hypnix/restore.json)
- [Deactivation](D:/JustFits/artifacts/live-license-test-2026-09-23/hypnix/deactivate.json)
- [Lifecycle summary](D:/JustFits/artifacts/live-license-test-2026-09-23/hypnix/lifecycle-summary.json)

The package-build `validation.json` records the earlier build-stage checks. The
follow-up evidence above records the subsequent successful Live license test.

## Checks not performed

Installed-MSIX UI/lifecycle, clean-machine checks, WACK and Store certification
have not been rerun for this exact package. The Live test exercised the actual
packaged classes through an isolated harness; it did not install the MSIX or
simulate Store delivery. The lifecycle script requires an elevated clean test
session and refuses to replace or disturb an existing running HYPNIX. No Partner
Center upload or submission was performed.
