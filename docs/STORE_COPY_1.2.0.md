# HYPNIX 1.2.0 — copy ready for Microsoft Store

Prepared: 25 September 2026. Locale: English (United States), matching the existing listing.
These are local replacement texts. Partner Center was not accessed or changed.
Copy only the contents of each field, not the preparation notes or headings.
This copy supersedes the listing and reviewer drafts in `PARTNER_CENTER_SUBMISSION_20260921.md`, including their obsolete statement that HYPNIX never uses a microphone.

## Short description

Relax. Have fun. Be cool. Animated wallpapers that react to music, your microphone, or both.

## Description

Your music. Your mood. Your desktop.

HYPNIX brings atmosphere and personal style to your Windows desktop. Choose an animated wallpaper, preview it in motion and give each screen a look that feels like you. Set a calm scene for a quiet break or let audio-reactive visuals follow the sound around you.

Choose how your wallpaper reacts: system audio, microphone, or both. Use music playing in your usual player, an available microphone, or a combination of the two. Select your playback and microphone devices, follow the Windows default device, and check microphone activity with a live input-level meter.

Audio analysis stays on your PC. HYPNIX does not save recordings, upload audio or play your microphone through the speakers. Microphone use is optional; new profiles start with system audio. HYPNIX does not include a music catalog or music player.

Open Wallpaper settings to adjust supported sensitivity, intensity, colors, glow and appearance controls. Available settings vary by wallpaper. Apply different wallpapers and settings to individual monitors, pause playback when needed, and use automatic pause options around your other apps. Local video wallpapers are supported with user-selected FFmpeg media tools.

Free installation includes a 15-day in-app trial. Continued use after the trial requires a one-time licence purchased through Lemon Squeezy. There is no subscription. Enter your licence key in HYPNIX to activate. Internet access is required for activation and periodic licence verification; a confirmed licence supports up to seven days offline, subject to its expiration. Lemon Squeezy handles checkout and payment processing.

Relax. Have fun. Be cool.

## Features — one entry per line

- Animated wallpapers with live previews and audio-reactive effects.
- Choose system audio, microphone, or both as the reactive source.
- Select audio devices or follow the Windows default, with live microphone-level feedback.
- Local audio analysis without saved recordings, audio uploads or microphone playback.
- Independent wallpapers and settings for each monitor.
- Clearly accessible Wallpaper settings, with sensitivity and appearance controls on supported effects.
- Tray controls, manual pause and automatic pause options.
- Local video support with user-selected FFmpeg media tools.

## What's new in this version

Version 1.2.0 makes sound and wallpaper settings easier to control.

- Choose system audio, microphone, or both for audio reaction.
- Select playback and microphone devices, with Windows-default options and device availability feedback.
- See live microphone input levels and use a temporary microphone test without saving a recording.
- Find customization more easily with the highlighted Wallpaper settings button.
- Includes the desktop-rendering fixes for Built-in ambient and Audio Visualizer.
- Improves preview behavior to address flickering of the Windows notification bell.

## Privacy policy — replacement text

Use the complete contents of [PRIVACY_POLICY.md](PRIVACY_POLICY.md), updated 27 September 2026 to also disclose development-session diagnostics. It includes the three audio sources, saved device preferences, microphone meter/test lifecycle, local diagnostics, Lemon Squeezy licensing and support contact. Do not reuse the 23 September text.

## Support contact

hello@mablook.com

## Private certification notes

HYPNIX is a Windows desktop wallpaper and audio visualizer application. Store acquisition is free. A 15-day in-app trial provides the core experience without payment. Continued use requires a one-time Lemon Squeezy licence. External checkout opens only when the user requests it; HYPNIX does not collect card details, and returning from checkout does not unlock the app. Activation validates a licence key with the provider. Internet access is required for activation and periodic validation; confirmed access can continue offline for up to seven days, subject to licence expiration.

To review: select a built-in wallpaper, preview it, choose a display and apply it. Open Wallpaper settings to adjust the controls supported by that wallpaper. App settings > Sound offers Audio reactive and an Audio source selector with System audio, Microphone, and System audio + microphone. Play audio in a separate player to test system reaction. For microphone modes, select an available microphone and speak; the microphone level meter reports that input independently of system playback. A temporary Test is available with audio reaction off. No microphone is required for system-audio mode. Input analysis is local and transient, with no saved recordings, audio transmission, speech transcription or microphone playback.

The full-trust capability is required for WPF/Win32 desktop hosting: attaching wallpaper rendering windows behind desktop icons, enumerating monitors and responding to display changes. WASAPI loopback reads selected system playback; optional WASAPI input capture reads the user-selected microphone. Optional video playback uses user-selected FFmpeg media tools, which are not downloaded automatically. Process-launch APIs also open user-requested folders and the external checkout page.

Stop ends wallpaper playback. Closing the main window leaves HYPNIX in the tray; Quit HYPNIX exits completely. The microphone settings meter/test stops when leaving App settings or hiding/minimizing the window. Wallpaper microphone reaction may continue independently when enabled. System audio mode does not open a microphone.

Local Windows App Certification Kit validation of version 1.2.0.0 returned overall PASS, with one optional Blocked executables finding related to process-launch APIs. The signed test package and unsigned upload package have identical application payloads. Local automated regression passed 380 tests. These local checks are separate from Microsoft certification.

## Private preparation notes — do not paste into the public listing

- Upload package: `artifacts/msix/HYPNIX-1.2.0-x64.msix` (unsigned Store candidate). Use the `msix-selfsign` package only for local testing.
- Product: HYPNIX; Store ID `9MTRP976K91M`; version `1.2.0.0`, x64.
- Keep free Store acquisition and the clearly disclosed in-app trial/external purchase model. No fixed checkout price is embedded in the copy, avoiding a mismatch with currency or tax presentation.
- If paid-activation review needs a dedicated licence, supply it only through the portal's private credential field. No customer licence or invented credential is included here.
- The owner reported completion of manual tests on 25 September 2026. This records the owner's confirmation; it is not a claim that the agent repeated every hardware scenario.
- Existing screenshots may show the previous sound controls. Update those images if used to illustrate the new source selector or meter; no new screenshot assets are represented as prepared here.
- Policy-writing reference: [Microsoft support information for MSIX apps](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info). The policy describes the implementation; this document does not claim legal or Store approval.
