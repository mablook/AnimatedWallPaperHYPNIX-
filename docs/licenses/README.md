# Third-party notice sources

These files accompany the existing dependencies. NuGet package metadata and any bundled runtime/library
notices are copied into `licenses/packages` by the packaging script. Package metadata retains the authors'
copyright declarations even where an upstream license file has older dates.

- NAudio 2.3.0: `naudio/NAudio`, commit `c89fee940ee6f8d7374d18714a6b85d8b7a18ab0`, `license.txt`.
- SharpGen.Runtime / SharpGen.Runtime.COM 2.4.2-beta: `SharpGenTools/SharpGenTools`, commit
  `6990bcafe124a4c22515ad19cee5a081da8db67b`, `LICENSE.txt`.
- Vortice.Windows 3.8.3: `amerkoleci/Vortice.Windows`, commit `9e609cb9439c9872aa1b339f177e40ec96f77239`, `LICENSE`.
- Vortice.Mathematics 2.1.0: `amerkoleci/Vortice.Mathematics`, commit `fa05ec6dcba48f3f7331791da6dc7f3d866b2ad6`, `LICENSE`.
- Effekseer: pinned source commit `11e7a852692332f4fe187cdb90af6afa12318c69`, `LICENSE` and
  `LICENSE_RUNTIME_DIRECTX`. `stb.txt` preserves the license block from `Dev/Cpp/3rdParty/stb/stb_image.h`.

All upstream repository paths above are on GitHub. The Effekseer DirectX notice also identifies DirectX Tool
Kit under Ms-PL; retain that notice with the native runtime. `MS-PL.txt` is the standard text from
`https://raw.githubusercontent.com/spdx/license-list-data/main/text/MS-PL.txt` (retrieved 2026-09-15).
Wallpaper attribution remains in the original
manifests and per-wallpaper credits. These notice files do not replace the HYPNIX proprietary license.
