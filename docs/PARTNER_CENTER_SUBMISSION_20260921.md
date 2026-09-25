# Partner Center submission — 2026-09-21

Historical snapshot of the user-authorized HYPNIX preparation on 2026-09-21.
The Test configuration and package status below describe that date; the
[2026-09-23 Live build record](STORE_LIVE_BUILD_20260923.md) supersedes those build
and licensing details. This is not a claim that the application has been submitted,
certified or published.

Audio behavior update, 2026-09-23: the source now supports optional **React to microphone** in Sound and
the tray, off by default and dependent on **Audio reactive**. It analyzes an available microphone locally
without saving recordings or sending audio. Missing or denied input leaves system-playback reaction working.
The listing and reviewer notes reproduced below remain the historical saved text, including their
system-playback-only claims. Before submitting a package with this feature, replace those claims with the
current behavior and update the portal's privacy text from [the current policy](PRIVACY_POLICY.md).
This repository change does not attest an update to Partner Center or a previously built package.

## Identity

User supplied these official values; manifest Name, Publisher and
PublisherDisplayName match `packaging/msix/AppxManifest.xml`.

| Field | Value |
| --- | --- |
| Name | `Mablook.HYPNIX` |
| Publisher | `CN=AE07AC42-52EF-4F23-BDC1-56F970CD3F72` |
| PublisherDisplayName | `Mablook` |
| Package family name | `Mablook.HYPNIX_pc2mes91x5ay8` |
| Package SID | `S-1-15-2-1135366869-1255718090-2809044192-983691373-1956898844-2911154978-816080795` |
| Store ID | `9MTRP976K91M` |

## Observed state

- Partner Center product was **In draft**, without a submission. Started
  **Submission 1**, ID `1152921505701944050`.
- Submission overview: [HYPNIX](https://partner.microsoft.com/en-us/dashboard/products/9MTRP976K91M/overview).
- The user confirmed the Lemon Squeezy configuration is the same as documented.
  Opening that exact checkout still showed **Test mode is currently enabled**.
  `packaging/Commerce.props` therefore remains `Test`; no release guard was bypassed.
- Existing MSIX packages predate Lemon Squeezy integration and are not the final
  package for this submission. No new MSIX has been uploaded in this preparation.

### Saved portal progress

- **Pricing and availability: Complete.** The owner explicitly confirmed
  **EUR 0 Store acquisition and paid licensing through Lemon Squeezy**. The
  saved offer uses EUR (Portugal), public discovery, all 240 markets and future
  markets, with availability as soon as released. No Microsoft-managed trial;
  the app supplies its own 15-day trial.
- **Store listings: Complete.** English (United States), description, short
  description, six features, Mablook developer attribution and two screenshots
  are saved. The copy below describes the trial and external one-time licence.
- **Additional Testing Information: saved and verified after reload.** Includes
  core review steps, trial access, external licensing, local audio processing,
  support email and the `runFullTrust` justification. No reviewer credential has
  been entered.
- **Properties: Complete.** Saved Personalization / Wallpaper + lock screens,
  secondary Entertainment, DirectX 11, Direct3D 11 compatible graphics, external
  purchases disclosure and `hello@mablook.com`. The portal accepts this raw email
  as a support contact; it rejected `mailto:`. Used the portal's supported
  **Provide privacy policy text** option with [the complete policy](PRIVACY_POLICY.md),
  based on the documented and inspected app data flows. The overview confirmed
  Complete after saving. A separately hosted policy URL is not required by this
  form when text is supplied. Disabled the game-only broadcast declaration.
- **Age ratings: In Progress.** Questionnaire preview generated; the IARC terms
  and age-of-majority declaration await the owner's explicit acceptance before
  saving. Preview includes ESRB Everyone, PEGI 3+, and Brazil DJCTQ 14, with
  In-App Purchases where applicable. These are portal results, not manually
  chosen ratings.
- **Packages: Not started.** Submit for certification remains disabled.

## Current preparation validation

- Unit regression rerun: **334 passed, 0 failed, 0 skipped**.
  Local report: `artifacts/partner-center-20260921/results/regression.trx`.
- Native harness Release build: **0 warnings, 0 errors**.
- WPF licence flow rerun: **PASS** for checkout behavior, invalid/valid key entry,
  encrypted persistence, trial expiry stopping playback, seven-day offline
  boundary, recovery, revocation, deactivation, preferences and no autoplay.
  Isolated output: `artifacts/partner-center-20260921/licence-ui/`.
- These are current source/build checks with controlled fixtures, not validation
  of a final installed MSIX or a payment operation.

## English listing copy

### Short description

Relax. Have fun. Be cool. Bring your desktop to life with visuals that react to your music.

### Description

Your music. Your mood. Your desktop.

HYPNIX brings atmosphere and personal style to your Windows desktop. Put on music
in your usual player and watch audio-reactive animations respond in real time.
Choose flowing visuals for a quiet break, enjoy the beat, or give your setup a
look that feels like you.

Explore thirteen built-in wallpapers, preview them in motion and customize the
available colors, glow, intensity and composition. Use different wallpapers and
settings on each monitor, with pause controls that fit around your other apps.

HYPNIX reacts to system playback; it does not include a music catalog or music
player. Audio analysis stays on your PC and does not use the microphone. Available
controls vary by wallpaper.

Free installation includes a 15-day in-app trial. Continued use after the trial
requires a one-time licence purchased through Lemon Squeezy. Enter your licence
key in the app to activate. Internet access is needed for activation and periodic
licence verification; a confirmed licence supports up to seven days offline,
subject to its expiration. Purchases and payment processing are managed by
Lemon Squeezy.

Relax. Have fun. Be cool.

### Features

- Real-time audio-reactive visuals for the music playing on your PC.
- Thirteen built-in wallpapers with live previews.
- Independent wallpapers and settings for each monitor.
- Customizable colors, glow, intensity and layout on supported effects.
- Local video support using user-selected FFmpeg media tools.
- Tray controls and automatic pause options for your desktop routine.

### Screenshot captions and assets

The five real [product screenshots](product-screenshots/README.md) were converted
from JPEG to PNG without resizing for the portal's accepted format. Local copies
are in `artifacts/partner-center-20260921/screenshots/`.

Two screenshots were uploaded and saved; the portal marks the listing Complete:

1. `03-four-live-previews.png`: “Explore your look with four simulated live previews.”
2. `01-wallpaper-library.png`: “Find your next desktop atmosphere among thirteen built-in wallpapers.”

The first caption explicitly identifies the four-display image as a simulation.

## Reviewer notes draft

HYPNIX is a Windows desktop wallpaper and audio visualizer application. Store
acquisition is free. It includes a 15-day local trial; paid access is activated
using a Lemon Squeezy licence. The app opens external checkout only when requested
by the user and never collects payment details. Returning from checkout does not
unlock the app; activation validates the licence through the provider API.

To review the core experience, select a built-in wallpaper, preview it, choose a
display and apply it. Play audio using a separate player to observe audio reaction;
the Audio reactive option must be enabled. Try changing the supported appearance
controls and apply different wallpapers on multiple displays when available.
Stop ends wallpaper playback. Closing the window leaves HYPNIX in the tray;
Quit HYPNIX exits completely.

The `runFullTrust` capability is required for WPF/Win32 desktop hosting, attaching
Direct3D surfaces behind desktop icons, enumerating monitors and responding to
display changes. WASAPI loopback analyzes system playback locally without opening
the microphone. Optional local video playback uses FFmpeg selected by the user;
media tools are not downloaded automatically.

The saved notes state that the complete core experience is available during the
15-day trial without payment. Provide a dedicated working reviewer licence for
paid-activation testing through the private **Credentials** section, not the
description or this repository. Final reviewer access and the required IARC
declaration remain pending. Privacy text is saved in Properties.

## Remaining work

- Properties and its privacy text are saved; no further form input is pending there.
- Record the owner's explicit acceptance of the IARC terms and age-of-majority
  declaration, then save the prepared age ratings.
- Resolve the observed Test checkout and verify the application's Live licence
  configuration; this is integration configuration, not payment implementation.
- Generate the final unsigned MSIX for Store signing, validate that exact package,
  upload it, and submit for certification when the required fields are complete.

Microsoft describes package signing and the certification/publication stages in
[the MSIX certification process](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-certification-process).
