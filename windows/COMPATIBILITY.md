# Windows 10 与低资源验证 · 1.5.13.0 Preview

2026-10-22。当前没有可用 Windows 10 实机或虚拟机，用户选择先完成可复现方案。**Windows 10 验收状态：待执行。** 以下本机结果来自 Windows 11 Pro build 26200；CPU 限制、禁用 SIMD、软件绘制或 Windows Server CI 均不等于 Windows 10 验收。

## 平台边界

| 环境 | 当前证据 / 要求 |
| --- | --- |
| Windows 11 x64 | 本机自动回归、原生窗口 CPU 回退及安装包构建；详情见更新记录。 |
| Windows 10 22H2 x64 / build 19045 | 首要兼容性验收目标，尚未运行。Avalonia 12 当前列为 Tier 2；这不是应用实测结果。 |
| Windows 10 LTSC / Enterprise | 必须记录具体版本、补丁和授权支持周期，单独运行同一套检查。不能把 LTSC 21H2 与 22H2 混为一谈。 |
| Windows Server 2022 / 2025 | GitHub CI 独立检查，无 Win10 客户端、真实数位板或核显认证含义。 |
| 32 位 / 原生 ARM64 | 未提供对应包。x64 仿真不在本次验收范围。 |

安装器历史 `MinVersion=10.0.17763` 只是允许安装的门槛，不代表运行时支持承诺。微软当前 [.NET 10 OS 表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)及[Windows 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)按具体 Windows 10 企业/LTSC 版本列支持，不能笼统宣称所有 Win10 Home/Pro 仍获 .NET 官方支持。[Avalonia 平台表](https://docs.avaloniaui.net/docs/supported-platforms)也按版本和支持等级划分。打包仍使用锁定的 .NET 10 / Avalonia 12.1.3 / SkiaSharp 4.152.1；本次没有为兼容性验证更换渲染后端。

原生边界检查：Windows 11 的 DWM 标题栏属性调用已有 `IsWindowsVersionAtLeast(10,0,22000)` 保护；Windows 10 使用自绘主题标题栏。原生窗口启动允许 ANGLE 失败后转 Software，且 `UseGpu=false` 可显式禁用 GPU。安装包携带自包含 .NET 与校验过微软签名的 VC++ x64 运行库。依赖还包括 ONNX、Skia、HarfBuzz、LibRaw、Magick；静态 API 审查和成功加载不能证明它们在未运行的 OS/CPU 上兼容。

## 自动执行

需要 PowerShell 7、.NET 10 SDK、完整仓库与模型/字体资产；首次构建需要 NuGet 网络。进入仓库根目录，在真实桌面登录会话运行：

```powershell
dotnet build windows/Compositor.slnx -c Release -warnaserror
./windows/scripts/compatibility-checks.ps1 -SkipBuild -Scalar -Native -MemoryMiB 512 -Output artifacts/win10-scalar-512 -RequireWindows10
./windows/scripts/compatibility-checks.ps1 -SkipBuild -Native -MemoryMiB 1024 -Output artifacts/win10-simd-1024 -RequireWindows10
```

本机 Windows 11 或 Server 上复现时去掉 `-RequireWindows10`。这个参数在错误 OS 上会直接失败，不会自动改成 Win10“通过”。验证自包含程序时另加 `-App '绝对路径/Compositor.exe'`；核心/性能探针仍需要 SDK。每次使用新的输出目录，以保留证据。默认不做原生桌面测量；需要 `-Native` 才执行它。没有管理员权限要求，脚本不会安装软件或修改用户偏好。

脚本为各子进程设两个允许的逻辑处理器亲和性、`DOTNET_PROCESSOR_COUNT=2`、`COMPOSITOR_MEMORY_MB=512/1024` 和隔离的 `UseGpu=false`、`AcceleratedCanvas=false` 设置。`-Scalar` 另设 `DOTNET_EnableHWIntrinsic=0`，探针校验托管 SIMD 确实关闭。**这不关闭 ONNX/Skia 的原生 SIMD，也不是模拟旧指令集 CPU或限制系统物理内存。** 512 MiB 是应用分配准入预算，不是 OS 工作集硬限制。需要验证真实 4 GiB 设备时使用对应物理机或限制 RAM 的 VM。

自动门禁包括：13 层复杂合成逐字节对照、缓存复用和预算；取消/分配失败/原子导出/保存安全；新线条和压力回归；中英 Windows、交互、钢笔/图层检查；可选的 60 帧原生 CPU 视口操作。输出 `summary.json`、逐项日志、截图、合成 `.comp` 样本及 `native-cpu.json`。任何失败或超时返回失败并保存报告。AI 检查要求真正生成蒙版；没有将内存不足改判为成功。

本次暴露并修复的问题：低于等于 1 GiB 预算时 ONNX 关闭 CPU arena 与 memory-pattern 缓存，避免一次推理后保留的工作内存阻止蒙版分配；正常预算继续保留原缓存策略。

## 实机验收清单

建议设备：2 核 / 4 GiB RAM / 集成显卡或基础显示适配器 / 1920×1080；分别测试 GPU 自动、软件绘制，100%/150%/200% DPI。VM 建议 2 vCPU、4 GiB 固定内存，先不启用 3D 加速。保存 OS edition/build、补丁、CPU 指令集、GPU/驱动、显示缩放、程序版本/提交及安装包 SHA-256。

1. 新账号与空设置目录运行免安装版；安装版分别验证首次安装、原版本升级、卸载保留文档。管理员升级单独记录，自动脚本不覆盖此项。
2. 中英界面和微软中文 IME；暗灰/浅色标题栏、浮窗、最大化/还原、多屏 DPI；打开/保存对话框的剪贴板和输入不被全局快捷键截走。
3. 打开合成复杂项目，连续缩放/平移 60 次；再编辑图层可见性、蒙版、效果、颜色通道并撤销，确认缓存没有旧像素。快速切换/关闭文档和取消后台任务。
4. 真实数位板分别测试压力大小、不透明度、低压到高压及抬笔，鼠标应保持全压力。软件回退下编辑中文、图层拖动、钢笔闭合与 Ctrl+Enter。
5. 分别导入普通 12 MP 和接近设备预算的大 PSD/PSB/TIFF；保存、关闭重开、PNG/TIFF 导出、Undo/Redo；记录耗时、峰值私有内存、磁盘占用。预算不足时必须明确拒绝，旧文件保持字节不变。
6. 在上述设备上完成 30 分钟混合编辑、休眠/唤醒和渲染设备丢失/驱动重启场景。画面不能停留在旧版本；若失败收集最小复现、日志和样本哈希，不上传用户私密原图。

验收通过需要全部自动检查通过，人工清单记录完成，且任何失败保留原文件。冷缓存首帧、稳态平移、编辑后重算分开报告；不能把缓存命中合成耗时解释成显示器帧率。实际 Win10 报告回填本页时保留原始 JSON 与精确提交，不能覆盖本次 Win11 记录。

## 1.5.5.1 追加记录（2026-10-23）

本机两种配置均通过：512 MiB / 两核 / 托管 SIMD 关闭，1024 MiB / 两核 / SIMD 开启；两者强制软件绘制和关闭场景直绘。每种运行 58 项安全/缓存/上游/修饰核心检查、8 组中英界面检查及 60 次原生窗口视口操作。脚本增加 `--utility-retouch-checks`，在低预算下实际调用 PDFium 导入两页 PDF，并检查修复、移动、撤销、标注和预设。12 个缓存图块中，局部编辑复用 11 块；输出与完整重算逐字节相同。

原生复杂场景首帧分别为 3748.72 ms 和 2666.44 ms；帧回调中位分别为 57.28 ms 和 54.74 ms。峰值工作集分别为 219 MiB 和 225 MiB。低资源验证报告仍明确标记 `ActualWindows10Client=false`、`Validation=Host resource simulation; NOT Windows 10 validation`。证据：[512 MiB](benchmarks/2026-10-23-scalar512-summary.json)、[1024 MiB](benchmarks/2026-10-23-simd1024-summary.json)、[1024 MiB 原生窗口](benchmarks/2026-10-23-simd1024-native-cpu.json)。

PDF 新增原生依赖为 PDFium 152.0.7961，完整许可随安装包发布；在 Win10 上必须实际测试原生 DLL 加载、中文字体 PDF、密码 PDF、超大页的预算拒绝。PSD 画板应包括画布外画板及图层蒙版样本。三种新滤镜调整层还应测试保存/重开、CPU 模式下预览、取消与撤销；真实数位板需检查修复/移除笔尖压力。生成式移除和干扰物检测需另外提供服务凭据，确认请求、失败、取消及输出尺寸，本轮未进行真实云端验收。

本机已有安装的 Compositor，未通过覆盖安装/卸载扰动该实例；新安装包的安装/卸载自动检查由干净的 Windows Server CI 执行。该检查仍不等于真实 Win10 或既有用户安装升级测试。

## 1.5.6.0 画笔／GIF 追加记录（2026-10-23）

两档配置均完成 137 项核心安全检查、12 组中英界面检查和 60 帧原生软件绘制。新增画笔／图章／历史／橡皮擦与 GIF 导入、播放隔离、帧编辑、保存重开、导出检查。最终失败保护和中文标签另由完整核心回归与两语言关键流程复核。

| 配置 | 原生首帧 | 帧回调中位 | CPU 合成中位 | 峰值工作集 |
| --- | --- | --- | --- | --- |
| 512 MiB，双逻辑处理器，托管 SIMD 关闭 | 3918.79 ms | 56.77 ms | 9.13 ms | 226 MiB |
| 1024 MiB，双逻辑处理器，SIMD 开启 | 2685.30 ms | 57.27 ms | 8.86 ms | 227 MiB |

两档复杂视口与编辑后输出均逐字节一致；编辑仅重绘 1 块，复用 11 块。报告明确保留 `ActualWindows10Client=false`，不代表真实低端 CPU 或 Windows 10 验收。证据：[512 MiB](benchmarks/2026-10-23-156-scalar512-summary.json)、[1024 MiB](benchmarks/2026-10-23-156-simd1024-summary.json)、[512 MiB 原生窗口](benchmarks/2026-10-23-156-scalar512-native-cpu.json)、[1024 MiB 原生窗口](benchmarks/2026-10-23-156-simd1024-native-cpu.json)。
