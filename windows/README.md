# Compositor for Windows — 简体中文 / English

基于 [Compositor](https://github.com/robbietilton/Compositor) 的独立 Windows 移植版。Windows 源码位于 `windows/`，macOS 源码保留在原目录。本分支为 `windows-port`，Windows 版本为 **1.4.6.1 Preview**。

[下载 Windows 发行包](https://github.com/konban47/Compositor/releases) · [迁移前项目调研](RESEARCH.md) · [来源和许可证](THIRD-PARTY-NOTICES.md)

## 使用

- 目标平台：Windows 10 1809 及以上、Windows 11，**x64**。不提供 32 位或原生 ARM64 包。
- 安装版：运行 `Compositor-Windows-1.4.6.1-x64-setup.exe`。支持简体中文/英文安装向导、开始菜单、可选桌面快捷方式、卸载。安装需要管理员权限；缺失时安装微软 Visual C++ 运行库。
- 免安装版：解压整个 `*-portable.zip`，运行文件夹中的 `Compositor.exe`。自带 .NET 运行时，无需安装 .NET SDK。若系统缺少 Visual C++ 2015–2022 x64 运行库，先运行随包提供的 `redist/vc_redist.x64.exe`。
- 程序与安装包目前未做商业代码签名。只从本仓库下载；`SHA256SUMS.txt` 可用于校验文件完整性。
- 中文系统默认简体中文；其他系统默认英文。使用 **帮助 → 语言 → 简体中文 / English** 切换，下次启动生效。
- 界面中文字体随包提供，不依赖系统是否安装中文语言包。画布文字支持中文输入法预编辑、候选窗口光标位置、汉字字体回退、无空格中文段落换行。
- `.comp` 是**文件夹项目**，包含 `manifest.json` 和图层资源。打开整个项目文件夹；保存时选择 `.comp` 名称。不要只复制 `manifest.json`。
- 普通图像、PSD/PSB、相机 RAW 通过“文件 → 导入图像”打开；PSD 转换限制会在导入报告中显示。PNG/JPEG 通过导出菜单输出。
- 可将项目文件夹或图像路径作为参数：`Compositor.exe "D:\图片\项目.comp"`。
- 设置保存在 `%APPDATA%\CompositorWindows`。卸载不会删除项目文件和个人设置。

## 功能与迁移范围

保留 `.comp` 格式版本 1–11 的读写、图层与组、24 种混合模式、蒙版和剪贴蒙版、调整图层、图层样式、选区、绘画/仿制/修复/涂抹/液化、可编辑文字和形状、裁剪/变换、参考线、色阶/曲线/色相饱和度、Camera Raw 调整、仿色、内容识别填充、导出以及撤销/重做。

在已有 MIT Windows 移植基础上增加：

- 离线 U²-NetP 主体识别：选择主体、单击对象选择、移除背景。移除背景生成可撤销图层蒙版；图片不上传到服务器。
- 查找命令（Ctrl+F，中英文搜索）、仅画布模式（F）、画布 90° 旋转、新建画布单位/背景内容。
- 修复 PSD/PSB 图层导入入口，增加 RAW 显影预览。
- 异步保存与修订号跟踪，防止保存期间的后续编辑被误标为已保存；关闭窗口和标签页检查未保存内容。
- 对齐上游 1.4.6 的颜色降噪和半透明颜色叠加规则。
- 简体中文界面及 Photoshop 风格术语：图层、图层蒙版、剪贴蒙版、矩形/椭圆选框、魔棒、对象选择、色阶、曲线等。语言资源与项目字段、快捷键标识分离，不改写已有图层名称和文字内容。
- Windows 安装包、独立更新频道、带截图及测试报告的双版本 Windows Server CI。

## 已知差异和验收边界

这是可运行的 Windows 移植版，不承诺和 macOS 的每个像素、窗口行为或性能完全一致：

- Windows 使用 Avalonia / Skia 的 CPU 分块合成；没有移植 Apple Metal、Core Image、Vision 的专有实现。主体识别使用 U²-NetP，结果与 Apple Vision 不同；对象选择按预测蒙版的连通区域选择，不是多实例语义分割。
- Bloom/Glow 的实现近似原视觉效果；相机 RAW 先经 LibRaw 解码为 8 位 sRGB 再进入调整面板，不是完整的传感器域 RAW 流程。Camera Raw 点颜色从前景色加入，原生 Mac 面板的直接取样交互未完全复刻。
- PSD/PSB 不是 Photoshop 的完整可编辑往返格式；不支持的项目按导入报告说明处理。只能导出 PNG/JPEG，不提供 PSD 写回。
- 删除仍被其他图层作为剪贴来源的图层时，需要先释放剪贴蒙版；Mac 的“烘焙后删除”对话框尚未移植。部分图层/标签页拖放交互与原生 Mac 不同，使用菜单操作。
- 字体名称跨系统不一定存在；使用回退字体后，重新编辑文字的排版可能变化。已有项目的图层 PNG 仍用于未编辑内容的显示。阿拉伯文/印度文字的复杂塑形不在此次中文支持范围内。
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

核心测试当前为 **864 项**；还提供 `--clicks`、`--tabs`、`--tools`、`--shortcuts`、`--camera-raw`、`--dialogs`、`--windows-checks` 窗口测试，每项参数后跟截图路径。旧窗口断言用 `COMPOSITOR_LANGUAGE=en`；新增测试覆盖 en 和 zh-CN。测试设置目录应与个人配置分开。

CLI 位于包内 `cli/Compositor.Cli.exe`，运行 `--help` 查看命令。

## English

An independent Windows x64 port retaining Compositor's `.comp` folder format. Based on the MIT Windows work by chenguisen, audited against upstream macOS 1.4.6, with local ONNX subject masks, newer editing behavior, safer saves, Windows packaging, and Simplified Chinese localization/input/fonts added. See [provenance](THIRD-PARTY-NOTICES.md).

Download the installer or extract the complete portable archive and run `Compositor.exe`. .NET is bundled; the Microsoft Visual C++ x64 runtime may be needed and is included in `redist/`. Choose Help → Language to switch English/Chinese on restart. Neither the executable nor installer is commercially code-signed.

This first release is a **Preview**: tested locally on Windows 11 and in Windows Server CI, not manually certified on Windows 10. CPU rendering and U²-NetP replace Apple's native implementations; segmentation, glow, fonts, some drag/drop interactions and RAW development are not identical. PSD conversion has reported limitations and no PSD export. For dependent clipping-layer deletion, release the clipping link first; the Mac bake-and-delete choice remains a gap. These differences are stated explicitly rather than hidden behind a claim of complete native parity.

The repository includes reproducible build/package scripts, core tests and headless UI checks. Windows updates use `windows-v*` releases in this fork, never the upstream macOS DMG feed.
