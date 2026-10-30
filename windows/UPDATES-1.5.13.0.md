# Windows 1.5.13.0 Preview

2026-10-31。本版补齐交接记录中“尚未完成”的各项，移植上游剩余的 Rim Light 与 Volumetric Light，并为 8 位编辑路径逐项补测 16 位保持。版本号、手册、打包脚本和安装器统一为 1.5.13.0。布局与交互参照参考截图，主题、字体、按钮沿用 Compositor，浮窗紧凑按钮样式不变。

## 上游移植

- **轮廓光（Rim Light，上游 `b9a0ed8`、`97c3b3f`）**：“滤镜 › 轮廓光…”。最多三盏灯（颜色、角度、强度、宽度、收缩，可添加／移除），“发光”把光散入主体周围并向内包裹。主体由内置 U²-NetP 识别并以导向滤波贴合边缘；杂乱背景中的发丝须比背景自身变化更突出才被照亮。上游为 Metal 着色器，Windows 以 CPU 浮点平面实现，结果不保证与上游逐像素一致。
- **体积光（Volumetric Light，上游 `1405545`）**：“滤镜 › 体积光…”。光源初始位于最大的明亮区域；颜色、雾气、大小、强度、长度、光束分散；主体与暗部在光束中投下阴影，雾气在主体前方更薄。Windows 滤镜面板为模态窗口，光源用“光源 X／Y（%）”移动，不能在画布上拖动。

## 交接记录剩余项

- **储存为 Web 所用格式**：存储为“仅限图像／HTML 和图像／仅限 HTML”，切片范围“所有切片／所有用户切片”。“输出设置…”按参考截图分 HTML、切片、背景、存储文件四页，含自定／背景图像／默认设置／XHTML 预设、上一个／下一个、载入／存储（本程序 JSON）。切片输出表格（跨行列、间距单元格、TD 宽高）或 CSS（按 ID、内联、按类），写出链接、目标、替代文本、背景色和无图像切片的文字；文件名按 Windows／Mac OS／Unix 兼容规则生成，图像可放入 `images` 文件夹。帧动画文档的 GIF 逐帧预览（第一帧／上一帧／下一帧／最后一帧），按“永远／一次／其他”循环存为动画 GIF。
- **PSD 写入**：“存储副本”可选 Photoshop（*.psd）：RGB 8／16 位，像素图层、组、名称（含 Unicode）、可见性、不透明度、混合模式、剪贴、像素蒙版及合成图像。文字、形状、智能对象、图层样式按外观写成像素，调整图层不写入（合成图包含其效果），保存后显示转换报告；32 位写为 16 位。
- **Photomerge**：“文件 › 自动 › Photomerge…”。版面自动、透视、圆柱、球面、拼贴、调整位置，带版面示意图；混合图像（曝光匹配与羽化接缝蒙版）、晕影去除、几何扭曲校正、内容识别填充透明区域；Harris 角点、方向块描述子、比值匹配与 RANSAC。
- **合并到 HDR Pro**：源文件与自动对齐后打开色调面板：模式 32／16／8 位、移去重影、局部适应／色调均化直方图／曝光度和灰度系数／高光压缩，边缘光、色调和细节、高级参数与实时预览。曝光比从照片本身估计。
- **镜头校正**：“文件 › 自动 › 镜头校正…”。按参考截图：源文件、目标文件夹与 PSD／TIFF／JPEG／PNG、匹配最佳配置文件或手选、几何扭曲、色差、晕影、自动缩放图像、边缘（透明度、边缘扩展、黑、白）。配置来自随附的 lensfun 数据库（CC BY-SA 3.0，2026-09-24 提交 `bbd4332`），不是 Adobe 镜头配置文件。
- **注释**：导入注释读取 PDF（含压缩对象流）、FDF、XFDF 的文本注释并按位置放置；导出注释可写 FDF、XFDF；PDF 演示文稿的“注释”复选框把已打开文档的注释写成 PDF 文本注释。
- **拼合所有蒙版／图层效果**：硬边组蒙版逐层乘入并保留图层结构；柔边组蒙版或组效果把组合并为一层（保留名称、不透明度、混合模式与可见性）；GIF 图层效果逐帧烘焙，超出动画帧的部分被裁切并提示。
- **JavaScript／JSX**：随附 Jint 4.16.4 解释器（BSD-2-Clause）。“文件 › 脚本 › 浏览…”运行脚本；脚本事件管理器可为事件选择 `.js`／`.jsx` 文件。提供 `app`、`app.activeDocument`（尺寸、图层、文件简介、调整大小、旋转、拼合）、图层属性与 `translate`／`remove`、`alert`、`$.writeln`；一次运行为一个撤销步骤。无文件、网络或 .NET 访问，限时 10 秒、64 MB 与递归深度；`.jsxbin`、ExtendScript 的 File／Folder／ActionDescriptor 与 `confirm` 不可用。
- **链接的智能对象与打包**：“文件 › 置入链接的智能对象…”保存源文件路径与 SHA-256 并保留内嵌副本；“图层 › 智能对象”新增更新修改的内容、更新所有修改的内容、重新链接到文件、嵌入链接的智能对象；“文件 › 打包…”把链接文件复制到所选文件夹的 `Links` 并在该处保存项目。项目格式升至 Windows v32（见 [项目格式](../docs/project-format.md#windows-extension-version-32)）。
- **8 位编辑路径**：新增逐项测试（内容识别移动与延伸、修补与仿制、红眼、仿色、油漆桶、渐变、填充、清除、移动像素、剪切、晕影、色调对比度、内容识别填充、扭曲）。测试发现复制／粘贴与“通过拷贝的图层”会把 16 位降为 8 位，已修复为保留原位深与样本。

## 验证

- Release 构建零警告、零错误；核心测试见本节末行。新增测试：`LightingFilterTests`、`EightBitEditDepthTests`、`WebPageTests`、`PsdWriterTests`、`PhotomergeTests`、`HdrMergeTests`、`PdfNotesTests`、`LayerBakingTests`、`ScriptHostTests`、`SmartObjectLinkTests`、`LensCorrectionTests`。
- 中英文界面检查（各自独立设置目录）：`--panel-checks` 增加轮廓光多灯与体积光面板；`--file-workflow-checks` 增加输出设置、Web 动画、Photomerge、HDR Pro、镜头校正、外部 JS 关联与 GIF 效果烘焙。截图已对照参考截图（输出设置、Photomerge、镜头校正）核对布局。
- 未验证：PDF 注释在 Acrobat 中的显示；PSD 在 Adobe Photoshop 中的打开（以本程序 PSD 读取器与 ImageMagick 往返验证）；Photomerge／HDR／镜头校正对真实相机照片的效果（以合成场景验证）；Windows 10、扫描仪与实体压感笔。

上游 2026-10-30 核对 main 为 `14055459feb47a2bd69ef99c1983f8155bea4e75`，所有适用于 Windows 的上游提交均已移植。完整 UI 对照基线仍为 `2d75d9172835e1dd6111cb2b659958ec33fa5abf`（`UPSTREAM-UI.json`）。独立颜色引擎不是 Adobe ACE 原引擎，未声称逐像素等价。不提供 Creative Cloud 同步与 Adobe Bridge。GitHub CI 结果以该提交的 Actions 记录为准。

沿用既有面板规范：普通窗口 32 DIP 标题栏；菜单和提示显示同一实际快捷键；跨面板交互取消滚动冲突；已有 imagegen 图标沿用原始出处记录，本次没有重新生成素材。浮窗紧凑按钮样式保持不变。

Camera Raw 参考数据未变（SHA-256）：上游表 `e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190`；Windows 转换表 `bc1f99628beadb07f50ddee1fc50de719b97c6e417e47d30b079532395e81034`；Shadows 表 `76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705`。

核心测试：**1624 项通过、2 项默认跳过**；中英文 `--file-workflow-checks`、`--interaction-checks`、`--windows-checks`、`--panel-checks`、`--selection-options-checks`、`--layer-style-checks`、`--image-workspace-checks` 通过；文档一致性检查通过。
