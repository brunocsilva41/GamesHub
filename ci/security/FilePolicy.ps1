# File-type policy shared by the PR security gate (ci/security/Test-PullRequest.ps1) and the Hygiene stage.
# Dot-source it; it only defines functions and has no side effects.

# Executables, archives, script hosts and shortcuts: never accepted through a PR, anywhere in the tree.
$script:ForbiddenFileRx = '(?i)\.(exe|dll|msi|msix|appx|zip|7z|rar|bin|com|pif|scr|cpl|ps1xml|bat|cmd|vbs|vbe|jse|wsf|wsh|hta|lnk|url|reg|jar|js\.map)$'

# web/ is copied verbatim into the installed app and loaded by WebView2: only UI sources, the top-level
# icon/logo and the bundled font folder belong there.
$script:WebAllowedRx = '(?i)^web/(.+\.(html|css|js|mjs|json|md|svg|txt)|[^/]+\.(png|ico)|fonts/[^/]+\.woff2)$'

<#
.SYNOPSIS Returns why a repository path is not allowed (forbidden type, or unexpected file in web/), or $null.
#>
function Get-FilePolicyViolation([string]$Path) {
    if ($Path -match $script:ForbiddenFileRx) { return 'executables, archives, script hosts and shortcuts must never arrive through a PR' }
    if ($Path -match '^web/' -and $Path -notmatch $script:WebAllowedRx) {
        return 'web/ only holds html/css/js/mjs/json/md/svg/txt, the top-level png/ico and fonts/*.woff2'
    }
    return $null
}
