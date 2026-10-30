# Windows 1.5.3.1 Preview

2026-10-17。核对 [上游 Compositor](https://github.com/robbietilton/Compositor) HEAD 仍为 `365b5745eda537f984f3d5bed1e53c361dbc9c91`（1.5.3），已在 1.5.3.0 合并；本版没有新增上游提交，也不重复合并。

- 修复深灰主题弹窗标题栏仍为黑色。应用弹窗与主窗口共用 32 DIP 主题标题栏和控制图形；关闭悬停红色，保留 Windows 11 圆角。主菜单按钮高度、文字和 logo 尺寸不变。
- 重排图层面板筛选、混合模式／不透明度、锁定／填充、图层行与底部动作。类型按钮支持多类组合及总开关，名称筛选和不透明度输入／滑块可用；保留原项目图标、层级和编辑行为。
- 左侧工具栏取消滚动；同一实际快捷键自动归组，含 C 的四个裁剪／切片工具。小窗口收紧间距，大窗口保持紧凑；单／双列、长按／右键切换、附加工具与预设继续可用。
- 自定义工具栏两栏为滚动条留出位置，长名称自动换行；同快捷键无法拆成重复按钮。
- 切片选项按钮未选择时自动选取现有或自动切片并打开完整属性弹窗。确定、取消和撤销均保留正确行为。四个排列图标按用户要求使用内置 imagegen 生成，直接以透明通道着色；[资源和完整提示词](design/slice-order-icons/README.md)。
- 同步首页、使用手册、安装包内文档、版本和 CI 文档一致性检查。项目文件格式仍为 Windows v23，无新增持久字段。

本机验证：1184 项核心测试；Release 构建零警告、零错误；22 组英文和 16 组中文界面回归通过。新增 `--panel-workflow-checks` 覆盖四套主题标题栏与红色关闭悬停、同快捷键归组、无滚动工具栏、图层筛选／不透明度、切片属性确定／取消／撤销和生成图标。实际 Windows 11 窗口检查确认深灰弹窗、圆角、最大化／还原和关闭。文档版本、功能描述及图标来源哈希检查通过。

发行包与独立安装验收结果见 [GitHub Release](https://github.com/konban47/Compositor/releases/tag/windows-v1.5.3.1)。本次未测试 Windows 10、真实数位板、多显示器或正式管理员升级。

Windows 继续使用既有 CPU／Skia 渲染；本版没有新增云端图像请求或模型功能。首次发布时 GitHub Actions 返回 HTTP 422（Actions has been disabled for this repository），因此当时采用本机构建与发行包验收。随后确认 GitHub 自动停用了此 Fork 的工作流；仓库所有者于 2026-10-17 重新启用。

恢复后的 [Windows CI 运行](https://github.com/konban47/Compositor/actions/runs/39028925211) 验证发布提交 `e3c28db7d65a5439aca3debedef7b78b35695f3e`：Windows Server 2022 和 2025 均通过文档检查、Release 零警告构建、1184 项核心测试以及 22 组英文／16 组中文界面检查。Windows Server CI 不等同于 Windows 10 实机或正式管理员升级验收。

同次 CI 的 Windows Server 2025 打包任务通过：构建自带运行时的免安装版和正式安装器，发行程序通过 16 组界面检查，正式安装器完成临时目录中的全新安装与卸载，产物上传成功。已发布下载包中的文档为首次发布时的验证快照；恢复后的追加验证记录见本页和 GitHub Release。

Camera Raw 数据来源（SHA-256，移植表保持原版本）：

- 上游 CameraRawTables.bin：`e67ff4dbcce35cb7765752acff4405be5c934d913ef1396b2ca3fc6dc8a85190`。
- Windows CameraRawTables.deflate：`bc1f99628beadb07f50ddee1fc50de719b97c6e417e47d30b079532395e81034`。
- 上游及 Windows CameraRawShadows.bin：`76578539bc45a32817aff669c4e41523d7040f3c691ad35adf0723a70a89a705`。
