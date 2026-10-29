# 截图任务交接记录

更新：2026-10-29，目标发行版 1.5.12.1 Preview。

## 必须保留的约束

布局、分组、菜单与交互参照用户截图；主题色、字体与控件风格沿用 Compositor；浮窗标题按钮不变。代码与 GitHub 文档同时更新，发布可追溯的预览版。Creative Cloud 同步不提供。独立颜色引擎不能标作 Adobe ACE 原引擎或声称已证明位精确一致。

## 本轮交付与验证入口

- 文件脚本：删除空图层、像素蒙版和效果烘焙；`BulkLayerScriptsTests` 与 `--file-workflow-checks` 检查 clipping、锁定、禁用蒙版、GIF 逐帧蒙版与效果保留、菜单调用、撤销／重做和不透明度。
- 导入：变量 CSV 复用已有流程；视频帧解码与帧动画；注释 JSON 往返；DICOM 多帧；原生 WIA。`FileImportTests`、`DicomImportTests`、`VideoImportIntegrationTests` 覆盖核心行为；真实 FFmpeg 测试仅在 PATH 有依赖时运行。
- `--file-workflow-checks` 包含中英文视频导入窗体与取消检查。扫描仪无硬件验收；不要将入口编译通过当作硬件验证。
- 完整结果以本版 [更新说明](UPDATES-1.5.12.1.md) 为准。

- 联系表 II：已补齐灰度／RGB／CMYK／Lab、8／16 位、ICC 和真实字体样式；增加按文件夹分页、分辨率单位、物理间距、自动间距和预设完整往返。`ContactSheetColorTests` 验证高于 8 位的像素精度，界面自检验证单位与设置往返。

## 进行中：脚本事件管理器（2026-10-29，Claude，未在本机编译验证）

- 基线 `windows-port@4f4eafa`。实现于云端克隆，云端无 .NET/NuGet，**尚未编译、未运行测试**；补丁与改动文件放在交接目录 `claude-output`。
- 新增：`Compositor.Core/IO/ScriptEvents.cs`（事件、关联、JSON v1 设置、校验、日志、内置脚本逻辑）、`BatchProcessing.ApplyTo`、`ScriptEventsDialog.cs`、`MainWindow.ScriptEvents.cs`、`ConfirmDialog.Tell`、`Program.ScriptEventChecks.cs`、`ScriptEventsTests.cs`、中文词条。
- 接线：文件 ▸ 脚本菜单；`Command()` 菜单命令事件；窗口打开（启动应用程序，每进程一次）；新建；打开（图像/PSD/PDF/项目，按文档身份判断，置入不算）；开始存储／存储；关闭（关闭前运行，改文档的项跳过）；导出 PNG／导出为。
- 验收入口：`ScriptEventsTests`；`--file-workflow-checks` 末尾的 `ScriptEventSelfCheck`（布局、添加／重复／移去／全部移去／取消／完成、添加事件、JS 限制、设置存盘重载、打开动作撤销重做、命令事件、存储前文件信息与存储后 JPEG、导出、关闭、禁用）。
- 待办：本机 `build -warnaserror`、Core 测试、中英文 file-workflow／interaction／windows 检查；对照截图人工核对；通过后再更新版本号与 Release。

## 尚未完成

JS/JSX 解释器（脚本事件管理器的外部脚本）；链接智能对象／依赖打包与 Bridge；Photomerge、HDR Pro、完整镜头配置数据库；PSD 写入；Web HTML 切片与动画输出；PDF 幻灯片播放属性；组蒙版／组效果、GIF 效果烘焙；Adobe/PDF/FDF 注释格式；Rim Light 上游移植。

画笔、图章、橡皮擦、渐变、修饰、视图、路径、文字／字符／段落、图层复合、时间轴、颜色／OCIO、预设和其余文件功能已有历史实现，仍需按原始截图逐项检查及联动验收。对应 `TOOL-OPTIONS.md`、`PAINTING-AND-GIF.md`、`TYPE-AND-MASKS.md`、`TIMELINE-AND-PRESETS.md`、`COLOR-MANAGEMENT.md`、`FILE-WORKFLOWS.md` 的边界；不能仅凭编译或本轮测试标记全部完成。

## 继续工作的精度规则

先读本记录、最新更新说明和 `git diff --stat`，再读取待修改的小范围源码。保留原始截图目录／文件名与需求的映射，按内容识别截图编号错配。将已实现、已验证、限制和下一步分别记录；命令输出写入本地日志，仅读取摘要与失败位置。每个阶段更新检查点，避免重复读取整段聊天和大量截图。系统自动压缩不受本仓库控制。
