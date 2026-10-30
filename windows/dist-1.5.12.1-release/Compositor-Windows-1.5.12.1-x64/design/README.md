# 图标与控件设计来源

适用于 Windows 1.5.2.0。上游基线为 `43d691b67995e924965bcd520f8b300f96b7d5b8`，文件哈希及对应代码见 [UPSTREAM-UI.json](../UPSTREAM-UI.json)。

## 直接移植与系统资源

`UpstreamArtwork.cs` 保留上游 MIT 自绘的仿制图章、渐变、多边形套索和对象选择图标。工具按钮沿用 36×36、圆角 7、栏宽 56；本版按用户要求将按钮间距由 10 缩小至 2 及白色低透明度选中态。`ColorPaletteControls.swift` 的重叠圆角色块、交换／重置位置与 `ProjectTabs.swift` 的胶囊尺寸、边框和填充由 Windows 布局复现。

上游其余图标是 Apple SF Symbols 调用；SwiftUI／AppKit 按钮和 Apple 系统字体不是仓库内可直接引用的 Windows 文件。此项目没有捆绑 Apple 字体或符号库。Windows 使用 Segoe UI、Microsoft YaHei UI 及随包 Noto Sans SC 回退，采用统一无衬线、灰阶文本和矢量图形。`UpstreamPaletteGlyph.cs` 的系统符号为对应绘制，不能标记为原始上游资源。

## 内置 imagegen 参考

用户要求使用内置 imagegen。2026-10-12 调用后保存 [icon-reference.png](icon-reference.png)，最终使用无纹理、深灰底的 4×4 图标表作为设计参考。工具没有模型选择参数，也未返回模型身份；不能验证它使用了 `GPT-Image-2.5` 或 `gpt-image-2.5-sunburst`。

主要提示词：

> Clean flat vector-style photo editor icons, SF Symbols-like visual weight; exactly 16 light-gray icons on solid #242424, consistent 1.6-unit strokes on a 24-unit grid, rounded ends, legible at 18px. No texture, shadows, glow, captions or transparency. Magic wand, brush, bandage, eyedropper, lasso, hand, swap arrows, zoom, blur drop, shapes, crop, text, quick mask, screens, generative image, plugin.

参考图用于设计，运行时没有裁切位图或网络依赖。`GeneratedSymbols.cs` 将其转换为可缩放矢量并校正小尺寸笔画、留白和端点：魔棒、画笔、修复、吸管、套索、抓手、移动、缩放、模糊、形状、裁剪、文字、快速蒙版、屏幕、生成式图像和插件。四个上游直接移植图标优先于这些对应图形，其余工具延续既有单色矢量体系。

`--edit-workspace-checks` 输出全部 69 个工具的图标图集，并检查工具栏底部居中、标签页胶囊和不透明度手柄边界。`check-upstream-ui.ps1` 校验上游源哈希，来源变化必须重新审查 Windows 对应实现。

## 对齐与分布（2026-10-13）

本轮按用户要求使用 imagegen skill 和内置 imagegen 工具，参考桌面“屏幕截图 2026-10-13 084116.png”。三轮生成图保存为 [第一稿](alignment-distribution-reference.png)、[第二稿](alignment-distribution-reference-v2.png)、[第三稿](alignment-distribution-reference-v3.png)。它们是设计参考；第三稿仍有分布对象数量偏差，最终代码以用户截图为准。

提示词要点：compact dark Photoshop-style alignment/distribution panel; fourteen flat monochrome icons; six alignment icons, six distribution icons and two equal-spacing icons; every alignment/reference line exactly 1.5 logical pixels, same color and opacity including centered axes; preserve the supplied screenshot's row order, unequal object geometry and orientation; no shadows, texture or gradients.

运行时由 `AlignmentGlyph.cs` 在 22×22 逻辑网格绘制。所有基准线共用一个 1.5 像素 Pen；分布图案为两组不同长度对象及各自的边缘／中心线，六种排列使用统一转置规则。按钮没有位图裁切或网络依赖，随主题自动变色。`--type-mask-checks` 输出实际控件的三倍分辨率图以及浅色主题、文字选区、字体候选、蒙版侧栏截图。[实际控件预览](alignment-distribution-native.png)是测试程序直接渲染的结果。

## 浮窗与面板折叠图标（1.5.2.0）

使用 imagegen skill，经内置 imagegen 为 35 个面板／工具窗口逐一生成专属参考稿。设计思路参考 Photoshop 的面板语义，延续项目单色、统一线宽和紧凑留白；不使用 Photoshop 原始图标文件，不改动左侧 69 个工具的按钮图标。完整提示词、生成稿、SVG 与深浅主题实际预览见 [面板图标设计说明](panel-icons/README.md)。

1.5.2.0 保留既有生成图标，修正按钮最小尺寸及内容居中。停靠预览采用蓝色细线和 9 像素模糊光晕，沿目标列完整高度显示；各列单独保存宽度和缩略状态。
