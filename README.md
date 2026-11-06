# Compositor for Windows

面向 Windows 10/11 x64 的图像编辑器，图层、蒙版、选区、滤镜工作流与 Photoshop 一致，界面提供简体中文和英文。

当前版本：**1.6.4.0 Preview**（2026-11-06，已合并上游 1.6.4）

[下载安装版和免安装版](https://github.com/konban47/Compositor/releases/tag/windows-v1.6.4.0) · [手册目录](windows/docs/README.md) · [操作速查](windows/docs/QUICK-REFERENCE.md) · [本版更新](windows/docs/UPDATES-1.6.4.0.md)

## 本版更新

- **文件菜单对话框按 Photoshop 重排**：批处理、创建快捷批处理、图像处理器、PDF 演示文稿、Photomerge、联系表 II、图像统计、载入图层、镜头校正、条件模式更改、限制图像、拾色器、导出为、存储为 Web 所用格式，不再需要滚动才能看全。
- **浮窗 ≡ 菜单恢复可用**，并补上图层、通道、历史记录等面板自己的命令。
- **底部停靠区**：时间轴和测量记录默认停靠在画布下方；其他面板首次打开时也按 Photoshop 的分组和位置出现。
- **左侧工具栏**在窗口变矮时先缩小按钮间距，放不下的工具收进 » 菜单，不再被窗口底边遮住。
- **窗口大小记忆**：下次启动恢复上次的大小、位置和最大化状态；安装后首次启动以最大化窗口打开。
- **上游 1.6.4**：选区“扩展／收缩／羽化”按钮和共用数值，羽化可累加，扩展和收缩保持直角，状态栏显示羽化值，色板样式和工具栏重排。

完整说明和未验证项见[更新记录](windows/docs/UPDATES-1.6.4.0.md)。

## 安装

- **安装版**：运行 `Compositor-Windows-1.6.4.0-x64-setup.exe`，按中文或英文向导安装。
- **免安装版**：完整解压 `Compositor-Windows-1.6.4.0-x64-portable.zip`，运行 `Compositor.exe`。
- 两种发行包都自带 .NET 运行时。免安装版如缺少 Visual C++ x64 运行库，可运行随包的 `redist/vc_redist.x64.exe`。
- 中文系统默认使用简体中文，可在“帮助 → 语言”切换，下次启动生效。

目标系统是 Windows 11 和 Windows 10 22H2（Windows 10 待实机验收），详见[兼容性方案](windows/docs/COMPATIBILITY.md)。应用和安装包未做商业代码签名。

## 主要功能

- **图层**：缩略图、搜索和类型筛选、分组、链接、24 种混合模式、蒙版、调整图层、滤镜图层、图层样式、智能对象、画框和画板。
- **选区**：选框、套索、魔棒、对象选择、选择主体、选择并遮住、色彩范围、快速蒙版，以及选项栏上的扩展、收缩和羽化。
- **绘制与修饰**：画笔、橡皮擦、渐变、仿制图章、修复画笔、修补、内容识别移动、红眼、涂抹、液化、历史记录画笔。
- **文字与路径**：段落和字符面板、钢笔工具组、形状和路径运算、OpenType 特性。
- **调整与滤镜**：色阶、曲线、色相／饱和度、Camera Raw、减少杂色、模糊、仿色、体积光、边缘光等。
- **颜色**：ICC 色彩管理、CMYK／Lab／灰度／索引等模式、8／16／32 位通道。
- **文件**：`.comp` 项目、PSD／PSB、RAW、PDF、GIF 动画、视频帧；批处理、图像处理器、联系表、Photomerge、HDR Pro、镜头校正、Web 导出。
- **工作区**：浮动和停靠面板、属性、历史记录与快照、自定义工具栏、工作区预设、快捷键、脚本事件和插件。

主体识别默认离线运行；云端主体识别和生成式功能由你配置服务后主动使用。

`.comp` 项目是文件夹，需要保留整个文件夹及其中的 `manifest.json` 和图层资源。本版读取支持字段范围内的 v1–v33 项目，格式说明见 [project-format.md](docs/project-format.md#windows-extension-version-33)。

## 手册

| 主题 | 手册 |
| --- | --- |
| 上手 | [操作速查](windows/docs/QUICK-REFERENCE.md) · [使用与构建说明](windows/docs/README.md) · [工作区](windows/docs/WORKSPACE.md) · [首选项](windows/docs/PREFERENCES.md) |
| 菜单 | [文件工作流](windows/docs/FILE-WORKFLOWS.md) · [编辑菜单](windows/docs/EDIT-MENU.md) · [图层菜单](windows/docs/LAYER-MENU.md) · [图像与属性](windows/docs/IMAGE-WORKSPACE.md) · [窗口菜单](windows/docs/WINDOW-MENU.md) |
| 工具 | [工具选项](windows/docs/TOOL-OPTIONS.md) · [选区、裁剪与切片](windows/docs/SELECTION-CROP-SLICE.md) · [编辑工具](windows/docs/EDITING-TOOLS.md) · [专业工具](windows/docs/PROFESSIONAL-TOOLS.md) · [取样与修复](windows/docs/UTILITY-RETOUCH.md) · [钢笔与图层](windows/docs/PEN-AND-LAYERS.md) |
| 内容 | [图层样式](windows/docs/LAYER-STYLES.md) · [文字与蒙版](windows/docs/TYPE-AND-MASKS.md) · [画笔与 GIF](windows/docs/PAINTING-AND-GIF.md) · [时间轴与图层复合](windows/docs/TIMELINE-AND-PRESETS.md) · [颜色管理](windows/docs/COLOR-MANAGEMENT.md) |
| 其他 | [插件](windows/docs/PLUGINS.md) · [性能](windows/docs/PERFORMANCE.md) · [设计记录](windows/docs/DESIGN.md) · [第三方许可](windows/docs/THIRD-PARTY-NOTICES.md) |

## 从源码构建

需要 .NET 10 SDK。生成安装包还需要 Inno Setup 6。

```powershell
dotnet build windows/Compositor.slnx -c Release -warnaserror
dotnet test windows/tests/Compositor.Core.Tests -c Release
./windows/scripts/package.ps1 -OutputDirectory "$PWD/dist" -Installer
```

Windows 源码位于 `windows/`，默认分支是 `windows-port`。GitHub Actions 会构建安装版和免安装版，每次提交的结果见 [Actions](https://github.com/konban47/Compositor/actions/workflows/windows.yml)。

## 来源与许可

本项目源自 [Robbie Tilton 的 Compositor](https://github.com/robbietilton/Compositor)（macOS 原项目），感谢 Robbie Tilton / Wonder Assembly 以 MIT 许可证开源，也感谢 [chenguisen 的 Windows 移植](https://github.com/chenguisen/Compositor)。原始 macOS 源码及历史保留在本仓库，macOS 版本的下载和介绍请访问上游。

以 MIT 许可证发布，见 [LICENSE](LICENSE)。
