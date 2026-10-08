# Windows migration research — 2026-10-08

Research was performed before implementation. The upstream application uses SwiftUI/AppKit, Metal, Core Image and Apple Vision. It cannot become a Windows executable by changing an Xcode build target.

| Project | Findings | Decision |
|---|---|---|
| [chenguisen/Compositor](https://github.com/chenguisen/Compositor/tree/compositor_win) | Existing .NET 10 / Avalonia 12 / Skia Windows implementation, same `.comp` format, 848 core tests; missing Vision equivalents and distributable installer, English UI. | Reuse its MIT-licensed implementation, preserve attribution, extend and test against current upstream. |
| [dvdstelt/Composa](https://github.com/dvdstelt/Composa) | Cross-platform .NET/Avalonia editor, local AI model support. Its `.cmps` project format differs from `.comp`. | Reference alternative; switching to it would lose native project interoperability. |
| [Pinta](https://github.com/PintaProject/Pinta) | Cross-platform raster image editor. | Useful alternative, not a Compositor migration. |
| [Krita](https://invent.kde.org/graphics/krita) | Mature cross-platform painting application. | Useful alternative; different document model and workflows. |
| [PhotoDemon](https://github.com/tannerhelland/PhotoDemon) | Windows image editor. | Useful alternative; not compatible with Compositor projects. |

Upstream's [Windows discussion, issue 23](https://github.com/robbietilton/Compositor/issues/23), welcomes independent Windows work but does not promise upstream Windows maintenance. This implementation therefore uses its own Windows release tags and update channel.

The inherited Windows implementation was audited against upstream 1.4.6. Added or repaired areas include offline subject detection, object selection, background masks, command search, canvas-only mode, quarter-turn canvas rotation, latest color-denoise/overlay behavior, PSD import entry points, RAW import development, asynchronous save revision tracking, safe project replacement, dirty-close handling, Windows packaging, and Simplified Chinese UI/input/fonts.

Chinese terminology follows the corresponding Photoshop concepts, with [Adobe's tool documentation](https://helpx.adobe.com/cn/photoshop/using/tool-techniques/object-selection-tool.html) and [Select Subject documentation](https://helpx.adobe.com/cn/photoshop/desktop/make-selections/automatic-color-based-selections/detect-subject-using-select-subject.html) as naming references. This does not imply Photoshop algorithm or file-format parity.
