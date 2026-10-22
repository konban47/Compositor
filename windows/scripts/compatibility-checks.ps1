param(
    [string]$Dotnet = 'dotnet',
    [string]$App = '',
    [string]$Output = 'artifacts/compatibility',
    [ValidateSet(512,1024)][int]$MemoryMiB = 512,
    [switch]$Scalar,
    [switch]$Native,
    [switch]$RequireWindows10,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$out = [IO.Path]::GetFullPath($Output)
$os = Get-CimInstance Win32_OperatingSystem
$isWindows10 = $os.ProductType -eq 1 -and [int]$os.BuildNumber -ge 17763 -and [int]$os.BuildNumber -lt 22000
if ($RequireWindows10 -and !$isWindows10) { throw "Windows 10 client required; actual host is $($os.Caption) build $($os.BuildNumber)." }
if (Test-Path (Join-Path $out 'summary.json')) { throw 'Choose a new output folder to preserve earlier evidence.' }
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dotnetExe = (Get-Command $Dotnet).Source
$dll = Join-Path $repo 'windows/src/Compositor.Desktop/bin/Release/net10.0/Compositor.dll'
$probe = Join-Path $repo 'windows/tests/Compositor.Performance/bin/Release/net10.0/Compositor.Performance.dll'
$saved = @{}
$variables = @('COMPOSITOR_SETTINGS_DIR','COMPOSITOR_LANGUAGE','COMPOSITOR_MEMORY_MB','COMPOSITOR_PERFORMANCE_DIR','DOTNET_PROCESSOR_COUNT','DOTNET_EnableHWIntrinsic')
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$checks = [Collections.Generic.List[object]]::new()
function Run-Check([string]$Name, [string]$Exe, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WorkingDirectory = $repo; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        # Pick two permitted logical processors, rather than assuming CPU 0 is available.
        $allowed = $process.ProcessorAffinity.ToInt64(); [long]$mask = 0; $count = 0
        for ($bit = 0; $bit -lt 63 -and $count -lt 2; $bit++) {
            $value = [long]1 -shl $bit
            if (($allowed -band $value) -ne 0) { $mask = $mask -bor $value; $count++ }
        }
        $process.ProcessorAffinity = [IntPtr]$mask
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(300000)) { $process.Kill($true); throw "$Name timed out after 5 minutes." }
        [IO.File]::WriteAllText((Join-Path $out "$Name.log"), $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
        $checks.Add([ordered]@{ Name = $Name; ExitCode = $process.ExitCode; Affinity = $mask; LogicalProcessors = $count })
        if ($process.ExitCode -ne 0) { throw "$Name failed; see $out/$Name.log" }
    } finally { $process.Dispose() }
}
$failure = $null
try {
    if (!$SkipBuild) {
        & $dotnetExe build (Join-Path $repo 'windows/Compositor.slnx') -c Release -warnaserror --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    $env:COMPOSITOR_MEMORY_MB = "$MemoryMiB"; $env:DOTNET_PROCESSOR_COUNT = '2'
    $env:DOTNET_EnableHWIntrinsic = $(if ($Scalar) { '0' } else { '1' })
    $env:COMPOSITOR_SETTINGS_DIR = Join-Path $out 'isolated-settings'
    $env:COMPOSITOR_PERFORMANCE_DIR = $out
    New-Item -ItemType Directory -Force -Path $env:COMPOSITOR_SETTINGS_DIR | Out-Null
    [IO.File]::WriteAllText((Join-Path $env:COMPOSITOR_SETTINGS_DIR 'preferences.json'), '{"UseGpu":false,"AcceleratedCanvas":false}')
    Run-Check 'complex-viewport' $dotnetExe @($probe, 'viewport-cache', '1600')
    $viewport = Get-Content (Join-Path $out 'complex-viewport.log') -Raw | ConvertFrom-Json
    if (!$viewport.PixelsEqual -or !$viewport.EditedPixelsEqual -or $viewport.EditTileHits -le 0 -or $viewport.ProcessorCount -ne 2 -or ($Scalar -and $viewport.Simd)) { throw 'Resource profile or pixel parity not applied.' }
    Run-Check 'core-safety' $dotnetExe @('test', (Join-Path $repo 'windows/tests/Compositor.Core.Tests'), '-c', 'Release', '--no-build', '--filter', 'FullyQualifiedName~ViewportCacheTests|FullyQualifiedName~Upstream155Tests|FullyQualifiedName~AdvancedRepairTests|FullyQualifiedName~UtilityRetouchTests|FullyQualifiedName~PerformanceSafetyTests|FullyQualifiedName~PerformanceResourceTests')
    foreach ($language in @('en','zh-CN')) {
        $env:COMPOSITOR_LANGUAGE = $language
        foreach ($check in @('windows-checks','interaction-checks','pen-layer-checks','utility-retouch-checks')) {
            $arguments = @("--$check", (Join-Path $out "$language-$check.png"))
            if ($App) { Run-Check "$language-$check" (Resolve-Path $App).Path $arguments }
            else { Run-Check "$language-$check" $dotnetExe (@($dll) + $arguments) }
        }
    }
    if ($Native) {
        $arguments = @('--performance-checks', (Join-Path $out 'complex-viewport.comp'), (Join-Path $out 'native-cpu.json'))
        if ($App) { Run-Check 'native-cpu' (Resolve-Path $App).Path $arguments }
        else { Run-Check 'native-cpu' $dotnetExe (@($dll) + $arguments) }
        $nativeReport = Get-Content (Join-Path $out 'native-cpu.json') -Raw | ConvertFrom-Json
        if (!$nativeReport.Success -or $nativeReport.Gpu -or $nativeReport.AcceleratedScene -or $nativeReport.Frames -ne 60) { throw 'Native software-renderer gate failed.' }
    }
} catch { $failure = $_.Exception.Message }
finally {
    $report = [ordered]@{ TimestampUtc = [DateTime]::UtcNow.ToString('o'); Success = !$failure; Error = $failure
        OS = $os.Caption; Build = $os.BuildNumber; ProductType = $os.ProductType; ActualWindows10Client = $isWindows10
        Validation = $(if ($isWindows10) { 'Windows 10 automated checks; manual checklist still required' } else { 'Host resource simulation; NOT Windows 10 validation' })
        PhysicalMemoryMiB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1MB)
        MemoryAdmissionBudgetMiB = $MemoryMiB; Scalar = [bool]$Scalar; NativeWindowTest = [bool]$Native; Checks = $checks }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'summary.json') -Encoding utf8
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
if ($failure) { throw $failure }
Write-Output "PASS: $($report.Validation); evidence in $out"
