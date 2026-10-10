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
Write-Output "PASS: release $version, format v$format, package names, current workflows and feature documentation agree."
