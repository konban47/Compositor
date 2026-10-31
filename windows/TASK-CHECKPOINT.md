# 截图任务交接记录

更新：2026-10-31，目标发行版 1.5.14.0 Preview。

## 必须保留的约束

布局、分组、菜单与交互参照用户截图；主题色、字体与控件风格沿用 Compositor；浮窗逻辑不变，标题按钮与面板菜单自 1.5.14.0 起按 Photoshop 样式。代码与 GitHub 文档同时更新，发布可追溯的预览版。Creative Cloud 同步不提供。独立颜色引擎不能标作 Adobe ACE 原引擎或声称已证明位精确一致。

## 本轮交付与验证入口

- 文件脚本：删除空图层、像素蒙版和效果烘焙；`BulkLayerScriptsTests` 与 `--file-workflow-checks` 检查 clipping、锁定、禁用蒙版、GIF 逐帧蒙版与效果保留、菜单调用、撤销／重做和不透明度。
- 导入：变量 CSV 复用已有流程；视频帧解码与帧动画；注释 JSON 往返；DICOM 多帧；原生 WIA。`FileImportTests`、`DicomImportTests`、`VideoImportIntegrationTests` 覆盖核心行为；真实 FFmpeg 测试仅在 PATH 有依赖时运行。
- `--file-workflow-checks` 包含中英文视频导入窗体与取消检查。扫描仪无硬件验收；不要将入口编译通过当作硬件验证。
- 完整结果以本版 [更新说明](UPDATES-1.5.14.0.md) 为准。

- 联系表 II：已补齐灰度／RGB／CMYK／Lab、8／16 位、ICC 和真实字体样式；增加按文件夹分页、分辨率单位、物理间距、自动间距和预设完整往返。`ContactSheetColorTests` 验证高于 8 位的像素精度，界面自检验证单位与设置往返。

## 已接入并本机验证：脚本事件管理器（2026-10-29）

- 基线 `windows-port@4f4eafa`。Claude 云端补丁在独立本机 Git 工作区接入，经修复编译歧义、重复事件、关闭事件缺口和异步事件竞争后验证。原交付补丁未编译的状态不再代表本版。
- 新增：`Compositor.Core/IO/ScriptEvents.cs`（事件、关联、JSON v1 设置、校验、日志、内置脚本逻辑）、`BatchProcessing.ApplyTo`、`ScriptEventsDialog.cs`、`MainWindow.ScriptEvents.cs`、`ConfirmDialog.Tell`、`Program.ScriptEventChecks.cs`、`ScriptEventsTests.cs`、中文词条。
- 接线：文件 ▸ 脚本菜单；`Command()` 菜单命令被选择时派发（异步命令不保证已完成）；启动应用程序每进程一次；新建；图像／每个 PSD 画板／每个 PDF 页面／项目成功打开后各一次，置入与失败打开不算；开始存储／存储；关闭标签或主窗口（额外文档视图不重复）；导出 PNG／JPEG／导出为。关闭事件中修改文档的项跳过；依赖活动图层的动作要求事件文档在前台。
- 验收入口：`ScriptEventsTests`；`--file-workflow-checks` 末尾的 `ScriptEventSelfCheck`（布局、添加／重复／移去／全部移去／取消／完成、添加事件、JS 限制、设置存盘重载、打开动作撤销重做、命令事件、存储前文件信息与存储后 JPEG、导出、关闭、禁用）。
- 验证：本机 Release 构建零警告／错误，Core 1536 通过、1 默认跳过；中英文 file-workflow／interaction／windows 检查通过。新增检查包含 .comp／manifest 单次打开、失败／置入不触发、打开为图层撤销重做、独立事件排队、递归抑制、并发保存与窗口关闭派发。空／非空管理器界面截图已核对；完整原始截图验收仍未完成。
- 1.5.12.2 不提供 JS/JSX 解释器、打印事件、Web／图层导出事件；导出事件已在 1.5.12.3 补齐（见下节）。不要重复覆盖已验证的本地修改。

## 已接入并本机验证：导出事件接线与 PDF 演示文稿属性（2026-10-30，1.5.12.3）

- 基线 `windows-port@a2e2ffdc920124ffae6368fa0a844fa9a9809b35`（1.5.12.2 Preview）。云端补丁 0002 以 `git am -3` 无冲突接入；本机截图发现“背景色”“字体大小”下拉框被固定标签列挤压截断，已改为自适应标签宽度。
- 导出事件：`WebExportDialog`／`LayerExportDialog` 新增 `CompletedExports`；Web、图层／图层复合（文件与 PDF）、通道打包、渲染视频成功后派发“导出文档”。Web 与图层对话框在关闭后派发一次，避免脚本提示压在模态对话框下。该路径暂无自动化检查（需要文件选择器）。
- PDF 演示文稿：`PdfPresentationOptions` 扩展（存储为演示文稿、自动换片秒数、循环、过渡、题注字段，旧 `FileNames` 兼容）；`PdfSlideshow` 以增量更新为每页写 `/Dur`、`/Trans`，目录写 `/PageMode /FullScreen`，循环写 Acrobat `app.fs` 脚本；对话框按截图分为输出选项（存储为、背景、字体大小、包含）与演示文稿选项（换片间隔、循环、过渡效果）。
- 验收入口：`PdfSlideshowTests`（无演示属性、/Dur 与 /Trans 计数、PDFium 仍识别页数、循环脚本、随机过渡可重复、非法秒数不写文件、题注字段）；`--file-workflow-checks` 中 `PdfPresentationSelfCheck`。
- 验证：本机 Release 构建零警告／错误，Core 1543 通过、2 默认跳过；中英文 file-workflow／interaction／windows 检查通过（独立设置目录）。新词条“标题”“扩展名”仅用于 PDF 题注复选框。
- 仍待人工：导出事件路径（需文件选择器）无自动化检查，建议把“是否派发”抽成可测函数；Acrobat／Edge／SumatraPDF 实测全屏、换片、过渡与循环；演示文稿峰值内存约为 PDF 大小 5 倍，需大文件实测。
- 上游 main 已到 `14055459feb47a2bd69ef99c1983f8155bea4e75`，新增 7 个提交（Volumetric Light、16 位深度保持、多选图层显示／剪贴蒙版／位深转换、空格移动选框、逐图层导出 PNG、Image Size 拖动上限）尚未评估移植。

## 已移植并本机验证：上游 main `1405545` 的 5 项改动（2026-10-30，1.5.12.4）

- 基线 `windows-port@908b1cdc4c35786a848a466dd455246037a08627`（1.5.12.3）。逐项对照上游 7 个提交：`5de396b`、`de0a43d`、`d693a2a`、`0673a4a`、`fc3c81a` 已移植；`42eb9b2` 无需移植（Windows 图像大小对话框无拖动调值）；`1405545` Volumetric Light 未移植。
- 位深：`DeepPixels.Painted` 增加整像素偏移，`ImageModeEdits.PixelOffset` 由新旧像素→文档矩阵求偏移；`DocumentHistory.End` 原有的 `Enforce` 挂钩不变（“转换图层位深”除外）。多选转换：`DeepPaint.DepthTargets／CanConvertLayers／ConvertLayers`。剪贴：`LayerMaskEdits.ReleasesSelected／CanToggleSelected／ToggleSelected`，可用判定在记录副本上进行，不改文档。导出：`LayerWorkflow.Isolated／IsolatedPixels／ExportFileNames` 与 `MainWindow.QuickExportLayers`。选框：`CanvasView.HoldMarqueeSpace`，`TakeHand` 在绘制选框时转交空格。
- 验收入口：`LayerDepthTests`、`SelectedClippingTests`、`LayerPngExportTests`；`--selection-options-checks` 的空格移动检查；`--layer-style-checks` 右键菜单项。
- 验证：Release 构建零警告／错误，Core 1555 通过、2 默认跳过；中英文 file-workflow／interaction／windows／selection-options／layer-style／image-workspace 检查通过。
- 仍待：多图层导出的文件夹与替换确认需人工点验；内容识别移动、粘贴等其他 8 位编辑路径未逐一补测；Volumetric Light 与 Rim Light 需 CPU/Skia 重写，建议单独一版。

## 已实现并本机验证：交接剩余项与上游 Rim／Volumetric Light（2026-10-31，1.5.13.0）

- 基线 `windows-port@a23989d`（1.5.12.4）。上游 main `1405545` 之前的全部适用提交现已移植：Rim Light（`b9a0ed8`、`97c3b3f`）与 Volumetric Light（`1405545`）为 CPU 实现（`RimLight.cs`、`VolumetricLight.cs`、`LightPlanes.cs`）。
- 新增：Web HTML／切片／输出设置与帧动画 GIF（`WebPage.cs`、`OutputSettingsDialog.cs`）；分层 PSD 写入（`PsdWriter.cs`）；Photomerge（`Photomerge.cs`）；合并到 HDR Pro（`HdrMerge.cs`、`HdrProDialog.cs`）；镜头校正与 lensfun 数据库（`LensProfiles.cs`、`lensfun/`）；PDF／FDF／XFDF 注释与演示文稿注释（`PdfNotes.cs`）；组蒙版／组效果／GIF 效果烘焙（`LayerBaking.cs`）；JavaScript／JSX（`ScriptHost.cs`，Jint 4.16.4）；链接的智能对象与打包（`SmartObjectLinks.cs`，项目格式 v32）。16 位图层的复制／粘贴修复，8 位编辑路径逐项测试（`EightBitEditDepthTests`）。
- 截图对照：本轮逐张核对“输出设置”1–7、储存为 Web（19、29）、Photomerge、镜头校正 1；其余文件工作流截图沿用前几版的验收。交接包中缺失的 117 张参考截图（如 stamp-history-eraser 等）无法核对。
- 验证：Release 构建零警告／错误，Core 1624 通过、2 默认跳过；中英文 file-workflow／interaction／windows／panel／selection-options／layer-style／image-workspace 检查通过；文档一致性检查通过。
- 仍待人工：Acrobat 中的 PDF 注释与演示文稿、Photoshop 中打开本程序写出的 PSD、真实照片的 Photomerge／HDR／镜头校正效果、多图层导出与 Web 存储的系统对话框。
## 已实现并本机验证：上游体积光深度、统一图标与 Photoshop 式浮窗（2026-10-31，1.5.14.0）

- 基线 `windows-port@c79f9bb`（1.5.13.0）。上游 main `76b7921` 之前的全部适用提交已移植：体积光的距离、深度对比度、提取深度、主体边缘合成、光源发光阈值和预览复用（`VolumetricLight.cs`），深度模型为首次下载并校验的 Depth Anything V2 Small ONNX（`DepthModel.cs`）。上游的 Vision 人像分割与姿态细化无 Windows 对应，使用 U²-NetP 主体蒙版。
- 图标：全部按钮、工具、面板标志、排列图标与浮窗标记改为统一的 `IconSet`（24 网格、1.5 线宽）；上游有的 15 个工具按其 SF Symbols 造型，上游自绘的 4 个图标与应用图标直接沿用。旧 `GeneratedSymbols.cs` 与排列位图移除（位图移至 `design/slice-order-icons`）。
- 浮窗：标签去掉 × 与下划线，当前标签与面板同色；标签行末端 ≡ 面板菜单（关闭、关闭选项卡组、浮动、折叠）；停靠组只留拖动条，浮动组保留 «／» 与 ×。停靠、拖动、浮动、分组、工作区逻辑未改。
- 维护：1.5.13.0 误提交的 `windows/dist-*` 文档副本移出版本控制并忽略。
- 验证：见 [1.5.14.0 更新](UPDATES-1.5.14.0.md)。真实模型测试需设 `COMPOSITOR_DEPTH_MODEL_FILE`；CI 不下载模型。
- 仍待人工：真实照片上的深度与雾气效果、首次下载在慢速网络下的体验；其余按钮在实际界面各处的大小和对齐需逐页过目（自检只覆盖图集与主要面板）。
## 尚未完成

交接清单中的功能项均已实现（边界见 [文件工作流](FILE-WORKFLOWS.md)）。不提供：Adobe Bridge、Adobe 镜头配置文件、Creative Cloud 同步、打印事件、编译的 .jsxbin 与 ExtendScript 的 File／Folder／ActionDescriptor。

画笔、图章、橡皮擦、渐变、修饰、视图、路径、文字／字符／段落、图层复合、时间轴、颜色／OCIO、预设和其余文件功能已有历史实现，仍需按原始截图逐项检查及联动验收。对应 `TOOL-OPTIONS.md`、`PAINTING-AND-GIF.md`、`TYPE-AND-MASKS.md`、`TIMELINE-AND-PRESETS.md`、`COLOR-MANAGEMENT.md`、`FILE-WORKFLOWS.md` 的边界；不能仅凭编译或本轮测试标记全部完成。

## 继续工作的精度规则

先读本记录、最新更新说明和 `git diff --stat`，再读取待修改的小范围源码。保留原始截图目录／文件名与需求的映射，按内容识别截图编号错配。将已实现、已验证、限制和下一步分别记录；命令输出写入本地日志，仅读取摘要与失败位置。每个阶段更新检查点，避免重复读取整段聊天和大量截图。系统自动压缩不受本仓库控制。
