# 本地插件接口（API 1）

适用于 Windows **1.5.12.3 Preview**。“插件 → 增效工具面板”打开右侧插件标签；面板三线菜单提供管理插件、关闭及关闭选项卡组。“管理增效工具”可安装、启用／停用、删除和刷新。关闭标签不会卸载插件。

## 安装与使用

1. 将插件解压到独立文件夹，包含 `compositor-plugin.json`、Windows `.exe` 和其运行依赖。
2. 在管理窗口选择该 JSON，应用复制整个插件文件夹到 `%LOCALAPPDATA%/Compositor/Plugins/<id>`。设置了 `COMPOSITOR_SETTINGS_DIR` 时改用其 `Plugins` 子目录。
3. 新装插件默认停用。选中并启用后，面板显示命令按钮；打开图像再执行。
4. `layer` 结果增加图层，`replace` 结果替换当前可绘制图层并遵循选区／锁定／通道，`message` 显示文本。操作支持撤销。等待期间切换文档或修改文档，会丢弃过期结果。

API 1 是本项目的独立进程协议，不兼容 Photoshop UXP、CEP、8BF 或 Adobe 插件市场。宿主校验路径、格式与大小，但**插件是本机可执行程序，不是操作系统沙箱**，具有当前用户的权限。仅安装可信来源程序。浏览列表不会执行代码，启用也不会自动启动。

## 清单

```json
{
  "apiVersion": 1,
  "id": "vendor.example",
  "name": "Example",
  "version": "1.0.0",
  "entry": "Example.exe",
  "author": "Vendor",
  "description": "Adds a processed copy of the active image",
  "commands": [{ "id": "process", "title": "Process image", "result": "layer" }]
}
```

ID 必须以英文字母开头，之后允许字母、数字、点、下划线和短横线，最长 128。最多 64 个命令，ID 不区分大小写且不能重复。`result` 必须为 `layer`、`replace` 或 `message`。可执行路径相对清单目录，不允许越界、绝对路径或符号链接。清单上限 256 KiB；安装包最多 2,048 文件、512 MiB。重复 ID 需先卸载原版本。

## 请求与响应

宿主隐藏启动进程（不经 shell），在 stdin 写入**一行 UTF-8 JSON** 后关闭输入：

```json
{"apiVersion":1,"command":"process","input":"C:/Temp/private/input.png","outputDirectory":"C:/Temp/private/output","width":640,"height":480,"layerID":"00000000-0000-0000-0000-000000000000","language":"zh-CN","bitDepth":16}
```

`input` 是所选层的原始像素 PNG，不含图层变换；未选择像素层时是合成图像，`layerID` 可为 null。PNG 为 8 或 16 位，32 位源在 API 1 中转换为 16 位，不能无损传输 HDR；合成输入为显示精度。`width`／`height` 是输入 PNG 尺寸。

程序只向 stdout 输出一个响应对象，其余诊断写 stderr；成功退出码为 0：

```json
{"apiVersion":1,"image":"processed.png","name":"Processed layer","message":"Done"}
```

`image` 必须是 `outputDirectory` 下已写完的相对 PNG 路径。`replace` 输出尺寸必须与原层相同；`layer` 可以改变尺寸。`message` 命令可以省略 image。`name` 最长 1,024 字符，`message` 最长 8,192。stdout 和 stderr 各上限 64 KiB；超量、非零退出、错误格式、路径越界或 2 分钟超时均失败。图片须符合应用像素／资源预算。完成后删除本次私有输入输出目录，不向插件传递原项目路径或生成服务密钥。

## 可编译示例

[示例工程](examples/Compositor.SamplePlugin) 包含复制当前图像和读取尺寸两个真实命令，无须引用宿主程序集：

```powershell
dotnet publish windows/examples/Compositor.SamplePlugin -c Release -r win-x64 --self-contained true -o plugin-example
```

在管理窗口安装 `plugin-example/compositor-plugin.json`，启用并运行。核心测试会实际启动该示例，验证安装默认禁用、启停、图像与文本响应、重复安装、未知命令、路径约束与移除。

API 1 不支持常驻嵌入式插件界面、网络插件商店、项目树直接写入或自动升级；未来协议通过 `apiVersion` 演进，宿主拒绝未知版本。
