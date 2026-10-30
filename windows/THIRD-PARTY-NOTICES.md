# Third-party notices and provenance

The application code and original icon are distributed under the repository's [MIT license](../LICENSE), copyright (c) 2026 Wonder Assembly LLC. This is an independently maintained Windows fork, not an official Windows release by the original author.

## Source provenance

The 1.5.12.4 Windows source preview merges upstream macOS commit `c9c7531c620d9880e5b015fbcc57518e81483bc8` (1.5.8). Directly adapted line-stroke geometry retains the upstream MIT attribution. Windows runtime parity and deferred backends are listed in [UPDATES-1.5.12.4.md](UPDATES-1.5.12.4.md); imported Swift reference code is not a claim that every upstream feature runs on Windows.

- macOS reference: [robbietilton/Compositor](https://github.com/robbietilton/Compositor), commit `fa41b9b6693b8e1b3a01a13a9ea112edd8e008e3` (initial 1.4.6 baseline). Windows 1.4.8.1 integrates upstream through `75147a27d90c681215151a6301373dd2b07b7aa2`, including measured color tables and ported Camera Raw/Scanlines algorithms. Windows 1.4.9.1 merges upstream through `b4bfdea87f9dc0cbc9eabfa63683eb1b9c2bca60`. The original MIT notices are retained. The original CRT3/XZ table stays at `Compositor/Resources/CameraRawTables.bin`; `windows/scripts/convert-camera-raw-tables.py` losslessly repacks its quantized planes into `windows/src/Compositor.Core/Resources/CameraRawTables.deflate` for .NET. No measured values are changed.
- Initial Windows implementation: [chenguisen/Compositor, compositor_win](https://github.com/chenguisen/Compositor/tree/compositor_win), commit `c51be1e57d699edce857115f43bbca579f18dcd4`, under the same MIT license. Its `windows/` directory was imported and extended. The original notices are retained.
- [dvdstelt/Composa](https://github.com/dvdstelt/Composa), commit `86529d3b5355d28ddc28ec3db2e4de1d4d00f63b`, was researched as an alternative. No Composa or Lolly application source was copied. The unmodified U²-NetP model was obtained from its asset copy and verified by SHA-256.

## Bundled model and font

Windows 1.4.9.4 directly ports the MIT-licensed `CloneStampToolIcon`, `GradientToolIcon`, `PolygonalLassoToolIcon` and `ObjectSelectionToolIcon` drawing code from upstream `Compositor/UI/BrushControls.swift`, `GradientControls.swift` and `LassoControls.swift` into `UpstreamArtwork.cs`. Tool button dimensions, spacing, corner radius and selected-state opacities come from upstream `ContentView.swift`. Source revision and normalized SHA-256 hashes are recorded in [UPSTREAM-UI.json](UPSTREAM-UI.json) and checked by `scripts/check-upstream-ui.ps1`. The repository MIT notice applies to these ports. Other upstream symbols call Apple's SF Symbols and native SwiftUI/AppKit controls; the repository contains no corresponding Windows-loadable assets. Windows vector counterparts and new-tool icons are independently drawn in `EditorIcon.cs`; no Apple symbol-font files are bundled.

| Asset | Upstream / license | SHA-256 |
|---|---|---|
| `models/u2netp.onnx` | [U²-Net](https://github.com/xuebinqin/U-2-Net), Apache-2.0; [ONNX distribution](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx) | `309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8` |
| `fonts/NotoSansSC.ttf` | [Noto CJK, NotoSansSC-VF.ttf](https://github.com/notofonts/noto-cjk/blob/main/Sans/Variable/TTF/Subset/NotoSansSC-VF.ttf), SIL Open Font License 1.1 | `d68bafcb48a2707749396aa12bbbd833cb70401f3a9a689fd2902c7e0d295964` |

The model runs locally on the CPU. Images are not sent to an external inference service. The font is unmodified and does not replace any system font. Full license texts are in `licenses/` and accompany the packaged application.

## Unicode data

`src/Compositor.Core/Document/BidiData.g.cs` is generated from the Unicode Character Database files `DerivedBidiClass.txt`, `BidiBrackets.txt` and `BidiMirroring.txt` (Unicode 18.0.0). It holds only the bidirectional character classes, paired brackets and mirroring used by the UAX #9 implementation in `Bidi.cs`. The Unicode data files are copyright © Unicode, Inc. and distributed under the [Unicode License v3](https://www.unicode.org/license.txt), reproduced in `licenses/UNICODE-LICENSE.txt`. The generated table is unmodified data.

## Runtime dependencies

| Component | Version | License / source |
|---|---|---|
| .NET runtime | 10.0, exact patched version chosen by SDK | MIT; [dotnet/runtime](https://github.com/dotnet/runtime). Published runtime includes its own notices. |
| Avalonia / Fluent / Headless | 12.1.3 | MIT; [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/12.1.3). `licenses/Avalonia-LICENSE.txt`. |
| Inter font | via Avalonia.Fonts.Inter 12.1.3 | SIL OFL; [rsms/inter](https://github.com/rsms/inter). |
| SkiaSharp | 4.152.1 | MIT / BSD and third-party notices; [mono/SkiaSharp](https://github.com/mono/SkiaSharp). `licenses/SkiaSharp-LICENSE.txt`. |
| ONNX Runtime | 1.30.0 | MIT; [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime). Full LICENSE and ThirdPartyNotices copied from its NuGet package. |
| Magick.NET Q16-HDRI | 14.17.2 | Apache-2.0 / ImageMagick and delegate licenses; [dlemstra/Magick.NET](https://github.com/dlemstra/Magick.NET). Full package Notice.txt included as `licenses/ImageMagick-NOTICE.txt`. |
| LibTiff.NET | 2.4.660 | BSD-3-Clause; [BitMiracle/libtiff.net](https://github.com/BitMiracle/libtiff.net). |
| PDFtoImage | 5.4.0 | MIT, David Sungaila; [source commit](https://github.com/sungaila/PDFtoImage/tree/5948628cb2cf7d070c7fd28b1b4496db39e3afcd). Full `licenses/PDFtoImage-LICENSE.txt`. |
| Jint / Acornima | 4.16.4 / 1.7.0 | BSD-2-Clause, Sebastien Ros; BSD-3-Clause, Adam Simon; [sebastienros/jint](https://github.com/sebastienros/jint/tree/884bd0c4ebf7806f70041eae0370ee11a9c138bc), [adams85/acornima](https://github.com/adams85/acornima/tree/401bd62d8aeb9f7cbe7b6147937a87e0c71747a8). JavaScript interpreter for File ▸ Scripts and the Script Events Manager. `licenses/Jint-LICENSE.txt`, `licenses/Acornima-LICENSE.txt`. |
| lensfun lens database | `bbd4332a9ec566fd9aa548c9e0d8ced238c56261` (2026-09-24) | CC BY-SA 3.0, the lensfun contributors; [lensfun/lensfun data/db](https://github.com/lensfun/lensfun/tree/bbd4332a9ec566fd9aa548c9e0d8ced238c56261/data/db). The 56 XML files are copied unchanged into `lensfun/` for File ▸ Automate ▸ Lens Correction; full license in `licenses/Lensfun-database-CC-BY-SA-3.0.txt`. The lensfun libraries are not used. |
| PDFium native binaries | 152.0.7961 | PDFium BSD and bundled dependency licenses, build scripts Apache-2.0; [7961 release](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium/7961). Full release license directory copied to `licenses/PDFium/`, build license in `licenses/PDFium-build-LICENSE.txt`. |
| Svg.Skia | 5.2.3 | MIT; [wieslawsoltes/Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia). Its transitive dependencies retain their own licenses. |
| Sdcb.LibRaw | 0.21.1.7 | MIT, copyright Zhou Jie; [source commit](https://github.com/sdcb/Sdcb.LibRaw/tree/43e9c1771c2b1387c2170e084e433d8bcef51cab). |
| Native LibRaw win64 | 0.21.1 | CDDL-1.0, chosen from its dual license. Unmodified dynamically linked DLL. [Corresponding LibRaw source](https://github.com/LibRaw/LibRaw/tree/0.21.1), [binding/build source](https://github.com/sdcb/Sdcb.LibRaw). `licenses/LibRaw-CDDL-1.0.txt` and `LibRaw-COPYRIGHT.txt`. |
| System.Security.Cryptography.ProtectedData | 10.0.0 | MIT; [.NET runtime](https://github.com/dotnet/runtime). Uses Windows DPAPI for opt-in API credential storage. |
| Microsoft Visual C++ Redistributable | VS 2022 x64, current official build at packaging | Microsoft software license. The unmodified signed redistributable is downloaded from [Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe); its signature is checked and its hash is recorded in each package. |

`licenses/nuget-packages.json` records the restored package versions, source URLs, license declarations and copyright metadata. Third-party licenses apply to those components independently of the application's MIT license. Build/test-only packages are not part of the application runtime. Inno Setup is a build tool; its generated installer contains its own engine and standard translated installer messages.

The installer Chinese translation is vendored unmodified from [jrsoftware/issrc, ChineseSimplified.isl](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl). Its original translator credits and terms are retained in the file header.

Windows 1.4.10.0 merges upstream macOS 1.4.10 through `2522e3b8f8bee3c64b1ab4c474656824969393ff`. The Windows ICO embeds the upstream app icon PNG frames at 16/32/64/128/256 pixels without redrawing. High precision pixels, portable RAW controls and preview behavior are adapted to Skia/LibRaw; the upstream Metal/Core Image and newer PatchMatch kernels are retained as source, not represented as Windows binaries. See [the feature mapping](UPDATES-1.4.10.0.md).

`DeepEightBit.cs` ports the MIT-licensed `DeepPixels.c` split/noise/change/merge algorithm from upstream commit `2522e3b`, copyright 2026 Wonder Assembly LLC, adapted to float assets and bounded managed buffers.

Windows 1.4.10.1 merges upstream through `f5ae5bad0b25b32dda9f254e62d2f07f59478698`. HDR normalize/restore follows upstream `DeepPixels.c`; EXR uses the bundled ImageMagick/OpenEXR delegate. Color swatch and project-tab layout metrics follow `ColorPaletteControls.swift` and `ProjectTabs.swift`. System-symbol counterparts in `UpstreamPaletteGlyph.cs` and `GeneratedSymbols.cs` are independent vectors, not Apple font resources. The latter use a built-in imagegen design reference; actual model identity was not returned. See [design provenance](design/README.md) in the source repository and [Edit menu limits](EDIT-MENU.md). No new binary runtime dependency is added. Windows spelling uses installed OS dictionaries; that historical version required a separately installed OpenColorIO tool. Version 1.5.12.4 supersedes this with the bundled runtime below.

Windows 1.5.1.3 merges upstream through `e8c9e29701528413cc0e608d61187c1c61790add`. CMYK conversion uses the existing Magick.NET/LittleCMS dependency and its bundled SWOP/FOGRA profiles. Windows progressive blur uses the existing float Gaussian implementation. The redesigned alignment icons use native geometry guided by the user reference and imagegen explorations; no Adobe or Apple binary artwork was imported. See [current scope](TYPE-AND-MASKS.md) and [design provenance](design/README.md).

The Camera Raw section resets and shortcut editor changes follow the MIT upstream sources recorded in `UPSTREAM-UI.json`. Docking, guide cursors and compact slider geometry are native Avalonia/Skia implementations; no Adobe cursor binary or proprietary panel code is included.

Windows 1.5.1.3 adds 35 individually generated panel-icon references using built-in imagegen. `DockPanelGlyph.cs` contains manually adapted small-size vectors with theme-aware color. Photoshop informed panel semantics; no Adobe icon files, fonts or branding are bundled. Full prompts, original generated references and exported SVGs are in [panel icon provenance](design/panel-icons/README.md). The tool did not expose its model identity.

## Windows 1.5.2.0 / Camera Raw Shadows

Upstream MIT sources are merged through `43d691b67995e924965bcd520f8b300f96b7d5b8`. `CameraRawShadows.cs` ports `CameraRawShadows.swift` and `adjust_camera_raw_shadows` from commit 735c2cc. `Resources/CameraRawShadows.bin` is the unmodified upstream coefficient data (SHA-256 `76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705`). The shadow landscape tests preserve upstream synthetic fields and reference measurements. No user video, PSD or Photoshop artwork is distributed.

GIF timeline and persistence behavior are adapted from the retained upstream MIT sources listed in `UPSTREAM-UI.json`. Windows GIF decode/encode reuse the existing SkiaSharp and Magick.NET dependencies. The procedural brush tips and tree/grass/water tiles are original code assets, not Adobe ABR/PAT resources.

## Upstream 1.5.8 reference update

Reference sources were merged through `c9c7531c620d9880e5b015fbcc57518e81483bc8`. Lens-effects parameter definitions, linear-light models and aperture formulas are adapted from `Compositor/Document/LensEffects.swift` and `Compositor/Rendering/MetalBokeh.swift` under the upstream MIT license. Windows uses bounded CPU/Skia approximations documented in TOOL-OPTIONS.md. The 28 new brush presets are original procedural assets; no Kyle/Adobe brush files are bundled.

## Upstream main 1405545 ports (Windows 1.5.12.4)

Behavior from upstream commits `5de396b` (16-bit layers keep their depth through 8-bit edits), `de0a43d` (depth conversion for every selected layer), `d693a2a` (clipping for every selected layer), `0673a4a` (each layer exported as its own trimmed PNG) and `fc3c81a` (Space moves a marquee being drawn) is reimplemented in C# against the Windows document model under the upstream MIT license. `42eb9b2` needs no port: the Windows Image Size dialog has no scrubbing. Volumetric Light (`1405545`) and Rim Light are not ported.

## Color management runtime (Windows 1.5.12.4)

The ACE-compatible CMM is independently implemented from published ICC transform and black-point compensation specifications. It contains no Adobe source or binary and does not claim Adobe ACE gamut mapping or bit-exact compatibility. Little CMS remains a separately labeled ImageMagick backend; Microsoft ICM uses Windows mscms.dll. Installed third-party ICC profiles may be selected locally but are not copied into release artifacts.

The build downloads pinned official Windows x64 distributions and verifies SHA-256 in `scripts/ensure-color-runtime.ps1`: CPython 3.12.10 (PSF license, [notice](licenses/CPython-3.12.10.txt)), OpenColorIO 2.4.2 (BSD-3-Clause, [notice](licenses/OpenColorIO-2.4.2.txt)), and NumPy 2.2.6 (BSD-3-Clause plus bundled dependency notices including OpenBLAS, [notice](licenses/NumPy-2.2.6.txt)). Their original distribution metadata/license files remain in `color-runtime/`. Built-in ACES configurations are supplied by that official OpenColorIO distribution. The isolated runtime is included in portable and installer packages; no user Python or pip installation is used. See [behavior and limitations](COLOR-MANAGEMENT.md).
