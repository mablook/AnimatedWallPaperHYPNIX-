# Wallpaper package format

This document defines the proposed local package and library layout for downloadable and imported wallpapers.

## Naming

- Product/UI term: **Wallpapers**.
- Storage/service term: **Library**.
- Package: one self-contained wallpaper directory.

Do not name the storage root `Animations`. The application may support video, video with audio, HTML/CSS/JavaScript, images, shaders, and built-in/native renderers.

## Storage locations

User-installed packages should not be written beside the executable. Use per-user application data:

```text
%LocalAppData%/HYPNIX/
  Library/
    packages/
      <wallpaper-id>/
    .staging/
      <download-id>/
    index.json
  logs/
  settings.json
```

Repository-owned built-ins may live under:

```text
Assets/Wallpapers/<wallpaper-id>/
```

`index.json` is an optional cache for fast UI loading. Each package manifest remains the source of truth; the index must be rebuildable by scanning `packages/`.

## Package layout

Recommended layout:

```text
<wallpaper-id>/
  wallpaper.json
  preview.jpg
  content/
    wallpaper.mp4
```

HTML package:

```text
<wallpaper-id>/
  wallpaper.json
  preview.webp
  content/
    index.html
    styles.css
    scripts.js
    assets/
```

Keeping content under `content/` makes validation, export, deletion, and future signatures easier. Metadata should use a stable root filename rather than a hidden `.metadata` folder.

## Manifest example

```json
{
  "schemaVersion": 1,
  "id": "68ae669b92d000f8170811b1",
  "title": "Example wallpaper",
  "description": null,
  "author": {
    "id": null,
    "name": null
  },
  "type": "video",
  "entrypoint": "content/wallpaper.mp4",
  "preview": "preview.jpg",
  "capabilities": {
    "hasVideo": true,
    "hasAudio": true,
    "audioReactive": false,
    "interactive": false,
    "input": [],
    "requiresNetwork": false
  },
  "media": {
    "durationSeconds": 116.6,
    "width": 1280,
    "height": 720,
    "framesPerSecond": 30,
    "videoCodec": "h264",
    "audioCodec": "aac",
    "audioChannels": 2
  },
  "playback": {
    "loop": true,
    "mutedByDefault": true,
    "fit": "cover"
  },
  "source": {
    "kind": "import",
    "catalogId": null,
    "downloadUrl": null
  },
  "createdAt": "2025-08-31T04:11:26.803148+01:00",
  "updatedAt": null
}
```

## Types

Use readable string values instead of undocumented numeric values:

- `video`: local video or animated image decoded by the media renderer.
- `web`: local HTML/CSS/JavaScript package.
- `image`: static image, useful for library consistency and transitions.
- `shader`: GPU shader package when supported.
- `native`: renderer included with the application, such as the current built-in animation.

Unknown types must be rejected as unsupported, not guessed.

## Implemented safe preset registry

HYPNIX now creates and watches `%LocalAppData%/HYPNIX/Library/packages`. Changes are debounced and registration
is reconstructed from package manifests; discovery never executes package content.

The first enabled downloaded type is `native-preset`. It selects an internal renderer through an allowlisted
`rendererId` and may contain JSON plus PNG/JPEG/WebP assets only. Current renderer IDs:

- `hypnix.visualizer.classic.v1`: available.
- `hypnix.flame-fluid.v1`: recognized and reserved, but unavailable until the Direct3D renderer ships.

```json
{
  "schemaVersion": 1,
  "id": "living-flame-neon",
  "title": "Living Flame Neon",
  "type": "native-preset",
  "rendererId": "hypnix.flame-fluid.v1",
  "entrypoint": "content/preset.json",
  "preview": "preview.webp",
  "background": "content/background.webp"
}
```

Validation enforces a conservative package ID, matching directory name, 64-file limit, 256 MB installed-size
limit, 64 KB manifest limit, local relative paths, no reparse points, an asset-extension allowlist, and a
renderer allowlist. A recognized future renderer is registered as unsupported and is never executed.

## Capabilities

Type and capabilities are separate. Examples:

- MP4 with H.264 only: `type=video`, `hasVideo=true`, `hasAudio=false`.
- MP4 with H.264 + AAC: `type=video`, `hasVideo=true`, `hasAudio=true`.
- HTML using only local CSS: `type=web`, `interactive=false`, `requiresNetwork=false`.
- HTML responding to mouse input: `type=web`, `interactive=true`.
- Remote webpage: future policy may allow `requiresNetwork=true`, but local-only should remain the safe default.

Capabilities in downloaded manifests are claims. Import validation must derive media facts from the actual files and overwrite or reject inconsistent claims.

Cross-cutting behaviors such as multi-monitor assignment, playlists, interactive input, and audio visualization are specified in [`PRODUCT_DIRECTION.md`](PRODUCT_DIRECTION.md).

## Import pipeline

1. Select or drop a local file/package.
2. Create a random directory under `.staging/`.
3. Copy content using safe generated filenames.
4. Detect the real type from content and file signatures, not extension alone.
5. For media, probe streams and derive audio/video capabilities, duration, dimensions, FPS, and codecs.
6. For web packages, resolve the entrypoint and scan all paths.
7. Generate or validate the preview.
8. Write `wallpaper.json` last.
9. Validate the complete staged package.
10. Move the directory atomically to `packages/<id>/`.
11. Rebuild/update the library index.

A failed import must delete only its own staging directory and must never leave a half-installed package in `packages/`.

## Download pipeline

Future catalog downloads should use the same staging and validation pipeline:

1. Download to `.staging/<download-id>`.
2. Enforce file-count and total-size limits while extracting.
3. Reject absolute paths, `..` traversal, symlinks/reparse points, and files outside the staging root.
4. Verify expected hashes/signatures when provided by the catalog.
5. Validate manifest and content capabilities.
6. Install with an atomic directory move.

Do not execute content directly from the download cache.

## Web package safety

Local web wallpapers require a stricter renderer policy than video:

- Block navigation outside the package by default.
- Disable network access unless `requiresNetwork=true` and the user explicitly permits it.
- Do not expose arbitrary filesystem paths.
- Do not expose process launch, shell, registry, or unrestricted native APIs.
- Use a dedicated browser data directory per package or trust group.
- Treat JavaScript and remote content as active code.

## Identifier rules

- IDs are opaque stable strings, not display names or filesystem paths.
- New local imports should use a GUID or ULID.
- Catalog IDs may be preserved if they match a conservative character/length policy.
- Never concatenate an unvalidated external ID directly into a path.
- Package directory name must exactly match the validated manifest ID.

## Example package studied

Source directory:

`D:/LiveWallpaper/68ae669b92d000f8170811b1`

Observed structure:

```text
68ae669b92d000f8170811b1/
  68ae669b92d000f8170811b1.mp4
  .metadata/
    68ae669b92d000f8170811b1.cover.jpg
    68ae669b92d000f8170811b1.meta.json
```

Observed content:

- MP4 size: 19,639,943 bytes.
- Duration: 116.6 seconds.
- Video: H.264 High, 1280×720, 30 FPS, YUV 4:2:0.
- Audio: AAC-LC, 44.1 kHz, stereo.
- Overall bitrate: approximately 1.35 Mbps.
- Preview: JPEG, 500×281.
- Existing metadata includes ID, title, cover, timestamps, numeric type, and playlist fields.

The existing package is a useful starting shape, but its numeric `type` and metadata do not declare or prove audio/video streams. Our import pipeline must inspect the real content.

## UI implications

Each library card can be built entirely from the manifest/index:

- Preview.
- Title and author.
- Type badge (`Video`, `Web`, `Native`).
- Audio badge when `hasAudio=true`.
- Network or interactive warning badges.
- Duration/resolution for media.
- Actions: Apply, Preview, Mute, Show in folder, Export, Delete.

The wallpaper detail view should show derived technical information separately from author-provided description.

## Implemented library playback and preset properties

The gallery now consumes built-in manifests with an internal `kind` field and validated user package registrations.
The experimental volumetric renderer is excluded from the production catalog even if a built-in manifest names it.
Installed packages are revalidated immediately before preparing playback.

The `native-preset` entrypoint must contain a JSON object, up to 64 KB. Supported classic-renderer properties:

```json
{
  "intensity": 1.0,
  "sensitivity": 1.0,
  "glow": 0.55,
  "colorTheme": 0
}
```

Intensity is bounded to 0.35–2.7, sensitivity to 0.4–2.5, glow to 0–1, and colorTheme to 0–3
(ice blue, sunset, emerald, monochrome). Missing properties use defaults. Saved user adjustments override the preset.
The manifest's background is loaded by the classic renderer; the preview is used by its gallery card.
An image that the installed Windows codecs cannot decode reports a preparation error and does not replace the current wallpaper.

Validation traverses incrementally, rejects reparse points before descending into directories, and counts both files
and directories against a 64-entry budget. Alternate data streams cannot be asset paths.

Local videos added through the UI currently remain references to user-selected files in settings, rather than
installed video packages. The staged/copying import pipeline above is still a future extension.
