param(
    [string]$Version = "3.0.0",
    [string]$Guid    = "b7c8d9e0-f1a2-3b4c-5d6e-7f8090a1b2c3",
    [string]$Owner   = "KOOL4"
)

$ErrorActionPreference = "Stop"
$Root   = $PSScriptRoot
$Proj   = Join-Path $Root "src\JellyFusion\JellyFusion.csproj"
$Pub    = Join-Path $Root "publish"
$Rel    = Join-Path $Root "releases"
$ZipOut = Join-Path $Rel  ("JellyFusion-v" + $Version + ".zip")
$Meta   = Join-Path $Pub  "meta.json"

Write-Host "==> Cleaning previous build"
if (Test-Path $Pub) { Remove-Item $Pub -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Pub | Out-Null
New-Item -ItemType Directory -Force -Path $Rel | Out-Null

Write-Host "==> Publishing $Version"
dotnet publish $Proj `
    --configuration Release `
    --output $Pub `
    "-p:Version=$Version" `
    "-p:AssemblyVersion=$Version.0" `
    "-p:FileVersion=$Version.0"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "==> Publish folder contents:"
Get-ChildItem $Pub | Format-Table Name, Length

$extraDlls = Get-ChildItem $Pub -Filter *.dll | Where-Object { $_.Name -ne "JellyFusion.dll" }
if ($extraDlls) {
    Write-Warning "Extra DLLs in publish output (should not happen):"
    foreach ($d in $extraDlls) { Write-Warning ("  - " + $d.Name) }
}

Write-Host "==> Writing meta.json"
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$metaObj = [ordered]@{
    category    = "General"
    changelog   = "Release $Version - see manifest.json for full changelog"
    description = "All-in-one Jellyfin plugin: Netflix-style banner with trailers, smart badges (LAT/SUB/NUEVO/KID), configurable studios, home rails (Top 10), 7 themes, navigation shortcuts, Discord/Telegram notifications and a 4-language UI (ES/EN/PT/FR)."
    guid        = $Guid
    imagePath   = ""
    name        = "JellyFusion"
    overview    = "Reemplaza 4-5 plugins individuales con una sola instalacion."
    owner       = $Owner
    targetAbi   = "10.10.0.0"
    timestamp   = $timestamp
    version     = ($Version + ".0")
}
$metaObj | ConvertTo-Json -Depth 4 | Set-Content -Path $Meta -Encoding utf8

Write-Host "==> Creating ZIP"
if (Test-Path $ZipOut) { Remove-Item $ZipOut -Force }
$dllPath = Join-Path $Pub "JellyFusion.dll"
Compress-Archive -Path $dllPath, $Meta -DestinationPath $ZipOut -Force

Write-Host "==> Computing MD5"
$md5 = (Get-FileHash -Algorithm MD5 -Path $ZipOut).Hash.ToLower()
Write-Host ("    MD5: " + $md5)

Write-Host "==> Updating manifest.json"
$manifestPath = Join-Path $Root "manifest.json"
$manifestRaw = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
# Force array: Jellyfin manifest MUST be a JSON array at root.
$manifest = @($manifestRaw)
$targetVersion = $Version + ".0"
$ver = $manifest[0].versions | Where-Object { $_.version -eq $targetVersion } | Select-Object -First 1
if ($ver) {
    $ver.checksum  = $md5
    # Keep strict ISO 8601 - NEVER strip T or Z.
    $ver.timestamp = $timestamp
    Write-Host "    Updated existing entry for $targetVersion"
} else {
    Write-Warning "manifest.json has no entry for version $targetVersion - leaving it untouched."
}
# Preserve the array wrapper. -AsArray is PS 7+; fall back to manual wrap for PS 5.
try {
    $json = $manifest | ConvertTo-Json -Depth 10 -AsArray
} catch {
    $inner = $manifest | ConvertTo-Json -Depth 10
    if ($inner.TrimStart().StartsWith("[")) { $json = $inner }
    else { $json = "[`n" + $inner + "`n]" }
}
$json | Set-Content $manifestPath -Encoding utf8

Write-Host ""
Write-Host "==> DONE"
Write-Host ("    ZIP:      " + $ZipOut)
Write-Host ("    Size:     " + (Get-Item $ZipOut).Length + " bytes")
Write-Host ("    MD5:      " + $md5)
Write-Host ("    Manifest: " + $manifestPath)

