# ---------------------------------------------------------------
#  JellyFusion V3 release push - force push + auto-verify
#
#  Run from the repo root:
#      .\push.ps1
#
#  Credentials prompt:
#      User:     KOOL4
#      Password: Personal Access Token (classic, scope 'repo')
#                https://github.com/settings/tokens
# ---------------------------------------------------------------

$ErrorActionPreference = 'Stop'

$repoUrl  = 'https://github.com/KOOL4/JellyFusionV3.git'
$rawUrl   = 'https://raw.githubusercontent.com/KOOL4/JellyFusionV3/main/manifest.json'
$userName = 'KOOL4'
$userMail = 'jose13tony13@gmail.com'
$projPath = Join-Path $PSScriptRoot 'src\JellyFusion\JellyFusion.csproj'

[xml]$projXml = Get-Content -LiteralPath $projPath -Raw
$packageVersion = $projXml.Project.PropertyGroup.Version
$manifestVersion = $projXml.Project.PropertyGroup.AssemblyVersion
if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    throw "Could not read <Version> from $projPath"
}
if ([string]::IsNullOrWhiteSpace($manifestVersion)) {
    $parts = $packageVersion.Split('.')
    $manifestVersion = if ($parts.Count -eq 3) { "$packageVersion.0" } else { $packageVersion }
}
$tag = "v$packageVersion"

function Invoke-Git {
    [CmdletBinding(PositionalBinding=$false)]
    param(
        [Parameter(Position=0, ValueFromRemainingArguments=$true)]
        [string[]]$GitArgs,
        [switch]$IgnoreError
    )

    if (-not $GitArgs) { $GitArgs = @() }

    $oldEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $code = $null
    try {
        & git @GitArgs 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                Write-Host $_.Exception.Message -ForegroundColor DarkGray
            } else {
                Write-Host $_
            }
        }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEAP
    }

    if ($code -ne 0 -and -not $IgnoreError) {
        throw "git $($GitArgs -join ' ') failed with exit code $code"
    }
    return $code
}

Write-Host '==> Cleaning previous .git (if any)' -ForegroundColor Cyan
if (Test-Path .\.git) { Remove-Item -Recurse -Force .\.git }

Write-Host '==> git init' -ForegroundColor Cyan
Invoke-Git init -b main | Out-Null
Invoke-Git config user.name  $userName | Out-Null
Invoke-Git config user.email $userMail | Out-Null

Write-Host '==> Staging files' -ForegroundColor Cyan
Invoke-Git add -A | Out-Null
$staged = (& git ls-files --cached | Measure-Object).Count
Write-Host "    $staged files staged"

Write-Host '==> Commit' -ForegroundColor Cyan
Invoke-Git commit -m "chore: prepare JellyFusion $packageVersion release" | Out-Null

$localSha = (& git rev-parse HEAD).Trim()
Write-Host "    Commit SHA: $localSha"

Write-Host "==> Tag $tag (local)" -ForegroundColor Cyan
Invoke-Git tag -f -a $tag -m "JellyFusion $packageVersion" | Out-Null

Write-Host '==> Adding remote' -ForegroundColor Cyan
Invoke-Git remote add origin $repoUrl | Out-Null

Write-Host '==> Force-pushing main to GitHub' -ForegroundColor Yellow
Invoke-Git push -u --force origin main | Out-Null

Write-Host "==> Deleting remote tag $tag (if exists)" -ForegroundColor Yellow
Invoke-Git push origin --delete $tag -IgnoreError | Out-Null

Write-Host "==> Pushing tag $tag" -ForegroundColor Yellow
Invoke-Git push origin $tag | Out-Null

Write-Host ''
Write-Host '==> Verifying remote main matches local commit' -ForegroundColor Cyan
$remoteLine = (& git ls-remote origin main)
$remoteSha  = ($remoteLine -split '\s+')[0]
Write-Host "    Local  SHA : $localSha"
Write-Host "    Remote SHA : $remoteSha"
if ($remoteSha -ne $localSha) {
    throw "REMOTE MISMATCH - push did not land on origin/main"
}
Write-Host '    OK - remote main matches local commit' -ForegroundColor Green

Write-Host ''
Write-Host '==> Waiting 45s for raw.githubusercontent.com cache to refresh...' -ForegroundColor Cyan
Start-Sleep -Seconds 45

Write-Host "==> Fetching $rawUrl" -ForegroundColor Cyan
$ok = $false
try {
    $raw  = Invoke-WebRequest -UseBasicParsing -Uri $rawUrl -Headers @{ 'Cache-Control' = 'no-cache' } -ErrorAction Stop
    $content = $raw.Content
    if ($content -and $content.Length -gt 0) {
        $content = $content.TrimStart([char]0xFEFF).TrimStart()
    }
    $json = $content | ConvertFrom-Json
    $versions = @($json)[0].versions | ForEach-Object { $_.version }
    Write-Host "    Versions in remote manifest: $($versions -join ', ')"
    if ($versions -contains $manifestVersion) { $ok = $true }
} catch {
    Write-Warning "Fetch failed: $_"
}

Write-Host ''
if ($ok) {
    Write-Host "==> SUCCESS - $tag is LIVE on GitHub" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Add this URL to Jellyfin > Dashboard > Repositories:' -ForegroundColor Yellow
    Write-Host "    $rawUrl"
    Write-Host ''
    Write-Host 'Release ZIP (upload manually if needed):' -ForegroundColor Yellow
    Write-Host "    https://github.com/KOOL4/JellyFusionV3/releases/new?tag=$tag"
} else {
    Write-Warning "Remote manifest does not yet show $manifestVersion - raw cache is still warm."
    Write-Warning 'Wait 2-3 more minutes and re-run this check:'
    Write-Warning "    Invoke-WebRequest -UseBasicParsing '$rawUrl' | Select -Expand Content"
}

Write-Host ''
Write-Host '==> DONE' -ForegroundColor Green
