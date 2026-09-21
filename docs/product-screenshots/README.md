# HYPNIX product screenshots — 2026-09-21

Original JPEG captures of the real WPF interface and native animated previews.
No generated UI, compositing or upscaling was used. The capture fixture uses an
isolated test-owned licence and settings, without payments or desktop application.
The images are still frames; the actual previews remain animated.

| File | Suggested English caption |
| --- | --- |
| [01-wallpaper-library.jpg](01-wallpaper-library.jpg) | Find your next desktop atmosphere. |
| [02-event-horizon-live-preview.jpg](02-event-horizon-live-preview.jpg) | Preview every wallpaper in motion. |
| [03-four-live-previews.jpg](03-four-live-previews.jpg) | Explore different wallpapers across multiple displays. |
| [04-customize-position-and-zoom.jpg](04-customize-position-and-zoom.jpg) | Make the composition your own. |
| [05-colors-and-effects.jpg](05-colors-and-effects.jpg) | Tune colors, glow and motion. |

Suggested cover: image 03. It shows the app's four-display simulation, not a claim
that four physical monitors were attached. One preview uses an ultrawide aspect.
Images 01–03 are 1268×1034; images 04–05 are 1040×780.

To prepare another capture, build `Tests/Hypnix.NativeSmoke` in Release and run its
executable from the repository root with an isolated output folder followed by
`--show-product-preview`. This fixture opens the real UI and renderers; capture
the window using the normal desktop capture workflow. Close the helper afterwards.
The fixture is test-only and is not packaged into the production application.
