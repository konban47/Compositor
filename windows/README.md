# Compositor for Windows — 简体中文 / English

基于 [Compositor](https://github.com/robbietilton/Compositor) 的独立 Windows 移植版。Windows 源码位于 `windows/`，macOS 源码保留在原目录。本分支为 `windows-port`，Windows 版本为 **1.4.9.4 Preview**。

[下载 Windows 发行包](https://github.com/konban47/Compositor/releases) · [迁移前项目调研](RESEARCH.md) · [来源和许可证](THIRD-PARTY-NOTICES.md)

## 使用

- 目标平台：Windows 10 1809 及以上、Windows 11，**x64**。不提供 32 位或原生 ARM64 包。
- 安装版：运行 `Compositor-Windows-1.4.9.4-x64-setup.exe`。支持简体中文/英文安装向导、开始菜单、可选桌面快捷方式、卸载。安装需要管理员权限；缺失时安装微软 Visual C++ 运行库。
- 免安装版：解压整个 `*-portable.zip`，运行文件夹中的 `Compositor.exe`。自带 .NET 运行时，无需安装 .NET SDK。若系统缺少 Visual C++ 2015–2022 x64 运行库，先运行随包提供的 `redist/vc_redist.x64.exe`。
- 程序与安装包目前未做商业代码签名。只从本仓库下载；`SHA256SUMS.txt` 可用于校验文件完整性。
- 中文系统默认简体中文；其他系统默认英文。使用 **帮助 → 语言 → 简体中文 / English** 切换，下次启动生效。
- 界面中文字体随包提供，不依赖系统是否安装中文语言包。画布文字支持中文输入法预编辑、候选窗口光标位置、汉字字体回退、无空格中文段落换行。
- `.comp` 是**文件夹项目**，包含 `manifest.json` 和图层资源。打开整个项目文件夹；保存时选择 `.comp` 名称。不要只复制 `manifest.json`。
- 普通图像、PSD/PSB、相机 RAW 通过“文件 → 导入图像”打开；PSD 转换限制会在导入报告中显示。PNG/JPEG/PDF 通过“导出为”菜单输出；PDF 为单页合成图像。
- 可将项目文件夹或图像路径作为参数：`Compositor.exe "D:\图片\项目.comp"`。
- 设置保存在 `%APPDATA%\CompositorWindows`。卸载不会删除项目文件和个人设置。

## 1.4.9.4 光标、网格与工具更新

- 修复 Alt 取样反馈、画笔作用范围圆圈、文字 I 形光标、快捷键窗口文字裁切与右下角网格缺失。布局网格覆盖整个工作区，包括旋转视图。
- 新增 19 个工具：选区画笔、磁性套索、快速选择、透视裁剪、切片／切片选择、图框、颜色取样器、标尺、注释、计数、移除、修复画笔、修补、内容感知移动、红眼、铅笔、颜色替换与混合器画笔。
- 新工具接入实际画布操作、选项栏、快捷键分组、中文提示、选区限制、锁定、撤销和保存。K 改为图框，模糊工具组默认无按键，可自定义。
- 直接移植上游四个自绘图标和工具按钮参数。上游调用的 SF Symbols／SwiftUI 系统资源没有可供 Windows 直接引用的文件，采用同风格矢量对应图形，来源清单与校验随包提供。
- 读取 v1–v17；含切片、测量或标注的项目使用 v17，其余保存为最低必要格式。核心测试 1038 项、14 组英文与 8 组中文 UI 回归；Windows Server 2022／2025 CI 和安装／卸载测试。
- [本版工具与使用边界](PROFESSIONAL-TOOLS.md) · [变更和验证](UPDATES-1.4.9.4.md)。下方各版本段落为历史摘要。

## 1.4.9.3 置入、复制与工具更新

- 拖入图片后进入可取消的置入变换，顶部有九宫格参考点、X/Y、W/H、比例约束、角度与确认／取消。右键提供缩放、旋转、斜切、透视、扭曲、拆分网格变形和翻转。
- 无像素选区时 Ctrl+C/X 复制／剪切完整图层和组，切换文档后 Ctrl+V；蒙版、样式、智能对象、文字与路径保持可编辑，源标签关闭后也可粘贴。
- 新增图中工具组，包含图案图章、历史记录艺术画笔、背景／魔术橡皮擦、油漆桶、锐化、调整画笔、减淡／加深／海绵、钢笔及锚点工具、直排文字和文字蒙版。
- 全局按钮与图标参照上游使用圆角、单色矢量线条、统一选中／悬停状态。保留原面板布局与中文术语。
- 读取 v1–v16；直排文字和开放路径描边需要 v16，其余特性沿用最低必要格式。1021 项核心测试、13 组英文与 7 组中文 UI 检查。
- [置入与工具完整使用说明](EDITING-TOOLS.md) · [本版变更与验证](UPDATES-1.4.9.3.md)。

## 1.4.9.2 图层样式与菜单更新

- 新增统一图层样式窗口：10 类效果、斜面等高线与纹理、重复效果、独立效果混合模式、全局光、样式库和 JSON 预设、预览／取消／确认。高级混合包含 RGB、浅／深挖空、效果／剪贴组合、蒙版隐藏效果及可 Alt 拆分的混合颜色带。
- 右键菜单覆盖截图中的图层清理、新建／复制／删除、快速导出／导出为、合并可见／拼合、锁定／重命名／显示、组、画框、画板、智能对象、遮住所有对象、剪贴蒙版、复制 CSS／SVG 和颜色标签。
- 智能对象保存内嵌可编辑图层源，双击打开内容；Ctrl+S 更新父文档的所有关联实例，父项目仍需保存。栅格化前禁止直接绘画，可撤销；内嵌数据计入历史预算。画框裁切子图层，画板支持独立尺寸、白底和组变换。
- Ctrl+Shift+E 现在为“合并可见图层”；选中图层快速 PNG 导出为 Ctrl+Shift+'，导出图层为 Ctrl+Alt+Shift+'。原整图 PNG 导出保留菜单入口，默认不绑定快捷键。
- 保留远端 `a0014d9` 的配色、字体、图标和面板布局；变换、对齐／分布、历史记录按钮继续提供悬停说明。上游复核仍为 `b4bfdea87f9dc0cbc9eabfa63683eb1b9c2bca60`，本次没有新上游提交。
- [图层样式与菜单手册](LAYER-STYLES.md) · [本版更新与验证](UPDATES-1.4.9.2.md)。

## 1.4.9.1 工作区更新

- 开始前将 GitHub `windows-port` 快进至 `b1113f6`，保留高级 OpenType／双向排版、历史记录画笔、非线性历史和路径编辑；合并上游 1.4.9 至 `b4bfdea87f9dc0cbc9eabfa63683eb1b9c2bca60`。
- F 切换带完整工具栏的全屏；Shift+F 切换仅画布；Esc 恢复。自定义工具栏支持工具／组拖放排序、附加工具、显示开关和 JSON 预设。组内工具用右键或长按展开，提示与预设列表显示实际快捷键。
- 形状扩展为矩形、椭圆、三角形、多边形、星形、直线和自定形状；A 切换路径选择／直接选择，可移动整个轮廓或编辑节点。R 旋转视图，Z 缩放；旋转后鼠标定位、对象移动和缩放锚点保持一致。Q 提供快速蒙版。
- 变换、对齐与分布、历史记录按参考图采用图标布局，悬停显示中文说明。新增边缘／中心分布、等间距分布，以及画布／所选图层／选区基准。历史记录左栏设置画笔源，底部新建文档、快照、删除；其他操作放在面板菜单。
- 生成式工作区：配置兼容 Images API 的地址、模型与密钥，输入提示词生成；支持取消、预览、保存、导入图层或新建文档。密钥默认仅在会话中使用，可选 Windows 当前用户加密存储；项目图像不会上传。服务费用由所选服务决定。
- Camera Raw 使用上游新版 763 张测量表与 33³ 合成 LUT，补齐颜色混合器／校准／参数曲线／颜色分级与暗角更新。图片重采样会栅格化文字和形状，图层样式按缩放倍数同步缩放；裁剪／画布大小／仅分辨率变更仍保留可编辑文字与样式。
- [完整工作区使用说明](WORKSPACE.md) · [1.4.9.1 上游对照与验证](UPDATES-1.4.9.1.md)。

## 属性、蒙版与历史记录

- 参考线贯穿画布之外的整个编辑工作区；修正标尺末端数字及竖向标尺的裁切。导航器在 100% 等任意缩放比例下可见，通过“视图 → 导航器”开关。
- 右上增加“属性／历史记录”，右下保留“图层／通道”；拖动中间分隔条调整高度。属性内容可以滚动；可从“视图”菜单切换面板。
- 像素／组／多选属性：宽、高、X、Y、约束比例、旋转／重置、水平／垂直翻转；对齐与分布。自动基准下单选相对画布、多选相对所选对象；可显式选择画布、所选图层或选区。支持六种边缘／中心对齐、六种边缘／中心分布与水平／垂直等间距分布。所选图层基准分布需要三项，画布／选区基准需要两项。锁定图层继续受到保护。
- 文字属性：字体、字号（点，按文档分辨率换算）、行距／字距（像素）、十六进制颜色、仿粗体／仿斜体／下划线／删除线、左／中／右段落对齐、文字编辑、项目符号／编号和大小写转换。设置字体或颜色作用于整层，会清除对应字符运行；项目符号／编号是文字前缀。形状支持填充色、圆角和线宽；调整图层可打开对应参数。
- 快速操作：选择主体、删除背景、选择图层像素、从选区增加像素／矢量蒙版、栅格化文字／形状。未选择图层的空文档显示画布尺寸、分辨率及图像／画布大小入口。
- 图像和蒙版缩略图之间显示链条，点击切换连接；断开后链条消失，原位置仍可点击重连。选中缩略图显示边框，选择图像可独立移动图像，选择断开链接的蒙版可独立移动蒙版。组蒙版随组选中变换，单独移动子图层不会移动组蒙版。
- 蒙版属性：密度 0–100%、羽化 0–1000 文档像素、反相、启用／停用、链接、载入选区、从选区替换、应用及删除。“选择并遮住”提供密度、羽化、平滑和移动边缘，点击预览可检查结果，取消不修改文档。“色彩范围”确认后直接替换蒙版覆盖率，并恢复原选区。垃圾桶在蒙版选中时提示删除蒙版并保留图像，可撤销。
- 数值输入按 Enter 或离开输入框应用；蒙版滑块在松开鼠标／按键时应用，一次操作为一个历史步骤。
- 历史记录：点击状态回退／前进，后续状态变灰；在旧状态继续编辑会丢弃其后的重做分支。自动建立初始快照，可创建快照、恢复快照、删除状态及后续记录、删除快照、清除撤销记录、从当前状态新建独立文档。最多 100 个操作状态、20 个快照，共享 256 MiB 保留像素及智能对象源包预算，超限按旧记录顺序释放；历史与快照仅在本次会话中有效，不写入项目文件。
- [1.4.8.2 更新与验证说明](UPDATES-1.4.8.2.md)。下面保留上一版功能摘要。

## 1.4.8.1 功能基础

- 滚轮围绕鼠标位置缩放；工具栏提示包含实际快捷键，支持自定义快捷键后刷新。
- 图层面板参考 Photoshop：缩略图、眼睛、搜索／类型筛选、混合模式、不透明度、填充和四种锁定。底部按钮提供链接、图层样式、蒙版、调整图层、新建组、新建图层、删除。
- 右键“锁定图层”或 Ctrl+\；点击行末锁图标解锁。锁定组同时保护子图层。支持透明像素、图像像素、位置及全部锁定。
- Ctrl/Shift 多选后右键“新建组”或 Ctrl+G；Ctrl+Shift+G 取消编组；组可展开／折叠。链接图层同步移动和变换。
- “通道”标签：RGB、红／绿／蓝，快捷键 Ctrl+2/3/4/5；显示与编辑通道相互区分，导出始终使用完整颜色。
- Alpha 通道支持新建、复制、重命名、删除、选区存取、灰度画笔／填充／渐变／滤镜；载入选区保留 256 级覆盖率并可反选。移动、文字、形状及 Camera Raw 等图层命令需返回 RGB 通道。Alpha 滤镜在应用时更新，不提供交互预览。
- 同步上游 1.4.8：扫描线、PNG/JPEG/PDF“导出为”预览及文件大小、导航器（1.4.8.2 已取消 300% 门槛）、Ctrl＋中键锚点缩放。移植 `75147a2` 的自适应 Camera Raw 测量表、去朦胧、纹理、清晰度和锐化；增加自动白平衡和白平衡吸管。
- [1.4.8.1 更新对照](UPDATES-1.4.8.1.md)；[上一版修复](UPDATES-1.4.7.1.md)。

### 项目兼容性

读取版本 1–15。新增样式实例、高级混合、颜色标签、画框／画板及内嵌智能对象使用 Windows 扩展 v15；高级 OpenType、小型大写／全部大写／上下标、文字方向、动态文字或矢量字形等新增文字样式，以及任意轮廓形状使用 Windows 扩展 v14；非默认蒙版密度／羽化、矢量蒙版或 v13 文字样式使用 v13；仅使用锁定、链接、非默认填充或 Alpha 通道时写入 v12；其余保存为 v11。旧于相应特性的 Windows 版本会拒绝更高版本，当前 macOS 原版仅支持至 v11。清除新增字段后可恢复相应较低版本；交付合成结果可导出 PNG。详细字段见 [项目格式 v15](../docs/project-format.md#windows-extension-version-15)。

## 功能与迁移范围

保留 `.comp` 格式版本 1–11 的读写并增加 Windows 扩展 v12/v13/v14/v15、图层与组、24 种混合模式、蒙版和剪贴蒙版、调整图层、图层样式、选区、绘画/仿制/修复/涂抹/液化、可编辑文字和形状、裁剪/变换、参考线、色阶/曲线/色相饱和度、Camera Raw 调整、仿色、内容识别填充、导出以及撤销/重做。

在已有 MIT Windows 移植基础上增加：

- 离线 U²-NetP 主体识别：选择主体、单击对象选择、移除背景。移除背景生成可撤销图层蒙版；图片不上传到服务器。
- 查找命令（Ctrl+F，中英文搜索）、全屏工作区（F）、仅画布模式（Shift+F）、画布 90° 旋转、新建画布单位/背景内容。
- 修复 PSD/PSB 图层导入入口，增加 RAW 显影预览。
- 异步保存与修订号跟踪，防止保存期间的后续编辑被误标为已保存；关闭窗口和标签页检查未保存内容。
- 对齐上游 1.4.6 的颜色降噪和半透明颜色叠加规则。
- 简体中文界面及 Photoshop 风格术语：图层、图层蒙版、剪贴蒙版、矩形/椭圆选框、魔棒、对象选择、色阶、曲线等。语言资源与项目字段、快捷键标识分离，不改写已有图层名称和文字内容。
- Windows 安装包、独立更新频道、带截图及测试报告的双版本 Windows Server CI。

## 已知差异和验收边界

这是可运行的 Windows 移植版，不承诺和 macOS 或 Photoshop 的每个像素、窗口行为或性能完全一致：

- 新样式为 CPU 实现，与 Photoshop 轮廓、纹理、斜面光照并非逐像素一致；不读写 ASL 样式文件。遮住所有对象生成显著性连通区域蒙版组，接触的主体可能合为一组。智能对象保存 Compositor 图层源与显示缓存，画板共享一个文档画布；没有 Adobe 外部链接、智能滤镜或 PSD 智能对象往返。简单形状复制为原生 SVG，复杂外观／文字回退为 SVG 内嵌 PNG；CSS 使用 PNG 背景保留视觉结果。具体参数和限制见 [图层手册](LAYER-STYLES.md)。

- Windows 使用 Avalonia / Skia 的 CPU 分块合成；没有移植 Apple Metal、Core Image、Vision 的专有实现。主体识别使用 U²-NetP，结果与 Apple Vision 不同；对象选择按预测蒙版的连通区域选择，不是多实例语义分割。
- Bloom/Glow 的实现近似原视觉效果；相机 RAW 先经 LibRaw 解码为 8 位 sRGB 再进入调整面板，不是完整的传感器域 RAW 流程。Camera Raw 点颜色从前景色加入，原生 Mac 面板的直接取样交互未完全复刻。
- PSD/PSB 不是 Photoshop 的完整可编辑往返格式；不支持的项目按导入报告说明处理。可导出 PNG/JPEG/PDF，不提供 PSD 写回；PDF 不保留矢量文字和图层。
- 删除剪贴来源提供“烘焙后删除／释放链接后删除／取消”，支持撤销。图层支持拖动排序；跨项目图层拖放等交互仍与原生 Mac 不同。
- 通道限 RGB 和最多 64 个 Alpha 通道，不提供 CMYK/Lab、专色、多通道计算及完整 Photoshop 面板功能。Alpha 为全画布灰度图，单通道受 200 MP 和文档资源预算限制；导出预览同样受单表面限制，大画布仍可用分块“导出 PNG”。扫描线光晕使用 CPU 全分辨率模糊，与 macOS 缩小预览算法存在轻微差异。
- 属性面板现提供 OpenType 特性开关（连字、字距调整和 `dlig`／`onum`／`frac`／`ss01` 等特性标签）、小型大写、全部大写、上标、下标、自动／从左到右／从右到左方向、动态文字标记（`{width}`、`{date}` 等），以及“转换为框架”和“转换为矢量形状”。文字以 Skia 加 HarfBuzz 塑形，阿拉伯语、希伯来语及诸多印度语系文字获得连写、上下文形变和从右到左排序；拉丁、中文等无上下文形变的文字仍逐字排版以保留逐字颜色与字体。
- 东亚排版加入避头尾（行首禁则、行尾禁则）与最简标点挤压；`Wrap` 对 CJK 逐字断行并对拉丁按词断行。双向排版使用完整的 Unicode 双向算法（UAX #9，规则 P1–P3／X1–X9／W1–W7／N0–N2／I1–I2／L1–L4），含配对括号、NSM、孤立符与镜像，字形类别取自 Unicode 18.0.0 数据。逐行按嵌入级重排后交由 HarfBuzz 按各段方向塑形，混排阿拉伯语、希伯来语、拉丁与数字均按规范排序；仅个别含嵌套显式方向覆盖符（U+202A–U+202E）与括号的极端用例可能与参考实现差一级。
- 文字“转换为矢量形状”用字形轮廓生成标准化 SVG 路径的形状图层；“转换为框架”把点文字变为段落框。矢量蒙版从选区路径创建并按路径重绘、保存 PNG 回退。**路径选择／直接选择工具**可移动形状或矢量蒙版的整个路径、拖动节点与手柄，双击加点、Alt 单击删点；节点编辑支持 M/L/C/Q/Z 命令，弧线路径不可编辑。反相、画笔、平滑和移动边缘仍会栅格化矢量蒙版；羽化与密度保持可调元数据。
- 历史面板新增**历史记录画笔**与**非线性历史**：在面板“历史记录选项”中勾选“非线性历史”在回退后继续编辑会保留其后的重做分支；历史记录画笔从所选状态或快照取源（会话内有效），在图层像素上绘制回该状态。跨会话快照、超过 8 MP 快照缩略图仍不提供，以避免阻塞编辑。
- 字体名称跨系统不一定存在；使用回退字体后，重新编辑文字的排版可能变化。已有项目的图层 PNG 仍用于未编辑内容的显示。复杂文字塑形与字体实际包含的字形及 OpenType 特性有关。
- **实机验证环境为 Windows 11 Pro 10.0.26200。Windows 10 实机、真实输入法候选窗口、多显示器/数位板压力及超大项目仍需人工验收。** GitHub 的 Windows Server 2022/2025 测试不能替代 Windows 10 实机验收。因此首发标记为 Preview。

## 从源码构建

需要 .NET 10 SDK；发布后的程序不需要 SDK。

```powershell
dotnet build windows/Compositor.slnx -c Release -warnaserror
dotnet test windows/tests/Compositor.Core.Tests -c Release
$env:COMPOSITOR_LANGUAGE = 'zh-CN'
$env:COMPOSITOR_SETTINGS_DIR = "$PWD\test-profile"
dotnet windows/src/Compositor.Desktop/bin/Release/net10.0/Compositor.dll --windows-checks chinese.png
# 生成自带运行时的 ZIP；-Installer 还需要预装 Inno Setup 6
./windows/scripts/package.ps1 -OutputDirectory "$PWD\dist" -Installer
```

构建脚本会从微软官网下载签名有效的 C++ 运行库，**不会在构建机安装它**。GitHub Actions 使用已有 Inno Setup 编译器构建安装包，并在临时 runner 上验证安装/卸载。

核心测试当前为 **975 项**；还提供 `--clicks`、`--tabs`、`--tools`、`--shortcuts`、`--camera-raw`、`--dialogs`、`--windows-checks`、`--interaction-checks`、`--panel-checks`、`--inspector-checks`、`--workspace-checks` 窗口测试，每项参数后跟截图路径。旧窗口断言用 `COMPOSITOR_LANGUAGE=en`；新增测试覆盖 en 和 zh-CN。测试设置目录应与个人配置分开。`windows/scripts/check-docs.ps1` 检查应用／安装包／文档版本、格式字段及当前功能说明，CI 在构建前执行以减少文档漂移。

CLI 位于包内 `cli/Compositor.Cli.exe`，运行 `--help` 查看命令。

## English

An independent Windows x64 port retaining Compositor's `.comp` folder format. Based on the MIT Windows work by chenguisen, updated through upstream macOS 1.4.9 and commit b4bfdea, with local ONNX subject masks, newer editing behavior, safer saves, Windows packaging, and Simplified Chinese localization/input/fonts added. See [provenance](THIRD-PARTY-NOTICES.md).

Download the installer or extract the complete portable archive and run `Compositor.exe`. .NET is bundled; the Microsoft Visual C++ x64 runtime may be needed and is included in `redist/`. Choose Help → Language to switch English/Chinese on restart. Neither the executable nor installer is commercially code-signed.

This first release is a **Preview**: tested locally on Windows 11 and in Windows Server CI, not manually certified on Windows 10. CPU rendering and U²-NetP replace Apple's native implementations; segmentation, glow, fonts, some drag/drop interactions and RAW development are not identical. PSD conversion has reported limitations and no PSD export. Dependent clipping-layer deletion supports baking or releasing links, with undo. These differences are stated explicitly rather than hidden behind a claim of complete native parity.

The repository includes reproducible build/package scripts, core tests and headless UI checks. Windows updates use `windows-v*` releases in this fork, never the upstream macOS DMG feed.
