# Changelog

All notable changes to this package are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.1.0] - 2026-10-05

### Added

- First release as a UPM package, `com.schyro.pixel-pipeline`, with the `PixelPipeline.Runtime`
  and `PixelPipeline.Editor` assemblies.
- The scene is drawn at the internal resolution instead of at screen resolution followed by a
  point downsample, which remains as the fallback for the scene view and camera stacks.
- Banded lighting on the deferred paths: every G-buffer pixel is lit with banded lights, pixel
  shadows, and zoned ambient, so URP Lit, Simple Lit, and Shader Graph Lit materials need no change.
- The demo scene as an importable **Demo** sample (`Samples~/Demo`), with its own settings asset,
  `PixelPipelineDemoSettings`.

### Removed

- `PixelPipeline/Lit`, `MAT_PixelLitDefault`, the automatic material conversion on model import
  and on adding objects to a scene, the **Convert Materials** commands, and the **Materials** page
  of the settings window. Materials that used `PixelPipeline/Lit` go back to URP Lit.
- The `Tools > Pixel Pipeline > Rebuild Demo Scene` command; the demo scene is now authored by hand.

### Changed

- Light bands need a **Deferred+** (or **Deferred**) renderer; forward renderers keep URP's smooth
  lighting.
- Grass, foliage, and grass terrain use `UniversalForwardOnly` and `DepthNormalsOnly` passes, which
  every rendering path draws.
- Shader includes resolve through `Packages/com.schyro.pixel-pipeline/`, so the package no longer
  depends on where it sits in a project.
- The settings asset stays in the host project and is not part of the package.
