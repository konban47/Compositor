# Compositor for Windows

源自 [Robbie Tilton 的 Compositor（macOS 原项目）](https://github.com/robbietilton/Compositor)。感谢 Robbie Tilton / Wonder Assembly 以 MIT 许可证开源，也感谢 [chenguisen 的 Windows 移植](https://github.com/chenguisen/Compositor/tree/compositor_win)为本项目提供基础。

本仓库维护 **Windows 10/11 x64 图像编辑器**，提供简体中文和英文界面，采用熟悉的图层、蒙版、选区和滤镜工作流。Windows 源码位于 `windows/`，默认分支为 `windows-port`。

[下载 Windows 安装包和免安装版](https://github.com/konban47/Compositor/releases/tag/windows-v1.4.9.4) · [使用与构建说明](windows/README.md) · [更新记录](windows/UPDATES-1.4.9.4.md) · [许可证与来源](windows/THIRD-PARTY-NOTICES.md)

## 安装

目标系统：Windows 10 1809 及以上、Windows 11，64 位 x64。

- **安装版**：运行 `Compositor-Windows-1.4.9.4-x64-setup.exe`，按中文或英文向导安装。
- **免安装版**：完整解压 `Compositor-Windows-1.4.9.4-x64-portable.zip`，运行 `Compositor.exe`。
- 两种发行包均自带 .NET 运行时。免安装版如缺少 Visual C++ x64 运行库，可运行随包 `redist/vc_redist.x64.exe`。
- 中文系统默认使用简体中文。通过“帮助 → 语言”切换，下次启动生效。

## 常用操作

| 操作 | 使用方式 |
| --- | --- |
| 导入图像 | 拖动桌面／资源管理器文件到画布，或“文件 → 导入图像” |
| 置入图片 | 拖入画布后在顶部或右键调整变换，Enter 置入、Esc 取消；支持透视／扭曲和拆分变形 |
| 跨文档复制图层 | 无像素选区时 Ctrl+C/X，切换标签后 Ctrl+V；保留组、蒙版、样式和可编辑文字／路径 |
| 新增修饰与钢笔工具 | S/Y/E/G/O/P/T 切换对应工具组；详见[工具手册](windows/EDITING-TOOLS.md) |
| 新增选区、辅助与修复工具 | L/W/C/I/J/B 切换对应组，K 图框；见[工具使用说明](windows/PROFESSIONAL-TOOLS.md) |
| 取样与画笔光标 | 仿制图章／修复画笔 Alt 取样时显示靶心；画笔圆圈随大小和缩放变化，文字使用 I 形光标 |
| 移动对象 | 按 V，选择图层或单击对象后拖动；支持多选图层变换 |
| 平移画布 | 按 H，或按住空格，或按住鼠标中键拖动 |
| 以鼠标为中心缩放 | 滚动鼠标滚轮；Ctrl＋中键上下拖动也可缩放 |
| 锁定／解锁图层 | 右键“锁定图层”或 Ctrl+\；点击行末锁图标解锁 |
| 图层样式 | 图层右键“混合选项”、双击缩略图或面板底部 fx；支持预览、预设和重复效果 |
| 智能对象／画框／画板 | 图层右键菜单；智能对象打开内容后 Ctrl+S 回写，保存父项目保留修改 |
| 合并可见／快速导出图层 | Ctrl+Shift+E 合并可见；Ctrl+Shift+' 导出选中图层 PNG |
| 新建组 | Ctrl/Shift 多选图层后右键“新建组”或 Ctrl+G；Ctrl+Shift+G 取消编组 |
| 查看与编辑通道 | 右侧“通道”标签；Ctrl+2 RGB、Ctrl+3 红、Ctrl+4 绿、Ctrl+5 蓝 |
| 导出为 | Ctrl+Alt+Shift+W，选择 PNG/JPEG/PDF、尺寸、透明度及背景色 |
| 导航器 | “视图 → 导航器”，任意缩放比例下显示；单击／拖动缩略图定位 |
| 属性面板 | 右上“属性”或“视图 → 属性”；随图层／蒙版切换；输入数值后按 Enter 或移开焦点应用 |
| 蒙版链接与删除 | 单击图像与蒙版之间的链条；断开后选中蒙版可独立移动。选中蒙版后点垃圾桶，仅确认删除蒙版 |
| 历史记录 | 右上“历史记录”或“视图 → 历史记录”；点击状态回退，创建快照或从状态新建文档 |
| 参考线 | 从标尺拉出，贯穿整个编辑工作区；“视图 → 标尺”切换标尺 |
| 显示／隐藏图层 | 单击图层名称左侧的眼睛；睁眼显示，闭眼隐藏 |
| 调整图层上下顺序 | 按住图层名称上下拖动，按插入线放置 |
| 建立选区 | 使用矩形／椭圆选框、套索、魔棒；Shift 添加，Alt 减去 |
| 清除局部内容 | 建立选区后按 Delete 或 Backspace，清除当前图层选区内像素 |
| 局部修饰 | 先建立选区，再使用液化、涂抹、污点修复、仿制图章或画笔 |
| 重复上次滤镜 | Ctrl+Alt+F；使用上次应用的参数，可撤销 |
| 搜索命令 | Ctrl+F，可搜索中文或英文名称 |
| 带工具栏全屏 | F 进入／退出，保留工具栏、选项栏、图层与属性面板；Esc 返回 |
| 仅显示画布 | Shift+F 进入／退出，Esc 返回 |
| 自定义工具栏 | “工具 → 自定义工具栏”；拖动工具分组、排序或放入附加工具，支持存储／载入预设 |
| 旋转视图／缩放 | R 拖动旋转视图，双击复位；Z 单击放大，Alt 单击缩小 |
| 形状与路径 | U／Shift+U 切换七种形状，A 切换路径选择与直接选择；选项栏设置多边形／星形和自定形状 |
| 快速蒙版 | Q 进入后用灰度绘制覆盖率，再按 Q 转回选区 |
| 生成式工作区 | 工具栏底部图像图标或“工具 → 生成式工作区”；配置 API 地址、密钥和模型后生成并导入 |

`.comp` 项目是文件夹，需保留整个文件夹及其中的 `manifest.json` 和图层资源。普通图像可导出 PNG/JPEG/PDF（PDF 为合成图像页面），PSD/PSB 支持导入并显示转换报告。

## 功能

本版光标、网格、19 个新增工具及上游 UI 复用见[工具使用说明](windows/PROFESSIONAL-TOOLS.md)。置入、复制、其他工具及按钮／图标说明见[新增编辑工具手册](windows/EDITING-TOOLS.md)。工作区与 API 配置见 [工作区使用说明](windows/WORKSPACE.md)，上游对应关系与验证见 [1.4.9.4 更新说明](windows/UPDATES-1.4.9.4.md)。

- 图层缩略图、搜索和类型筛选、锁定、分组折叠、链接、不透明度与填充；24 种混合模式、蒙版、调整图层和图层样式。
- RGB 分通道显示与编辑、Alpha 通道新建／复制／重命名／删除、选区存取及灰度绘制。
- 选区及羽化、画笔／橡皮擦、仿制图章、污点修复、涂抹、液化、文字、形状、渐变和变换。
- 色阶、曲线、色相／饱和度、Camera Raw、模糊、仿色及其他滤镜。
- 离线选择主体、对象选择和移除背景，图片不上传服务器。
- 可调整高度的属性／历史记录面板；蒙版密度、羽化、反相、选择并遮住、色彩范围、矢量选区蒙版和应用蒙版。
- 多标签页、撤销／重做、历史快照、从状态新建独立文档、异步保存、未保存关闭保护和独立 Windows 更新频道。
- 中文界面使用 Photoshop 常见术语，提供中文输入法接口、随包中文字体和中文换行。

本版读取 v1–v17 项目。切片、注释、计数、颜色取样点和测量使用 **Windows 扩展格式 v17**；直排文字和开放路径描边使用 **Windows 扩展格式 v16**；新增样式实例、高级混合、颜色标签、画框／画板及内嵌智能对象使用 **Windows 扩展格式 v15**；使用高级 OpenType、小型大写／全部大写／上下标、文字方向、动态文字或矢量字形等新增文字样式，或使用任意轮廓形状时保存为 **Windows 扩展格式 v14**；使用非默认蒙版密度／羽化、矢量蒙版或 v13 文字样式时保存为 v13；仅使用锁定、链接、非默认填充或 Alpha 通道时保存为 v12，其余为 v11。旧版不接受超出其支持范围的版本；macOS 原版当前仅支持至 v11。[文件格式与兼容性](docs/project-format.md#windows-extension-version-17)

## 验证与当前状态

**1.4.9.4 Preview** 修复仿制取样反馈、画笔／文字光标、快捷键文字裁切和工作区网格，新增截图中的选区、透视裁剪、切片、图框、测量标注、修复及绘画工具。直接移植上游四个自绘图标与工具按钮参数；SF Symbols／SwiftUI 使用 Windows 矢量和控件对应实现。开始前 GitHub 与本地均为 `ef97e92`，上游仍为 **1.4.9 / `b4bfdea`**。核心测试 **1038 项**，14 组英文及 8 组中文界面检查；安装、卸载和发布程序由双 Windows Server CI 验证。[持续集成](https://github.com/konban47/Compositor/actions/workflows/windows.yml)

[图层样式与右键菜单使用说明](windows/LAYER-STYLES.md)记录每个菜单项、快捷键、格式和 Photoshop 差异。样式使用 CPU 近似渲染；遮住所有对象采用离线显著性连通区域，不是 Adobe 实例识别；智能对象保存本应用图层源，不提供 PSD 智能对象往返。

本机验证环境为 Windows 11；Windows 10 实机、真实输入法候选窗、多显示器和数位板仍需人工验收。使用 CPU/Skia 与 U²-NetP，渲染、主体识别及 RAW 显影与 Apple 原生实现存在差异；应用和安装包尚未进行商业代码签名。高级 OpenType 特性、小型大写／上下标、动态文字、东亚避头尾与标点挤压、阿拉伯语／希伯来语等中东与复杂文字塑形与从右到左排版、文字转框架与文字转矢量字形、历史记录画笔、非线性历史及矢量路径节点编辑均已提供，用法与兼容范围见下文。[完整已知差异](windows/README.md#已知差异和验收边界)

## 开发

需要 .NET 10 SDK：

```powershell
dotnet build windows/Compositor.slnx -c Release -warnaserror
dotnet test windows/tests/Compositor.Core.Tests -c Release
./windows/scripts/package.ps1 -OutputDirectory "$PWD/dist" -Installer
```

生成安装包需要 Inno Setup 6。GitHub Actions 可构建自带运行时的安装版与免安装版。

MIT 许可证，见 [LICENSE](LICENSE)。原始 macOS 源码及历史保留在仓库，macOS 的下载、介绍和贡献请访问顶部上游链接。
