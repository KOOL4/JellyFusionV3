# ---------------------------------------------------------------
#  JellyFusion V3 v3.0.7  -  Force push + auto-verify
#
#  Run from  D:\Archivos\Descargas\JellyFusion V3\JellyFusion :
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
$tag      = 'v3.0.7'
$userName = 'KOOL4'
$userMail = 'jose13tony13@gmail.com'

# Run git and capture BOTH stdout and stderr so PowerShell shows what
# git actually said when something fails (not just "exit code 128").
# NOTE: $pinfo.ArgumentList only exists on PowerShell 7+ (.NET Core).
#       On Windows PowerShell 5.1 it is $null and calling .Add() throws
#       "No se puede llamar a un método en una expresión con valor NULL".
#       So we build a single Arguments string with manual quoting, which
#       works on both 5.1 and 7+.
function Invoke-Git {
    # Runs git through the PowerShell call operator so PS handles the
    # pipe buffering. The previous ProcessStartInfo version deadlocked on
    # "git add -A" when stderr filled up (one "CRLF will be replaced"
    # warning per file is enough to lock a big repo for minutes).
    #
    # Param naming notes:
    #   - Do NOT name this parameter $Args. $Args is a PowerShell
    #     automatic variable and PS binds by prefix, so "git add -A"
    #     would bind -A as -Args and throw "Falta un argumento para Args".
    [CmdletBinding(PositionalBinding=$false)]
    param(
        [Parameter(Position=0, ValueFromRemainingArguments=$true)]
        [string[]]$GitArgs,
        [switch]$IgnoreError
    )

    if (-not $GitArgs) { $GitArgs = @() }

    # IMPORTANT: with $ErrorActionPreference = 'Stop' (top of script),
    # ANY line git writes to stderr - even harmless "LF will be replaced
    # by CRLF" warnings - becomes a NativeCommandError that kills the
    # script. Save the preference, drop to 'Continue' inside this
    # function, restore in finally.
    $oldEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $code = $null
    try {
        # Redirect stderr to stdout (2>&1) so everything flows through a
        # single PowerShell pipeline - no buffer deadlock, and order is
        # preserved. ErrorRecord lines are printed in gray so you can
        # still tell what git wrote to stderr.
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
Invoke-Git commit -m "JellyFusion v3.0.7 - follow-up over v3.0.6: user reported ONLY the banner rendered, rails/themes/badges/studios were missing, config Save did not reflect on the home page. Root causes identified and fixed: (1) AUTHORIZATION: the [AllowAnonymous] attribute on home/rails, slider/items, studios kept the ASP.NET Core auth pipeline from running, so User.Claims was empty and GetUserId() returned null. In Jellyfin 10.10 an InternalItemsQuery without a user scope returns empty results (even for user-independent rails like top10Movies/newReleases/categories), so bootstrap.js filtered every rail out by items.length===0 and only the banner (which has a user-less fallback broad pull) survived. Fix: no class-level [Authorize], each endpoint gets its own policy (RequiresElevation for config/notifications/admin badge ops, DefaultAuthorization for rails/slider/studios, AllowAnonymous only for theme/bootstrap/i18n), and GetUserId() falls back to the first administrator via _userManager.Users when the claim is absent. HomeService.BuildRailsAsync mirrors the admin fallback. GetNewReleases/GetCategories/LocalTop10 now pass the user into InternalItemsQuery when available. (2) THEME: ThemeService was emitting --jf-primary/--jf-background/--jf-font, but Jellyfin's own CSS never references those variables, so clicking a theme was a no-op visually. Now we also override Jellyfin's real CSS variables (--primary-color, --background-color, --theme-primary-color, --accent) plus !important overrides on body, .skinHeader, .button-submit, .emby-button.raised.button-submit and :hover so the repaint is immediate instead of ghosting. (3) BADGES: BadgeMiddleware had two silent bugs - upstream Content-Length was not cleared before writing the composited buffer (browsers truncated or rejected) and early-return paths did not restore Response.Body (Kestrel wrote fallback bytes to a disposed MemoryStream). Now a try/finally guarantees restoration, Content-Length is removed before WriteImageResponse, the regex requires exactly 32 hex chars (matches both GUID formats), and a Debug log fires on entry so admins can confirm the middleware is wired in. (4) CONFIG SAVE: bootstrap.js now calls refreshThemeCss() on every viewshow/hashchange/popstate, bumping the ?v=... on the <link href> so the browser re-fetches - Save in config now actually repaints when the user comes back to home. (5) Version bumps: bootstrap VERSION 3.0.7, manifest, build.ps1, push.ps1, index.html, .github/workflows/release.yml." | Out-Null

$localSha = (& git rev-parse HEAD).Trim()
Write-Host "    Commit SHA: $localSha"

Write-Host "==> Tag $tag (local)" -ForegroundColor Cyan
Invoke-Git tag -f -a $tag -m "JellyFusion v3.0.7 - all-in-one plugin" | Out-Null

Write-Host '==> Adding remote' -ForegroundColor Cyan
Invoke-Git remote add origin $repoUrl | Out-Null

Write-Host '==> Force-pushing main to GitHub' -ForegroundColor Yellow
Invoke-Git push -u --force origin main | Out-Null

# Some Git/GitHub combos reject "git push --force" for tags when the tag
# already exists on the remote. The bulletproof sequence is: delete the
# remote tag first (best effort, ignore if it doesn't exist) and then
# push the fresh one.
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
    # PS 5.1 ConvertFrom-Json chokes on the UTF-8 BOM (EF BB BF) that
    # PowerShell's own Set-Content writes to manifest.json. Strip it
    # before parsing. Also trim any leading whitespace just in case.
    $content = $raw.Content
    if ($content -and $content.Length -gt 0) {
        $content = $content.TrimStart([char]0xFEFF).TrimStart()
    }
    $json = $content | ConvertFrom-Json
    $versions = @($json)[0].versions | ForEach-Object { $_.version }
    Write-Host "    Versions in remote manifest: $($versions -join ', ')"
    if ($versions -contains '3.0.7.0') { $ok = $true }
} catch {
    Write-Warning "Fetch failed: $_"
}

Write-Host ''
if ($ok) {
    Write-Host '==> SUCCESS - v3.0.7 is LIVE on GitHub' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Add this URL to Jellyfin > Dashboard > Repositories:' -ForegroundColor Yellow
    Write-Host "    $rawUrl"
    Write-Host ''
    Write-Host 'Release ZIP (use .\release.ps1 or upload manually):' -ForegroundColor Yellow
    Write-Host '    https://github.com/KOOL4/JellyFusionV3/releases/new?tag=v3.0.7'
} else {
    Write-Warning 'Remote manifest does not yet show v3.0.7.0 - raw cache is still warm.'
    Write-Warning 'Wait 2-3 more minutes and re-run this check:'
    Write-Warning "    Invoke-WebRequest -UseBasicParsing '$rawUrl' | Select -Expand Content"
}

Write-Host ''
Write-Host '==> DONE' -ForegroundColor Green

