# HYPNIX installation and updates

HYPNIX ships an installer and in-app auto-update built with [Velopack](https://velopack.io), on top of
the existing portable ZIP. Releases are hosted on **GitHub Releases**, which is the feed the updater reads.

## What the user gets

- **`HypnixWallpaper-win-Setup.exe`** — a per-user installer (no administrator rights). It installs to
  `%LocalAppData%\HypnixWallpaper`, creates Desktop and Start Menu shortcuts named **HYPNIX**, and runs
  the app.
- **Automatic updates** — on launch, an installed HYPNIX checks the GitHub Releases feed in the
  background. If a newer version is found it is downloaded silently, a tray balloon appears, and the tray
  menu shows **"Restart to update HYPNIX to <version>"**. The update is applied only when the user chooses
  it (the app then restarts into the new version). A failed or offline check is logged and ignored — it
  never disrupts a running wallpaper.

Updates are delta-based: only changed files are downloaded (a version bump patched 5 of 636 files in
testing), so upgrades are small even though the app bundles the .NET runtime.

## User data is separate from the install

The install lives in `%LocalAppData%\HypnixWallpaper`; settings, presets, imported backgrounds and logs
live in `%LocalAppData%\HYPNIX`. The two folders are intentionally distinct so that **install, update and
uninstall never touch user data**, and a portable user keeps their settings after installing. (The
Velopack package id is `HypnixWallpaper` precisely to avoid colliding with the `HYPNIX` data folder; the
visible name stays HYPNIX.)

## Build a release locally

Requires PowerShell 7 and the Velopack CLI matching the NuGet package:

```powershell
dotnet tool install -g vpk --version 1.2.0
./scripts/package-release.ps1 -Version 1.2.3
```

Output goes to `artifacts/releases`: the Setup.exe, the full and delta `.nupkg`, a portable ZIP, and
`releases.win.json` (the update feed). Pack a newer version into the same folder to produce a delta.

## Test the install -> update cycle locally (no publishing)

The updater honors the `HYPNIX_UPDATE_FEED` environment variable: point it at a local folder feed instead
of GitHub. Full local cycle:

1. `./scripts/package-release.ps1 -Version 1.0.0` and run `HypnixWallpaper-win-Setup.exe --silent`.
2. Confirm the install at `%LocalAppData%\HypnixWallpaper\current\HYPNIX.exe` and that
   `%LocalAppData%\HYPNIX` (settings) is untouched.
3. `./scripts/package-release.ps1 -Version 1.0.1` into the same `artifacts/releases` folder.
4. Launch the installed app with `HYPNIX_UPDATE_FEED` set to that folder. The log records
   `Update downloaded and ready: 1.0.1`, the tray shows the restart item, and choosing it restarts into
   1.0.1.
5. Uninstall with `%LocalAppData%\HypnixWallpaper\current\Update.exe --uninstall --silent`; user data
   remains in `%LocalAppData%\HYPNIX`.

This exact cycle (install 1.0.0 -> feed 1.0.1 -> download -> ready) was validated on Windows, including
confirming `settings.json` survives installation.

## Publish a release (GitHub Actions)

Run the **Release** workflow (`.github/workflows/release.yml`) manually with a version, or push a `v*`
tag. It publishes, packs with Velopack (downloading the previous release for a delta), and uploads to
GitHub Releases using the built-in `GITHUB_TOKEN`. Installed apps pick up the new release automatically.

Bump `<Version>` in `AnimatedWallPaper.csproj` to match the release version.

## Code signing (separate, recommended before public release)

The installer and app are **not code-signed**. Without a signature, Windows SmartScreen warns on first
run/install. Signing needs a code-signing certificate and is wired through `vpk pack --signTemplate`
(and `--azureTrustedSignFile` for Azure Trusted Signing). It does not change functionality, only the
first-run trust prompt.

## Relationship to the portable ZIP

The portable validation package (`scripts/package-portable.ps1`, see [DISTRIBUTION](DISTRIBUTION.md))
still exists for a no-install, hash-verified build. The Velopack installer is the path for end users who
want shortcuts and automatic updates. Both share the same `%LocalAppData%\HYPNIX` user data under one
Windows account, so quit one before running the other.
