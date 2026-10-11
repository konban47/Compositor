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
if (!(Read-Repo $updates).Contains('b4bfdea87f9dc0cbc9eabfa63683eb1b9c2bca60')) { throw 'Upstream baseline is missing.' }
$table = Join-Path $repo 'Compositor/Resources/CameraRawTables.bin'
$converted = Join-Path $repo 'windows/src/Compositor.Core/Resources/CameraRawTables.deflate'
foreach ($asset in @($table,$converted)) {
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
& (Join-Path $PSScriptRoot 'check-upstream-ui.ps1')
Write-Output "PASS: release $version, format v$format, package names, current workflows and feature documentation agree."
