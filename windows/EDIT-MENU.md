# 编辑菜单与首选项

适用于 Windows **1.5.6.0 Preview**。本次截图展示的是 Photoshop 的“编辑”菜单；原有“图像”菜单保留在 [图像与属性手册](IMAGE-WORKSPACE.md)。以下说明实际行为及平台边界。

## 历史与粘贴

撤销、重做和切换最终状态使用当前文档历史。渐隐按指定不透明度混合最近操作前后的同尺寸像素，不改变图层变换；不适用的操作会提示。清理剪贴板、历史或全部需要确认，清理不能撤销。没有视频缓存，入口置灰。

选择性粘贴包括系统剪贴板纯文字、原位粘贴、贴入和外部粘贴。原位粘贴保留复制图层坐标；贴入／外部粘贴要求目标选区，生成未链接的图层蒙版，保留羽化覆盖率并与原有启用蒙版相乘。跨文档图层、组、样式和高位深资源仍用内部剪贴板复制。纯文字不继承源格式。粘贴及蒙版是一个撤销步骤。

## 文字、填充与描边

- 查找替换可选大小写、整词和全部文字层；替换保留图层级文字样式，清除旧的逐字符字体／颜色运行，以免索引错位。锁定图层不会修改。
- Windows 拼写检查调用本机安装的语言字典，可修改语言标记、查看建议并替换；字典不可用时提示。中文逐字校对、在线语法服务及 Adobe 字典未提供。
- 填充支持前景、背景、自定颜色、图案、黑、白、50% 灰；描边支持宽度、内侧／居中／外侧。两者提供不透明度、七种混合模式和保留透明度，遵守图层锁定、RGB 通道和选区。
- 内容识别填充继续使用本地修复实现。生成式填充把图像交给用户配置的图像 API，将返回结果置于新图层并用原选区蒙版限制可见范围；请求不传 Adobe 专有选区协议。

## 变换、对齐和图像组合

自由变换及缩放、旋转、斜切、扭曲、透视、变形复用置入工具栏。Enter 确认、Esc 取消，应用后为一个历史操作。“再次”重复最近确认的几何变换；不重放网格节点编辑。分割变形仅在活动网格变形时可用。

操控变形建立 9×9 网格：单击添加图钉，拖动图钉改变局部形状，Alt 单击移除；Enter 确认、Esc 取消。使用距离加权网格插值，没有 Photoshop 骨架、深度遮挡与碰撞解算。网格变形与操控变形当前输出 8 位栅格结果，文字／形状会栅格化。

内容识别缩放通过 ImageMagick Liquid Rescale 接缝裁减改变所选层尺寸，并调整图层边界；大幅缩放可能改变人物或重复纹理。没有 Photoshop 皮肤保护／保护通道模式。

自动对齐需要至少两个像素层，按层堆栈中的第一层做亮度相关平移配准，可限制最大位移；不自动解算旋转或透视。自动混合提供均值堆栈和局部对比度加权焦点堆栈，新增结果层并隐藏源层，源层仍可编辑。操作最多处理 3200 万源像素；不是全景接缝或 Adobe 景深合成引擎。

天空替换使用事先选出的天空范围和本地替换图像，生成独立蒙版层，可继续画蒙版。去除反射打开带有参考图和去反射提示的生成式工作区，需要用户已配置的 API；没有内置 Adobe 去反射模型。

## 预设、菜单与首选项

Ctrl+K 打开常规首选项，Ctrl+Shift+K 打开颜色设置，Ctrl+Alt+Shift+K 打开键盘快捷键，Ctrl+Alt+Shift+M 打开菜单设置；可在快捷键窗口更改。

定义画笔记录参数和当前内容的灰度笔尖，黑色为实心、白色为空；定义图案保存像素图案，定义自定形状保存所选矢量路径。预设管理器可应用、重命名、删除、导出，迁移预设导入并合并本项目 JSON 文件。每图最大 2048×2048，单文件上限 32 MB、最多 500 项；不读取 Adobe ABR／PAT／CSH。

PDF 预设入口设置扁平单页 PDF 的默认分辨率及 JPEG 质量，不实现 Adobe PDF joboptions、分页或印前标准预设。远程连接配置 OpenAI 兼容图像服务的 API 地址和密钥，凭据的保存方式见 [工作区手册](WORKSPACE.md)，不实现 Photoshop 远程控制协议。

菜单窗口可隐藏／恢复命令，配置不依赖界面语言。“菜单”入口始终保留以便恢复。Ctrl+K 现在打开 19 分类的统一首选项，包含搜索、草稿确定／取消／重置、高级图形设置及已有配置入口；主题、光标、历史、恢复、导出、单位、文字与性能等实际接入及限制见 [首选项手册](PREFERENCES.md)。OpenCL 迁移暂缓，继续使用 Skia；暂存盘换出和 30 位显示未实现。

这些配置保存在用户设置目录的 `preferences.json`，预设保存在 `presets.json`，不写入 `.comp`，不需要项目格式升级。

## ICC、OpenColorIO 与 HDR

编辑工作空间仍是 sRGB／扩展 sRGB。“指定配置文件”按所选源 ICC 解释当前图层的数值，再转到 sRGB，烘焙到图层并可撤销；并非文档级无损配置文件指派。“转换为配置文件”将合成结果转换并导出嵌入 ICC 的 16 位 TIFF。内置 sRGB 和 Adobe RGB，也可加载 ICC／ICM。ICC 功能面向 SDR；没有 CMYK 原生工作通道或印刷软打样。

OpenColorIO 接口由用户选择已安装的 `ocioconvert`、配置文件、输入／输出色彩空间。勾选应用后使用本地浮点 TIFF 中间文件调用转换器，验证输出尺寸后写回所选层；失败或取消不修改图层。工具未随包安装，配置决定实际变换；应选择适合 sRGB 预览的输出。文档不会自动记录或重新执行外部 OCIO 配置。

EXR／Radiance HDR 导入和 EXR 导出保留支持路径中的高光；HDR 转 16 位会裁切并量化。JPEG／PNG 为常用格式，PDF／TIFF／EXR 在“更多”中。复杂图层效果和网格变形的精度边界见 [图像手册](IMAGE-WORKSPACE.md)。

## 参考

- [Adobe：贴入或贴到选区外部](https://helpx.adobe.com/photoshop/desktop/make-selections/refine-modify-selections/paste-one-selection-into-or-outside-another.html)
- [Adobe：变换图像、形状和路径](https://helpx.adobe.com/photoshop/desktop/crop-resize-transform/resize-adjust-resolution/transform-images-shapes-and-paths.html)
- [ImageMagick Liquid Rescale](https://imagemagick.org/command-line-options/#liquid-rescale)
- [OpenColorIO 使用指南](https://opencolorio.readthedocs.io/en/stable/guides/using_ocio/using_ocio.html)
