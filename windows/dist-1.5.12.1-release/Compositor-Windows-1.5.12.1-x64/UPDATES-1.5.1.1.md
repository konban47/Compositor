# Windows 1.5.1.1 Preview

2026-10-13。合并上游 [f2747c42ac1a08234d3209621b9e4c85d6a619ed](https://github.com/robbietilton/Compositor/commit/f2747c42ac1a08234d3209621b9e4c85d6a619ed)，包含 1.5／1.5.1。

## 改动

- 8 位 RGB／CMYK PSD 导入保留可解码文字和矢量蒙版路径、孔洞、启用／链接状态。文字可以在画布继续输入，拖选字符并单独改变字体、字号、颜色和样式，保存重开保留范围格式。
- 蒙版缩览图与图像／文件夹缩览图有各自右键菜单，接入选区组合、显示选项、缩览图尺寸／裁切和颜色标签。“选择并遮住”位于属性侧栏，提供预览、边缘参数及四种输出。
- 文字菜单、五种文字面板、本地字体候选搜索／分类／收藏／预览、缺失字体替换、文字变形／形状／工作路径、默认样式与命名样式。
- 浅色主题立即更新图层模块与共享控件；工具栏、属性输入和标题更紧凑。对齐／分布以 084116 截图重新绘制，居中线与其他基准线完全共用 1.5 逻辑像素线宽。图像生成过程及实际控件预览见 [design/README.md](design/README.md)。
- 对应上游：ICC CMYK PSD／图像导入和配置保留、软打样／色域警告、JPEG／TIFF／PDF 印刷输出、每次导出默认 RGB、支持场景的浮点合成后屏幕抖动、渐进模糊、宽版更新通知。
- 范围样式、文字几何、矢量孔洞规则、附加标签色和 ICC 数据使用 Windows 格式 v20。中文词条、操作手册、格式说明、打包文件和 CI 检查同步更新。
- **OpenCL 迁移仍暂缓**。继续使用 Skia；没有开启 OpenCL 或 30 位显示。

## 验证与边界

Release 构建将警告视为错误；1151 项核心测试通过，新增 PSD 元数据、字符选区往返、字素删除／旋转命中、CMYK 导出／默认 RGB／格式检查、渐进模糊、浮点区域合成和蒙版净化精度测试。英文与中文 interaction、windows、inspector、type-mask、preferences、image-workspace 检查通过。CI 同步增加 type-mask 检查。

本机用户提供的 PSD 导入为 45 层，其中 15 个可编辑文字层（含空文字层）和 2 个矢量蒙版。逐层输入测试文字、保存 `.comp` 并重新载入后数量和元数据保持。私人文件及派生测试项目不上传。

实现差异在 [TYPE-AND-MASKS.md](TYPE-AND-MASKS.md)：不声称复刻 Adobe 排版／AI；缺字和复杂变形可能改变外观，同层双蒙版合成单蒙版；渐进模糊使用浮点高斯分级插值及面板坐标，未复刻上游画布控制线／边缘扩展。ICC 使用 LittleCMS／ImageMagick，与 ColorSync 的色域容差不同。复杂图层样式仍可能回退至 8 位合成，原始高位深数据保留。Windows Server CI 不能代替 Windows 10 实机、数位板和真实输入法人工验收。

Camera Raw 数据未变：上游表 SHA-256 `e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190`；Windows DEFLATE 表 SHA-256 `bc1f99628beadb07f50ddee1fc50de719b97c6e417e47d30b079532395e81034`。
