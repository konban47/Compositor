param([string]$Dotnet = 'dotnet', [string]$Output = 'artifacts/performance')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../tests/Compositor.Performance/Compositor.Performance.csproj'
$dll = Join-Path $PSScriptRoot '../tests/Compositor.Performance/bin/Release/net10.0/Compositor.Performance.dll'
$env:COMPOSITOR_PERFORMANCE_DIR = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory $env:COMPOSITOR_PERFORMANCE_DIR -Force | Out-Null
& $Dotnet build $project -c Release -warnaserror --nologo
if ($LASTEXITCODE -ne 0) { throw 'Performance probe build failed.' }
function Probe([string]$Name, [int]$Width = 4000) {
    $json = & $Dotnet $dll $Name $Width
    if ($LASTEXITCODE -ne 0) { throw "Performance probe failed: $Name" }
    $json | Set-Content -LiteralPath (Join-Path $env:COMPOSITOR_PERFORMANCE_DIR "$Name-$Width.json") -Encoding utf8
    return ($json | ConvertFrom-Json)
}
& $Dotnet $dll prepare 4000
if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture generation failed.' }
$records = @()
foreach ($name in @('preview', 'preview-hidden', 'render-fit', 'render-tile', 'selection', 'decode-png', 'decode-tiff')) {
    $records += Probe $name
}
$records += Probe 'blur-8' 1024
$records += Probe 'blur-9' 1024
$viewport = Probe 'viewport-cache'
$correct = Probe 'verify'
$history = Probe 'history-selection'
$export = Probe 'export-failure'
$selection = $records | Where-Object Kind -eq 'selection'
$tiff = $records | Where-Object Kind -eq 'decode-tiff'
$hidden = $records | Where-Object Kind -eq 'preview-hidden'
$failures = @()
if (!$viewport.PixelsEqual -or $viewport.Hits -le 0 -or $viewport.WarmMedianMs -gt $viewport.UncachedMedianMs) { $failures += "Viewport cache pixel parity or reuse regressed." }
if (($selection.Samples.ManagedAllocatedMiB | Measure-Object -Maximum).Maximum -gt 2) { $failures += 'Small selection allocated more than 2 MiB.' }
if (($selection.Samples.Ms | Sort-Object)[2] -gt 1000) { $failures += 'Small selection median exceeded 1 second.' }
if (($tiff.Samples.ManagedAllocatedMiB | Measure-Object -Maximum).Maximum -gt 64) { $failures += '8-bit TIFF conversion allocated more than 64 MiB managed memory.' }
if (-not $hidden.DisplaySceneSupported -or -not $correct.PreviewPixelsEqualWithHiddenAdjustment) { $failures += 'Hidden layer acceleration changed pixels or fell back.' }
if ($correct.MaxChannelDelta -gt 1) { $failures += 'PNG/TIFF pixel parity exceeded one level.' }
if (-not $export.OriginalPreserved) { $failures += 'Failed export replaced the existing file.' }
if ($history.ActualRetainedCoverageBytes -gt $history.RetainedByteLimit -or $history.ReportedRetainedBytes -lt $history.ActualRetainedCoverageBytes) { $failures += 'History exceeded or underreported the selection budget.' }
$summary = [ordered]@{ TimestampUtc = [DateTime]::UtcNow.ToString('o'); ProcessorCount = [Environment]::ProcessorCount;
    Runtime = [Environment]::Version.ToString(); Failures = $failures; Cases = $records; Correctness = $correct; History = $history; Export = $export; Viewport = $viewport }
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $env:COMPOSITOR_PERFORMANCE_DIR 'summary.json') -Encoding utf8
if ($failures.Count) { throw ($failures -join "`n") }
Write-Output 'PASS: synthetic performance, allocation, pixel parity, history and export safety gates.'
