param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'),
    [string]$Version = '1.4.8.2',
    [string]$Dotnet = 'dotnet',
    [switch]$Installer
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = [IO.Path]::GetFullPath($OutputDirectory)
$name = "Compositor-Windows-$Version-x64"
$bundle = Join-Path $out $name
if (Test-Path -LiteralPath $bundle) { throw "Output already exists: $bundle. Choose an empty output directory." }
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
& $Dotnet publish "$repo\windows\src\Compositor.Desktop" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $bundle --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
& $Dotnet publish "$repo\windows\src\Compositor.Cli" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "$bundle\cli" --nologo
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
Copy-Item -LiteralPath "$repo\LICENSE" -Destination "$bundle\LICENSE.txt"
Copy-Item -LiteralPath "$repo\windows\README.md" -Destination "$bundle\README.md"
Copy-Item -LiteralPath "$repo\windows\THIRD-PARTY-NOTICES.md", "$repo\windows\RESEARCH.md", "$repo\windows\UPDATES-1.4.7.1.md", "$repo\windows\UPDATES-1.4.8.1.md", "$repo\windows\UPDATES-1.4.8.2.md" -Destination $bundle
Copy-Item -LiteralPath "$repo\windows\licenses" -Destination $bundle -Recurse
New-Item -ItemType Directory -Path "$bundle\redist" -Force | Out-Null
$runtime = "$bundle\redist\vc_redist.x64.exe"
Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $runtime
$signature = Get-AuthenticodeSignature -LiteralPath $runtime
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'The Visual C++ redistributable does not have a valid Microsoft signature.'
}
# No prerequisites are installed on the build computer. The end-user installer checks them.
Get-FileHash -LiteralPath $runtime -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  redist/vc_redist.x64.exe" } |
    Set-Content -LiteralPath "$bundle\REDIST-SHA256.txt" -Encoding utf8
$zip = Join-Path $out "$name-portable.zip"
Compress-Archive -LiteralPath $bundle -DestinationPath $zip -CompressionLevel Optimal
if ($Installer) {
    $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (!(Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 is required for -Installer. GitHub Actions provides it.' }
    & $compiler "/DSourceDir=$bundle" "/DOutputDir=$out" "/DAppVersion=$Version" "$repo\windows\installer\Compositor.iss"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
Get-ChildItem -LiteralPath $out -File | Where-Object { $_.Extension -in '.zip', '.exe' } |
    Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Packaged $name in $out"
