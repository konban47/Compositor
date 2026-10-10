# Compositor for Windows

源自 [Robbie Tilton 的 Compositor（macOS 原项目）](https://github.com/robbietilton/Compositor)。感谢 Robbie Tilton / Wonder Assembly 以 MIT 许可证开源，也感谢 [chenguisen 的 Windows 移植](https://github.com/chenguisen/Compositor/tree/compositor_win)为本项目提供基础。

本仓库维护 **Windows 10/11 x64 图像编辑器**，提供简体中文和英文界面，采用熟悉的图层、蒙版、选区和滤镜工作流。Windows 源码位于 `windows/`，默认分支为 `windows-port`。

[下载 Windows 安装包和免安装版](https://github.com/konban47/Compositor/releases/tag/windows-v1.4.8.2) · [使用与构建说明](windows/README.md) · [更新记录](windows/UPDATES-1.4.8.2.md) · [许可证与来源](windows/THIRD-PARTY-NOTICES.md)

## 安装

目标系统：Windows 10 1809 及以上、Windows 11，64 位 x64。

- **安装版**：运行 `Compositor-Windows-1.4.8.2-x64-setup.exe`，按中文或英文向导安装。
- **免安装版**：完整解压 `Compositor-Windows-1.4.8.2-x64-portable.zip`，运行 `Compositor.exe`。
- 两种发行包均自带 .NET 运行时。免安装版如缺少 Visual C++ x64 运行库，可运行随包 `redist/vc_redist.x64.exe`。
- 中文系统默认使用简体中文。通过“帮助 → 语言”切换，下次启动生效。

## 常用操作

| 操作 | 使用方式 |
| --- | --- |
| 导入图像 | 拖动桌面／资源管理器文件到画布，或“文件 → 导入图像” |
| 移动对象 | 按 V，选择图层或单击对象后拖动；支持多选图层变换 |
| 平移画布 | 按 H，或按住空格，或按住鼠标中键拖动 |
| 以鼠标为中心缩放 | 滚动鼠标滚轮；Ctrl＋中键上下拖动也可缩放 |
| 锁定／解锁图层 | 右键“锁定图层”或 Ctrl+\；点击行末锁图标解锁 |
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
| 仅显示画布 | F 进入，F 或 Esc 返回 |

`.comp` 项目是文件夹，需保留整个文件夹及其中的 `manifest.json` 和图层资源。普通图像可导出 PNG/JPEG/PDF（PDF 为合成图像页面），PSD/PSB 支持导入并显示转换报告。

## 功能

- 图层缩略图、搜索和类型筛选、锁定、分组折叠、链接、不透明度与填充；24 种混合模式、蒙版、调整图层和图层样式。
- RGB 分通道显示与编辑、Alpha 通道新建／复制／重命名／删除、选区存取及灰度绘制。
- 选区及羽化、画笔／橡皮擦、仿制图章、污点修复、涂抹、液化、文字、形状、渐变和变换。
- 色阶、曲线、色相／饱和度、Camera Raw、模糊、仿色及其他滤镜。
- 离线选择主体、对象选择和移除背景，图片不上传服务器。
- 可调整高度的属性／历史记录面板；蒙版密度、羽化、反相、选择并遮住、色彩范围、矢量选区蒙版和应用蒙版。
- 多标签页、撤销／重做、历史快照、从状态新建独立文档、异步保存、未保存关闭保护和独立 Windows 更新频道。
- 中文界面使用 Photoshop 常见术语，提供中文输入法接口、随包中文字体和中文换行。

本版读取 v1–v13 项目。使用非默认蒙版密度／羽化、矢量蒙版或新增文字样式时保存为 **Windows 扩展格式 v13**；仅使用锁定、链接、非默认填充或 Alpha 通道时保存为 v12，其余为 v11。旧版不接受超出其支持范围的版本；macOS 原版当前仅支持至 v11。[文件格式与兼容性](docs/project-format.md#windows-extension-version-13)

## 验证与当前状态

**1.4.8.2 Preview** 增加属性和历史记录面板、蒙版链接与独立变换，修复参考线和导航器。继续对齐上游 1.4.8，并移植其后至 `75147a2` 的 Camera Raw 自适应调整。核心测试 930 项，另有中英文窗口、图层／通道、鼠标中心缩放、导出预览、文件拖入、选区、安装和卸载检查。[持续集成](https://github.com/konban47/Compositor/actions/workflows/windows.yml)

本机验证环境为 Windows 11；Windows 10 实机、真实输入法候选窗、多显示器和数位板仍需人工验收。使用 CPU/Skia 与 U²-NetP，渲染、主体识别及 RAW 显影与 Apple 原生实现存在差异；应用和安装包尚未进行商业代码签名。高级 OpenType／东亚／中东排版、动态文字、历史记录画笔及独立矢量路径编辑尚未实现；本版面板提供下文列明的常用功能。[完整已知差异](windows/README.md#已知差异和验收边界)

## 开发

需要 .NET 10 SDK：

```powershell
dotnet build windows/Compositor.slnx -c Release -warnaserror
dotnet test windows/tests/Compositor.Core.Tests -c Release
./windows/scripts/package.ps1 -OutputDirectory "$PWD/dist" -Installer
```

生成安装包需要 Inno Setup 6。GitHub Actions 可构建自带运行时的安装版与免安装版。

MIT 许可证，见 [LICENSE](LICENSE)。原始 macOS 源码及历史保留在仓库，macOS 的下载、介绍和贡献请访问顶部上游链接。
