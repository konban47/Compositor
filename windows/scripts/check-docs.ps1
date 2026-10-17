$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
function Read-Repo([string]$Path) { Get-Content -LiteralPath (Join-Path $repo $Path) -Raw -Encoding utf8 }
[xml]$project = Read-Repo 'windows/src/Compositor.Desktop/Compositor.Desktop.csproj'
$version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
if (!$version) { throw 'Missing desktop version.' }
foreach ($path in @('README.md','windows/README.md')) {
    $body = Read-Repo $path
    if (!$body.Contains("**$version Preview**")) { throw "$path does not describe $version." }
    if (!$body.Contains("Compositor-Windows-$version-x64-setup.exe")) { throw "$path installer name is stale." }
    if ($body -match '放大至 300% 后显示|300% 以上的导航器') { throw "$path still documents the old navigator gate." }
    foreach ($feature in @('属性','历史记录','蒙版','快照')) { if (!$body.Contains($feature)) { throw "$path is missing $feature." } }
}
if (!(Read-Repo 'README.md').Contains("releases/tag/windows-v$version")) { throw 'Homepage release link is stale.' }
$updates = "windows/UPDATES-$version.md"
if (!(Test-Path -LiteralPath (Join-Path $repo $updates))) { throw "Missing $updates." }
if (!(Read-Repo 'windows/scripts/package.ps1').Contains("[string]`$Version = '$version'")) { throw 'Package version is stale.' }
if (!(Read-Repo 'windows/installer/Compositor.iss').Contains("#define AppVersion `"$version`"")) { throw 'Installer version is stale.' }
$formatCode = Read-Repo 'windows/src/Compositor.Core/Format/ProjectManifest.cs'
$format = [regex]::Match($formatCode, 'const int Current = (\d+)').Groups[1].Value
$formatDoc = Read-Repo 'docs/project-format.md'
if (!$formatDoc.Contains("## Windows extension version $format")) { throw 'Current format is undocumented.' }
foreach ($field in @('maskDensity','maskFeather','maskVectorPath','bold','italic','underline','strikethrough','smallCaps','allCaps','superscript','subscript','ligatures','kerning','features','direction','complexShaping','language','dynamic','path')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented persisted field: $field" }
}
$workspace = Read-Repo 'windows/WORKSPACE.md'
if (!($workspace.Split("`n")[0]).Contains($version)) { throw 'Workspace manual version is stale.' }
foreach ($feature in @('自定义工具栏','生成式工作区','Shift+F','images/generations','DPAPI')) {
    if (!$workspace.Contains($feature)) { throw "Workspace manual is missing $feature." }
}
if ((Read-Repo 'README.md') -match '仅显示画布 \| F 进入') { throw 'Homepage still assigns F to canvas-only mode.' }
$keys = Read-Repo 'windows/src/Compositor.Core/IO/Shortcuts.cs'
if (!$keys.Contains('Menu("Full Screen with Tools", "F")') -or !$keys.Contains('Menu("Canvas Only", "F", Shift)')) { throw 'Screen mode documentation and shortcuts differ.' }
$upstream = (Read-Repo 'windows/UPSTREAM-UI.json' | ConvertFrom-Json).commit
if (!(Read-Repo $updates).Contains($upstream)) { throw 'Upstream baseline is missing.' }
$table = Join-Path $repo 'Compositor/Resources/CameraRawTables.bin'
$converted = Join-Path $repo 'windows/src/Compositor.Core/Resources/CameraRawTables.deflate'
foreach ($asset in @($table,$converted,(Join-Path $repo "Compositor/Resources/CameraRawShadows.bin"),(Join-Path $repo "windows/src/Compositor.Core/Resources/CameraRawShadows.bin"))) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLower()
    if (!(Read-Repo $updates).Contains($hash)) { throw "Camera Raw data changed without updated provenance: $asset" }
}
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--workspace-checks')) { throw 'Workspace UI checks are missing from CI.' }
foreach ($field in @('blending','items','globalLightAngle','smartObjectFile','smartObjectID','container','label','blackSplit','whiteSplit')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented v15 field: $field" }
}
$layerGuide = Read-Repo 'windows/LAYER-STYLES.md'
foreach ($feature in @('混合颜色带','智能对象','画框','画板','遮住所有对象','CSS','SVG','Ctrl+Shift+E','Ctrl+Alt+Shift+')) {
    if (!$layerGuide.Contains($feature)) { throw "Layer guide is missing $feature." }
}
if (!(Read-Repo 'windows/scripts/package.ps1').Contains('LAYER-STYLES.md')) { throw 'Layer guide missing from package.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--layer-style-checks')) { throw 'Layer style UI checks missing from CI.' }
if (!$keys.Contains('Menu("Merge Visible", "E", Ctrl | Shift)')) { throw 'Merge Visible shortcut documentation is stale.' }
$editing = Read-Repo 'windows/EDITING-TOOLS.md'
if (!$editing.Contains("**$version Preview**")) { throw 'Editing manual version is stale.' }
foreach ($feature in @('置入','跨文档','钢笔','直排','图案图章','历史记录艺术画笔','魔术橡皮擦','海绵','单色','v16')) {
    if (!$editing.Contains($feature)) { throw "Editing tool manual is missing $feature." }
}
if (!(Read-Repo 'windows/scripts/package.ps1').Contains('EDITING-TOOLS.md')) { throw 'Editing guide missing from package.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--editing-checks')) { throw 'Editing UI checks missing from CI.' }
if (!$formatDoc.Contains('`vertical`') -or !$formatDoc.Contains('`lineWidth`')) { throw 'New text/path fields undocumented.' }
$professional = Read-Repo 'windows/PROFESSIONAL-TOOLS.md'
if (!$professional.Contains("**$version Preview**")) { throw 'Professional tool guide version is stale.' }
foreach ($feature in @('Alt','磁性套索','快速选择','透视裁剪','切片','颜色取样器','注释','计数','红眼','混合器','v17','SF Symbols')) {
    if (!$professional.Contains($feature)) { throw "Professional guide is missing $feature." }
}
foreach ($field in @('marks','kind','group','url','visible')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented v17 field: $field" }
}
if (!(Read-Repo 'windows/scripts/package.ps1').Contains('PROFESSIONAL-TOOLS.md')) { throw 'Professional guide missing from package.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--professional-checks')) { throw 'Professional UI checks missing from CI.' }
if (!$keys.Contains('Canvas("Frame tool", "K")')) { throw 'Frame shortcut documentation is stale.' }
foreach ($field in @('imageSettings','floatImageFile','variables','dataSets','measurementScale','measurements','measurementFields','layout','horizontalScale','verticalScale','baselineShift','justification','smoothing')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented v18 field: $field" }
}
foreach ($guide in @('IMAGE-WORKSPACE.md','PLUGINS.md','EDIT-MENU.md')) {
    if (!(Read-Repo "windows/$guide").Contains("**$version Preview**")) { throw "$guide version is stale." }
    if (!(Read-Repo 'windows/scripts/package.ps1').Contains($guide)) { throw "$guide missing from package." }
    if (!(Read-Repo 'README.md').Contains($guide)) { throw "$guide missing from homepage." }
}
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--image-workspace-checks')) { throw 'Image workspace UI checks missing from CI.' }
foreach ($text in @('8 位 sRGB 再进入','不提供 CMYK/Lab、专色、多通道计算')) {
    if ((Read-Repo 'windows/README.md').Contains($text)) { throw "Obsolete capability statement: $text" }
}
if (!(Read-Repo 'windows/PLUGINS.md').Contains('apiVersion') -or !(Read-Repo 'windows/PLUGINS.md').Contains('32 位源')) { throw 'Plugin protocol or precision boundary is undocumented.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--edit-workspace-checks')) { throw 'Edit workspace UI checks missing from CI.' }
$editGuide = Read-Repo 'windows/EDIT-MENU.md'
foreach ($feature in @('选择性粘贴','内容识别缩放','操控变形','OpenColorIO','ICC','EXR','Windows 拼写','JSON')) {
    if (!$editGuide.Contains($feature)) { throw "Edit guide missing $feature." }
}
foreach ($guide in @('PREFERENCES.md','LAYER-MENU.md')) {
    if (!(Read-Repo "windows/$guide").Contains("**$version Preview**")) { throw "$guide version is stale." }
    if (!(Read-Repo 'windows/scripts/package.ps1').Contains($guide) -or !(Read-Repo 'README.md').Contains($guide)) { throw "$guide is missing from package/homepage." }
}
foreach ($field in @('extended','values','cube','preserveLuminosity','monochrome','absolute','referenceMean','referenceDeviation')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented v19 field: $field" }
}
$preferences = Read-Repo 'windows/PREFERENCES.md'
foreach ($feature in @('OpenCL','Skia','30 位','恢复副本','草稿','暂存盘','--preferences-checks')) {
    if (!$preferences.Contains($feature)) { throw "Preferences guide missing $feature." }
}
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--preferences-checks')) { throw 'Preferences UI checks missing from CI.' }
foreach ($field in @('styleRuns','maskVectorEvenOdd','geometry','tateChuYoko','bottomLeading','cmykProfile')) {
    if (!$formatDoc.Contains('`' + $field + '`') -and !$formatDoc.Contains('.' + $field + '`')) { throw "Undocumented v20 field: $field" }
}
if (!(Read-Repo 'windows/TYPE-AND-MASKS.md').Contains("**$version Preview**")) { throw 'Type and mask guide is stale.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--type-mask-checks')) { throw 'Type/mask UI checks missing from CI.' }
$windowGuide = Read-Repo 'windows/WINDOW-MENU.md'
if (!$windowGuide.Contains("**$version Preview**")) { throw 'Window guide is stale.' }
foreach ($feature in @('工作区','参考线','Ctrl+Esc','Skia','ATN','5 fps')) { if (!$windowGuide.Contains($feature)) { throw "Window guide missing $feature." } }
if (!$formatDoc.Contains('`artboardID`')) { throw 'Artboard guide ownership is undocumented.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--docking-checks')) { throw 'Docking checks missing from CI.' }
if (!(Read-Repo 'windows/scripts/package.ps1').Contains('WINDOW-MENU.md')) { throw 'Window guide missing from package.' }
foreach ($feature in @('各列独立','左／右边缘','柔和光晕','固定位置')) { if (!$windowGuide.Contains($feature)) { throw "Docking guide missing $feature." } }
$panelIcons = (Read-Repo 'windows/design/panel-icons/prompts.json') | ConvertFrom-Json
$glyphs = Read-Repo 'windows/src/Compositor.Desktop/DockPanelGlyph.cs'
if ($panelIcons.icons.Count -ne 35 -or !$windowGuide.Contains('35 个专属')) { throw 'Collapsed icon coverage or guide is stale.' }
foreach ($icon in $panelIcons.icons) {
    if (!$glyphs.Contains('["' + $icon.name + '"]')) { throw "Missing runtime panel icon: $($icon.name)" }
    foreach ($asset in @($icon.reference, $icon.vector)) {
        if (!(Test-Path -LiteralPath (Join-Path $repo "windows/design/panel-icons/$asset"))) { throw "Missing panel design asset: $asset" }
    }
    if (!$icon.prompt) { throw "Missing imagegen prompt: $($icon.name)" }
}
if (!$formatDoc.Contains('`artboardBackground`') -or !$windowGuide.Contains('最底部信息栏中间')) { throw 'Artboard background or embedded status scrollbar is undocumented.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--chrome-checks')) { throw 'Chrome checks missing from CI.' }
$selectionGuide = Read-Repo 'windows/SELECTION-CROP-SLICE.md'
if (!$selectionGuide.Contains("**$version Preview**")) { throw 'Selection/crop/slice guide is stale.' }
foreach ($feature in @('单行','单列','硬度','交叉','人物','云端','透视','自动切片','Alt','v23','Skia')) {
    if (!(($selectionGuide + (Read-Repo $updates)).Contains($feature))) { throw "Selection guide is missing $feature." }
}
foreach ($field in @('noImage','target','altText','background','shape.smoothing')) {
    if (!$formatDoc.Contains('`' + $field + '`')) { throw "Undocumented v23 field: $field" }
}
if (!(Read-Repo 'windows/scripts/package.ps1').Contains('SELECTION-CROP-SLICE.md') -or !(Read-Repo 'README.md').Contains('SELECTION-CROP-SLICE.md')) { throw 'Selection guide is missing from package/homepage.' }
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--selection-options-checks')) { throw 'Selection options checks missing from CI.' }
$panelWorkflow = Read-Repo $updates
foreach ($feature in @('32 DIP','同一实际快捷键','取消滚动','imagegen')) {
    if (!$panelWorkflow.Contains($feature)) { throw "Panel workflow documentation missing $feature." }
}
if (!(Read-Repo '.github/workflows/windows.yml').Contains('--panel-workflow-checks')) { throw 'Panel workflow checks missing from CI.' }
$sliceIconGuide = Read-Repo 'windows/design/slice-order-icons/README.md'
foreach ($name in @('front','forward','backward','back')) {
    $iconPath = Join-Path $repo "windows/src/Compositor.Desktop/Assets/slice-order/$name.png"
    $hash = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash.ToLower()
    if (!$sliceIconGuide.Contains($hash) -or !$sliceIconGuide.Contains("### $name")) { throw "Generated icon provenance missing: $name" }
}
& (Join-Path $PSScriptRoot 'check-upstream-ui.ps1')
Write-Output "PASS: release $version, format v$format, package names, current workflows and feature documentation agree."
