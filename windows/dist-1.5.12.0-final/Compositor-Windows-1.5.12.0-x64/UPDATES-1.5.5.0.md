# Windows 1.5.5.0 Preview

2026-10-22。合并上游 main `551a35fc06fb8429fed3b99b175e31681875f580`，包含 1.5.4 / 1.5.5。Windows 为源码构建，尚未创建对应 GitHub Release；既有 1.5.3.2 安装包不包含本页改动。

沿用现有 Skia 显示与完整合成、32 DIP 主题标题栏、左工具栏取消滚动、同一实际快捷键归组、imagegen 切片排列图标；原有选区、蒙版、图层、钢笔、裁剪与切片工作流继续保留。

## CPU 回退及低资源

- 按不可变文档/显示版本缓存精确的 512 px 图块，LRU 上限为 min(128 MiB, 总准入预算/8)。平移/缩放复用图块；编辑、通道、校样与预览改变时重建。缓存租约保护并发绘制、取消、淘汰与文档关闭，保留原合成 halo 和导出精度。
- 屏幕输出仍最多 2048 px；窗口尺寸/DPI 变化纳入显示请求。复杂场景首次计算和修改后的重算仍有成本，不宣称所有操作达到 60 fps。
- 渲染/形状/渐变模糊新增集中分配检查；渐变模糊半径相同时只做一次高斯。低于等于 1 GiB 预算的 ONNX 会话禁用 arena/memory-pattern 缓存，修复推理后无法分配主体蒙版的问题。
- 新增两核、512/1024 MiB、托管 SIMD 关闭、软件绘制脚本和 CI 门禁。没有可用 Win10 环境；[可复现方案](COMPATIBILITY.md)与实际本机验证严格区分。

## 上游对应表

“已合并参考源码”不等于“Windows 已实现”。本轮移植现有 Windows 功能可直接对应的改动；以下边界同时写入操作文档。

| 上游变化 | Windows 状态 |
| --- | --- |
| 画笔压力大小 / 不透明度 | 已移植至笔触采样、插值、低压间距、笔迹预览与画笔/蒙版提交；检测到笔输入后显示控制，鼠标保持原像素行为。自动合成压力已验证；真实数位板待验收。 |
| Line 点线、虚线、长虚线、方/圆端点 | 已移植；新线条默认方端点，旧项目保持原渲染，奇数像素横/竖线对齐像素。Windows 使用 v25 保存，接受上游 v15/v16 同名字段。 |
| 初次打开不超过 100%；调整尺寸不自动改变缩放 | 已同步打开行为、Image Size；Canvas Size 已保留缩放。用户主动 Fit 仍可放大。 |
| 字体列表独立搜索和所有字体面 | 原有固定搜索栏保留，列表增加真实 PostScript 字体面，按实际字重/斜体预览与选择。 |
| 图层搜索/类型筛选 | 已有 Windows 名称、类型、多类型、锁定、组和智能对象筛选；保持现有 Photoshop 风格布局。上游新增 Selected 筛选、命中组包含后代未在本轮移植。 |
| PDF 逐页导入 | 参考源码已合并；Windows 暂未实现，需要可随安装包发布并验证 Win10 的 PDF 后端。当前导入格式未错误增加 PDF。 |
| PSD 画板分别打开标签 | 参考源码已合并；Windows 保留既有 PSD 图层导入和本地画板工具，本轮未移植自动拆分标签。 |
| RGB Split / Split to Layers / Channel Pack | 参考源码与上游测试已合并；Windows 暂未实现，不能用简单 RGB 偏移冒充上游光谱算法，也不通过预乘 PNG 损失透明像素的隐藏通道。 |
| Progressive Blur / Bloom / RGB Split 调整图层 | Windows 已有前两种破坏性滤镜；新的非破坏调整层暂未移植，macOS v16 含这些调整的项目会拒绝打开，不静默丢失。 |
| 非拉丁实体键、原生对话框快捷键、测试期间不检查更新 | macOS 对应修复已合并；Windows 保留 Avalonia 快捷键/IME、Windows 原生文件对话框及现有测试启动行为，不引入 AppKit/Sparkle 逻辑。 |

macOS 源码需 macOS/Xcode 才能验证，本机未运行 Swift 测试。新后端功能的后续移植应先补精度、保存格式、取消/预算和 OS 测试，具体不会被计入本轮 Windows 已完成功能。

## 验证

本机环境、逐项测试和可重现基准见 [PERFORMANCE.md](PERFORMANCE.md) 末节。低资源 AI 主体检查曾因内存预算失败，修复缓存策略后重新运行；失败记录没有当作成功证据。中文/英文 UI、新旧项目、压力/线条、取消及像素一致性均纳入回归。最新 CI 状态以 [Windows 工作流](https://github.com/konban47/Compositor/actions/workflows/windows.yml) 中本次提交为准。

Camera Raw 数据未变化（SHA-256）：

- 上游 CameraRawTables.bin：`e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190`。
- Windows CameraRawTables.deflate：`bc1f99628beadb07f50ddee1fc50de719b97c6e417e47d30b079532395e81034`。
- 上游及 Windows CameraRawShadows.bin：`76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705`。
