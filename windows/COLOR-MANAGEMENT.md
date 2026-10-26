# 颜色管理 · Windows **1.5.10.0 Preview**

“编辑 → 颜色设置”采用工作空间、色彩管理方案、转换选项、高级控制和底部说明的布局。“编辑 → OpenColorIO 设置”采用启用、根配置文件夹、默认配置、默认设置的布局。界面使用项目字体、控件和主题；浮窗按钮保持原样。没有 Creative Cloud 同步、账号或网络上传功能。

## ICC 与 ACE 兼容引擎

默认的 **ACE 兼容（独立实现）** 是本仓库编写的 ICC CMM，代码位于 `IO/AceCompatibleEngine.cs`。它不是 Adobe ACE 二进制，也没有 Adobe 专有源码；没有将 Little CMS 改名。支持 RGB 矩阵/TRC、灰度曲线、ICC v2/v4 的 mft1、mft2、mAB、mBA 查找表、XYZ/Lab PCS、四种意图和黑场补偿。RGB 三维 CLUT 使用四面体插值，四通道 CLUT 使用多线性插值。未实现 MPE/D2B/B2D、设备链接和专有色域映射；不支持的配置明确报错。数值回归以 ICC 公式和 Little CMS 为参考，不能据此声称与 Adobe ACE 逐位一致。

另外可选 Little CMS（ImageMagick）或 Windows Microsoft ICM。ICM 的此接口以 16 位通道转换，没有可切换的黑场补偿参数，因此该选项在 ICM 下置灰。独立引擎使用浮点计算，但 ICC 输入/输出限定在 SDR 0–1；HDR 及负值用 OpenColorIO。透明度不参与颜色变换。

工作空间分别选择 RGB、CMYK、灰度及专色预览曲线。列表读取内置配置、Windows/Adobe 已安装 ICC 和用户载入的 ICC/ICM；不会把本机 Adobe 配置文件复制到发行包。地区预设需要相应印刷 ICC，缺少时报告其名称，不悄悄替换成另一印刷条件。自定网点增大支持 13 个节点和曲线预览，自定灰度系数会生成可导出的有效灰度 ICC。专色选项用于现有双色调的墨色覆盖预览，尚无原生专色印版/分色通道。设置以 Compositor `.color-settings.json` 载入/存储；不宣称读取 Adobe CSF。

| 选项 | 实际行为 |
| --- | --- |
| 保留嵌入配置 | 用源 ICC 转到内部 sRGB，文档保留源输出配置。 |
| 转换为工作空间 | 经选定工作 ICC 转换，再归一到内部 sRGB，文档保留工作 ICC。 |
| 关 | 忽略嵌入 ICC，按所选工作空间解释数值。 |
| 缺失/不匹配询问 | 导入/作为图层置入时按相应开关提示；拒绝则不导入。内部剪贴板像素已是 sRGB，没有第二次源 ICC 转换。 |
| 仿色 | 管理后的 8 位导入使用确定性半码值抖动，16/32 位不加该抖动。 |
| 场景参考补偿 | 标注为 scene、HDTV (Rec. 709)、SDTV 的 RGB ICC 采用独立的线性亮度 1.2 次方补偿，反向用倒数；不承诺 Adobe 私有曲线一致。OCIO 场景导入则使用配置内 sRGB Display 的默认 View，而非该近似。 |
| 显示器降低饱和度 | 只改变画布预览，不改变像素、保存或导出。 |
| RGB/文本混合灰度系数 | 普通图层/文本的 Normal source-over 在指定幂律空间合成，保留 alpha；2.2 对应原编码混合，1.0 接近线性混合。其他混合模式保留既有算法。 |

“预览”即时改变显示和混合；取消/关闭恢复打开前设置，确定后保存。PNG/JPEG/TIFF/HEIC 等静态图像导入经过 ICC 方案；SVG、GIF、RAW、PDF、PSD 各自已有解码流程，不假定为任意工作 RGB。ICC 指定到图层是解释该层 RGB 数值并烘焙转换，可撤销。“转换为配置文件”将合成结果导出为嵌入 ICC 的 16/32 位 TIFF。普通 PNG/JPEG/TIFF 导出也使用文档保存的适用输出 ICC；CMYK 校样和 CMYK JPEG/TIFF/PDF 导出使用选择的引擎。大画布分块 PNG 保持原 sRGB 输出。

## OpenColorIO

随包提供官方 OpenColorIO **2.4.2**、隔离的 CPython **3.12.10** 和 NumPy **2.2.6**。不需要用户安装 Python 或 `ocioconvert`。构建脚本 `ensure-color-runtime.ps1` 下载固定版本并核验 SHA-256；运行时不下载程序，不搜索用户 Python 包。许可证见 `licenses/` 和 `color-runtime/*dist-info/`。

默认配置是 `ocio://cg-config-v2.2.0_aces-v1.3_ocio-v2.4`。根文件夹用于列出 `.ocio` 配置，也可直接载入文件；清除只取消根路径，不删除文件。未允许覆盖时，`OCIO` 环境变量优先。下拉项、角色、Display/ACES/Utility 分类、编码和说明均来自实际 OCIO 配置；确认前验证工作空间与编辑 sRGB 的正反处理器。自定义配置需要明确的 sRGB 空间或 `color_picking` 角色。

开启后，导入 EXR/HDR 将数值按选定工作空间解释，并通过 OCIO 转到扩展 sRGB。该默认值是用户对输入的指定，不会自动猜测相机/ACES 空间。文档深度选项用于新建文档；既有文档不会被设置窗口悄悄量化。新文档和导入记录保存配置路径/URI及工作空间，重开项目直接使用已归一的像素，避免重复转换。外部配置及其 LUT 应与工作文件一起保管。

“编辑 → OpenColorIO 转换”可明确选择输入/输出空间：应用到选中图层可撤销；浮点 TIFF 导出保留输出数值，alpha 单独保存。正常编辑的输出应选 sRGB 编码空间；任意 log/线性输出用于交换时选择导出。场景补偿使用配置的默认 sRGB 显示 View，可能包含色调映射，不保证可逆。普通空间变换保留 HDR/负值，分块处理，超时/错误不写入图层。

## 保存与验证

ICC 输出字节、OCIO 配置/空间作为文档 v28 元数据保存；图层内部始终为 sRGB/扩展 sRGB。工具设置 JSON 和颜色设置不冒充 Adobe 文件格式。已有文档继续按所需最低格式版本保存。

新增回归覆盖四种意图、RGB/CMYK/灰度、v2/v4 LUT、alpha、黑场补偿、三引擎、自定曲线、设置导入校验、工作空间策略、文档/导出 ICC 保留、伽马合成和真实 OCIO HDR 往返/显示变换。`--color-checks` 在中英文运行，检查引擎切换、预览取消、持久化、灰度 ICC 生成及配置验证。

参考：[ICC 规范](https://archive.color.org/specification/ICC.1-2022-05.pdf)、[ICC 黑场补偿](https://www.color.org/WP40-Black_Point_Compensation_2010-07-27.pdf)、[Windows ICM](https://learn.microsoft.com/en-us/windows/win32/api/icm/nf-icm-createmultiprofiletransform)、[OpenColorIO 处理器](https://opencolorio.readthedocs.io/en/v2.4.2/api/processors.html)、[Adobe 颜色转换选项](https://helpx.adobe.com/photoshop/desktop/adjust-color/color-effects-techniques/color-conversion-and-advanced-color-controls.html)。

## 指定配置文件（1.5.10.0）

窗口提供“不对此文档应用色彩管理”“工作中的 RGB/灰度/CMYK”“配置文件”三种选择，预览可开关；取消恢复原解释，确定产生可撤销操作。配置文件列表按文档颜色模型过滤，工作空间读取颜色设置。指定时保留图层像素，以 `profileEncoding` 记录之前编码，再按新 ICC 解释最终合成结果；保存重开不会重复改写像素。该路径不等同于在 Adobe 原生通道空间重新执行每层混合。转换为配置文件仍是单独的输出转换功能。Creative Cloud 同步入口与状态均不提供。
