$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$manifest = Get-Content -LiteralPath (Join-Path $repo 'windows/UPSTREAM-UI.json') -Raw -Encoding utf8 | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $file.path)).Replace("`r`n", "`n")
    $bytes = [Text.Encoding]::UTF8.GetBytes($source)
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    if ($digest -ne $file.sha256) { throw "Upstream UI source changed: $($file.path). Review its Windows port and update provenance." }
}
$ported = Get-Content -LiteralPath (Join-Path $repo $manifest.implementation) -Raw -Encoding utf8
foreach ($token in @('Clone','Gradient','Polygon','Object','ButtonSide = 36','ButtonRadius = 7','RailWidth = 56','RailSpacing = 10')) {
    if (!$ported.Contains($token)) { throw "Upstream artwork mapping missing: $token" }
}
Write-Output "PASS: four direct icon ports and button metrics track upstream $($manifest.commit)."
