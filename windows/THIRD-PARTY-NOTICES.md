# Third-party notices and provenance

The application code and original icon are distributed under the repository's [MIT license](../LICENSE), copyright (c) 2026 Wonder Assembly LLC. This is an independently maintained Windows fork, not an official Windows release by the original author.

## Source provenance

- macOS reference: [robbietilton/Compositor](https://github.com/robbietilton/Compositor), commit `fa41b9b6693b8e1b3a01a13a9ea112edd8e008e3` (initial 1.4.6 baseline). Windows 1.4.7.1 integrates upstream through `b5f9b80`, including the monotone Camera Raw tone curves from `0b4c56c`; the original MIT notices are retained.
- Initial Windows implementation: [chenguisen/Compositor, compositor_win](https://github.com/chenguisen/Compositor/tree/compositor_win), commit `c51be1e57d699edce857115f43bbca579f18dcd4`, under the same MIT license. Its `windows/` directory was imported and extended. The original notices are retained.
- [dvdstelt/Composa](https://github.com/dvdstelt/Composa), commit `86529d3b5355d28ddc28ec3db2e4de1d4d00f63b`, was researched as an alternative. No Composa or Lolly application source was copied. The unmodified U²-NetP model was obtained from its asset copy and verified by SHA-256.

## Bundled model and font

| Asset | Upstream / license | SHA-256 |
|---|---|---|
| `models/u2netp.onnx` | [U²-Net](https://github.com/xuebinqin/U-2-Net), Apache-2.0; [ONNX distribution](https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx) | `309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8` |
| `fonts/NotoSansSC.ttf` | [Noto CJK, NotoSansSC-VF.ttf](https://github.com/notofonts/noto-cjk/blob/main/Sans/Variable/TTF/Subset/NotoSansSC-VF.ttf), SIL Open Font License 1.1 | `d68bafcb48a2707749396aa12bbbd833cb70401f3a9a689fd2902c7e0d295964` |

The model runs locally on the CPU. Images are not sent to an external inference service. The font is unmodified and does not replace any system font. Full license texts are in `licenses/` and accompany the packaged application.

## Runtime dependencies

| Component | Version | License / source |
|---|---|---|
| .NET runtime | 10.0, exact patched version chosen by SDK | MIT; [dotnet/runtime](https://github.com/dotnet/runtime). Published runtime includes its own notices. |
| Avalonia / Fluent / Headless | 12.1.3 | MIT; [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia/tree/12.1.3). `licenses/Avalonia-LICENSE.txt`. |
| Inter font | via Avalonia.Fonts.Inter 12.1.3 | SIL OFL; [rsms/inter](https://github.com/rsms/inter). |
| SkiaSharp | 4.152.1 | MIT / BSD and third-party notices; [mono/SkiaSharp](https://github.com/mono/SkiaSharp). `licenses/SkiaSharp-LICENSE.txt`. |
| ONNX Runtime | 1.30.0 | MIT; [microsoft/onnxruntime](https://github.com/microsoft/onnxruntime). Full LICENSE and ThirdPartyNotices copied from its NuGet package. |
| Magick.NET Q8 | 14.17.2 | Apache-2.0 / ImageMagick and delegate licenses; [dlemstra/Magick.NET](https://github.com/dlemstra/Magick.NET). Full package Notice.txt included as `licenses/ImageMagick-NOTICE.txt`. |
| LibTiff.NET | 2.4.660 | BSD-3-Clause; [BitMiracle/libtiff.net](https://github.com/BitMiracle/libtiff.net). |
| Svg.Skia | 5.2.3 | MIT; [wieslawsoltes/Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia). Its transitive dependencies retain their own licenses. |
| Sdcb.LibRaw | 0.21.1.7 | MIT, copyright Zhou Jie; [source commit](https://github.com/sdcb/Sdcb.LibRaw/tree/43e9c1771c2b1387c2170e084e433d8bcef51cab). |
| Native LibRaw win64 | 0.21.1 | CDDL-1.0, chosen from its dual license. Unmodified dynamically linked DLL. [Corresponding LibRaw source](https://github.com/LibRaw/LibRaw/tree/0.21.1), [binding/build source](https://github.com/sdcb/Sdcb.LibRaw). `licenses/LibRaw-CDDL-1.0.txt` and `LibRaw-COPYRIGHT.txt`. |
| Microsoft Visual C++ Redistributable | VS 2022 x64, current official build at packaging | Microsoft software license. The unmodified signed redistributable is downloaded from [Microsoft](https://aka.ms/vs/17/release/vc_redist.x64.exe); its signature is checked and its hash is recorded in each package. |

`licenses/nuget-packages.json` records the restored package versions, source URLs, license declarations and copyright metadata. Third-party licenses apply to those components independently of the application's MIT license. Build/test-only packages are not part of the application runtime. Inno Setup is a build tool; its generated installer contains its own engine and standard translated installer messages.

The installer Chinese translation is vendored unmodified from [jrsoftware/issrc, ChineseSimplified.isl](https://github.com/jrsoftware/issrc/blob/main/Files/Languages/ChineseSimplified.isl). Its original translator credits and terms are retained in the file header.
