# HYPNIX portable Windows package

## Run the validation build

1. Extract the complete ZIP into a new folder. Keep all DLLs, `Assets`, `Shaders` and notices beside `HYPNIX.exe`.
2. Run `HYPNIX.exe` on Windows x64 with Direct3D 11 hardware. The .NET and Windows Desktop runtimes are included.
3. Choose a wallpaper and press **Start**. Closing the window keeps HYPNIX in the tray; use **Quit** to exit.

This is an unsigned portable validation build, not an installer or a Store release. Its ZIP filename identifies
the source commit and packaging time; it does not establish a new product version. `build-info.json` records
the assembly version, source state, included runtime versions, dependencies and wallpaper IDs.

Settings and the preset library still live in `%LocalAppData%\HYPNIX`. This is portable packaging, not isolated
user data: another HYPNIX build under the same Windows account shares those settings. Quit the existing build
before starting this one. Test settings migrations under a separate Windows account when needed.

Videos require separately installed `ffmpeg.exe` and `ffprobe.exe`, selected through **Media tools…**.
FFmpeg is not bundled. Local video files remain in their original locations and playback is muted.

The `HYPNIX-LICENSE.txt`, wallpaper credits and `licenses` directory retain the supplied notices. Packaging
does not change those terms or constitute a legal clearance for public/commercial release.

## Check integrity

Compare `Get-FileHash -Algorithm SHA256 <downloaded.zip>` with the adjacent `.sha256` file.
`file-manifest.json` lists the size and SHA-256 of every other file in the extracted package.
The manifest excludes itself. Neither the hashes nor the manifest are a code signature.

## Rebuild from the repository

Requires PowerShell 7 and a .NET SDK supporting the project's .NET 8 target:

```powershell
./scripts/package-portable.ps1
```

The script uses `win-x64-self-contained.pubxml`, creates a fresh timestamped output under `artifacts/distribution`,
checks required runtime files and all nine wallpaper entries, compares tracked published assets with source,
collects dependency notices, and produces a ZIP, a checksum and a file inventory. Existing packages and running
application files are left in place. NuGet access may be needed to restore runtime packs. Each build records
whether the source working tree was dirty; for a release candidate, commit the reviewed changes first.

To package an already published directory without rebuilding:

```powershell
./scripts/package-portable.ps1 -PublishedDirectory artifacts/distribution-validation/publish
```

Use this only for a publish from the current source checkout with its matching `obj/project.assets.json`.
The script checks assets and records the current commit but cannot prove the provenance of supplied binaries.

## Validation before release

- Build and run the regression tests.
- Extract the ZIP into a fresh directory and verify every file against `file-manifest.json`.
- Exercise the published application assembly and native dependencies with the native smoke harness in a
  separate copy of that directory. Keep test executables, test fixtures and captures outside the shipping ZIP.
  Copy only `Hypnix.NativeSmoke.*` from its build output into the validation copy, then overwrite its
  `.runtimeconfig.json` and `.deps.json` with copies of `HYPNIX.runtimeconfig.json` and `HYPNIX.deps.json`.
  Run `Hypnix.NativeSmoke.exe` from the repository working directory; this uses the package's application DLL,
  dependency map and bundled runtime while retaining the source `App.xaml` required by the harness.
- Run the real-desktop, audio-endpoint, video, mixed-DPI and monitor recovery matrix from the repository's
  testing guide on the extracted build. Also test on a clean Windows x64 machine without a preinstalled .NET SDK.

Passing a native smoke check on a development machine does not establish clean-machine compatibility,
visual acceptance on every GPU, signing, Store certification or completion of the live-desktop matrix.
