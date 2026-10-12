# 图标与控件设计来源

适用于 Windows 1.4.10.1。上游基线为 `f5ae5bad0b25b32dda9f254e62d2f07f59478698`，文件哈希及对应代码见 [UPSTREAM-UI.json](../UPSTREAM-UI.json)。

## 直接移植与系统资源

`UpstreamArtwork.cs` 保留上游 MIT 自绘的仿制图章、渐变、多边形套索和对象选择图标。工具按钮沿用 36×36、圆角 7、栏宽 56、间距 10 及白色低透明度选中态。`ColorPaletteControls.swift` 的重叠圆角色块、交换／重置位置与 `ProjectTabs.swift` 的胶囊尺寸、边框和填充由 Windows 布局复现。

上游其余图标是 Apple SF Symbols 调用；SwiftUI／AppKit 按钮和 Apple 系统字体不是仓库内可直接引用的 Windows 文件。此项目没有捆绑 Apple 字体或符号库。Windows 使用 Segoe UI、Microsoft YaHei UI 及随包 Noto Sans SC 回退，采用统一无衬线、灰阶文本和矢量图形。`UpstreamPaletteGlyph.cs` 的系统符号为对应绘制，不能标记为原始上游资源。

## 内置 imagegen 参考

用户要求使用内置 imagegen。2026-10-12 调用后保存 [icon-reference.png](icon-reference.png)，最终使用无纹理、深灰底的 4×4 图标表作为设计参考。工具没有模型选择参数，也未返回模型身份；不能验证它使用了 `GPT-Image-2.5` 或 `gpt-image-2.5-sunburst`。

主要提示词：

> Clean flat vector-style photo editor icons, SF Symbols-like visual weight; exactly 16 light-gray icons on solid #242424, consistent 1.6-unit strokes on a 24-unit grid, rounded ends, legible at 18px. No texture, shadows, glow, captions or transparency. Magic wand, brush, bandage, eyedropper, lasso, hand, swap arrows, zoom, blur drop, shapes, crop, text, quick mask, screens, generative image, plugin.

参考图用于设计，运行时没有裁切位图或网络依赖。`GeneratedSymbols.cs` 将其转换为可缩放矢量并校正小尺寸笔画、留白和端点：魔棒、画笔、修复、吸管、套索、抓手、移动、缩放、模糊、形状、裁剪、文字、快速蒙版、屏幕、生成式图像和插件。四个上游直接移植图标优先于这些对应图形，其余工具延续既有单色矢量体系。

`--edit-workspace-checks` 输出全部 69 个工具的图标图集，并检查工具栏底部居中、标签页胶囊和不透明度手柄边界。`check-upstream-ui.ps1` 校验上游源哈希，来源变化必须重新审查 Windows 对应实现。
