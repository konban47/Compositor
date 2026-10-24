param([string]$Destination = (Join-Path $PSScriptRoot '../runtime/color'))
$ErrorActionPreference = 'Stop'
# Nested Windows PowerShell can inherit a PowerShell 7 module path on CI.
# Use framework APIs for hashing/extraction rather than optional script modules.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function ArchiveHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function ExtractArchive([string]$Path, [string]$DestinationPath) {
    $root = [IO.Path]::GetFullPath($DestinationPath).TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $entry.FullName))
            if (!$target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe runtime archive path.' }
            if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            $inputStream = $entry.Open()
            try {
                $outputStream = [IO.File]::Create($target)
                try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            } finally { $inputStream.Dispose() }
        }
    } finally { $archive.Dispose() }
}
$destinationPath = [IO.Path]::GetFullPath($Destination)
$stamp = Join-Path $destinationPath 'compositor-runtime-2.4.2.txt'
if (Test-Path -LiteralPath $stamp) { return }
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$downloads = @(
    @('python.zip', 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip', '4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3'),
    @('ocio.zip', 'https://files.pythonhosted.org/packages/e8/6d/d8c9e1c88981caf29b374ba28c141007d04be399878b2c69e22c367f9e8b/opencolorio-2.4.2-cp312-cp312-win_amd64.whl', '120bfb83830f319d2da5b297e835a095c5f615bbc641522db5f8edce78d58aaf'),
    @('numpy.zip', 'https://files.pythonhosted.org/packages/36/fa/8c9210162ca1b88529ab76b41ba02d433fd54fecaf6feb70ef9f124683f1/numpy-2.2.6-cp312-cp312-win_amd64.whl', 'c1f9540be57940698ed329904db803cf7a402f3fc200bfe599334c9bd84a40b2')
)
$cache = Join-Path $PSScriptRoot '../runtime/downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
foreach ($download in $downloads) {
    $archive = Join-Path $cache $download[0]
    if (!(Test-Path -LiteralPath $archive) -or (ArchiveHash $archive) -ne $download[2]) {
        Invoke-WebRequest $download[1] -OutFile $archive -UseBasicParsing -TimeoutSec 180
    }
    if ((ArchiveHash $archive) -ne $download[2]) { throw "Color runtime checksum mismatch: $($download[0])" }
    ExtractArchive $archive $destinationPath
}
# Isolated embedded interpreter: neither user site-packages nor PATH are searched.
[IO.File]::WriteAllText((Join-Path $destinationPath 'python312._pth'), "python312.zip`n.`n")
& (Join-Path $destinationPath 'python.exe') -I -c "import PyOpenColorIO as o, numpy; assert o.__version__ == '2.4.2'"
if ($LASTEXITCODE -ne 0) { throw 'Bundled OpenColorIO runtime validation failed.' }
[IO.File]::WriteAllText($stamp, 'CPython 3.12.10; OpenColorIO 2.4.2; NumPy 2.2.6')
