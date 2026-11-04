# Windows 1.5.3.2 Preview

2026-10-19。核对 [上游 Compositor](https://github.com/robbietilton/Compositor) 最新 main 仍为 `365b5745eda537f984f3d5bed1e53c361dbc9c91`（1.5.3）。该提交已合并，本版没有额外上游变更。

- 按 15 张补充图完成钢笔、自由钢笔和弯度钢笔选项：形状／路径模式、选区／蒙版／形状转换、布尔操作、组件排列与对齐分布、路径粗细／颜色／橡皮带，以及自由钢笔拟合和磁性设置。
- 距起点 10 屏幕像素内显示闭环圆圈和蓝色实心起点；节点选中后填蓝，Ctrl+Enter 将路径接入现有选区编辑。工作路径加入保存与撤销，并随文档几何变换；使用 Windows v24。
- 图层左键按住或拖动支持浮动预览、蓝色插入线、平滑跟随、边缘自动滚动和落位动画。多选保序、锁定、组关系、取消、蒙版点击与 Ctrl+缩略图取选区均继续可用。
- 同步首页、操作手册、文件格式、发行包文档和 CI；新增 `--pen-layer-checks`，加入中英文与发行程序回归。完整用法见 [钢笔与图层拖动](PEN-AND-LAYERS.md)。

沿用上一版的 32 DIP 主题标题栏、左工具栏取消滚动、同一实际快捷键归组和 imagegen 切片排列图标；路径排列也复用这些本项目图标。原有选区、人物入口、裁剪、透视、自动切片、Alt 辅助、v23 平滑圆角与 Skia 渲染保持可用。

本机验证：Release 零警告、零错误构建；1200 项核心测试；23 组英文和 17 组中文界面回归全部通过。Windows 11 实际窗口已核对中文钢笔选项、路径闭合与 Ctrl+Enter 选区。组合路径额外覆盖减法孔洞保存及合并的回归，保证中空区域不会被重新填满。

[GitHub Actions 39230205560](https://github.com/konban47/Compositor/actions/runs/39230205560) 已全部通过，验证提交为 `b0bce9cd3f70efbb095b6ffaee3abc05abaa2712`。Windows Server 2022／2025 分别通过文档检查、零警告构建、1200 项核心测试及 23 组英文／17 组中文界面检查。安装版与免安装版由同次 CI 生成，发行程序的 17 组界面检查、临时 Windows Server 2025 上的全新安装／卸载也全部通过。[发行包与 SHA-256 校验文件](https://github.com/konban47/Compositor/releases/tag/windows-v1.5.3.2) 对应该验证提交。包内文档是构建时快照，最终验收记录见本页和 Release。

发布前下载该 CI 产物，逐项比对两个发行文件的 SHA-256 均一致；另核对免安装包中的程序版本为 1.5.3.2，包含新手册及本版更新记录。

没有 Windows 10 实机、真实数位板、多显示器或管理员升级验收结论。Windows Server CI 仅证明对应运行环境中的自动检查。

Camera Raw 数据来源（SHA-256，移植表保持原版本）：

- 上游 CameraRawTables.bin：`e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190`。
- Windows CameraRawTables.deflate：`bc1f99628beadb07f50ddee1fc50de719b97c6e417e47d30b079532395e81034`。
- 上游及 Windows CameraRawShadows.bin：`76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705`。

## 2026-10-22 后续源码修订

完成审查报告 F01–F16 对应的安全、内存、调度、文件读取、显示和 CPU 优化；未改变项目格式版本。具体实现、微基准改善与剩余边界见 [性能修订记录](PERFORMANCE.md)。新增性能门禁随 CI 执行；这里不把此前发布包的验证结果视为新修订的验证证据。
