#Requires -Version 7
<#
.SYNOPSIS Stage 5 — builds the single-file installer and verifies it without executing it:
  checksum file, version info of every executable, the embedded payload (byte-identical to the build),
  payload contents (required files present, nothing unexpected, web files identical to the repo, pinned DLLs),
  referenced assemblies allowlist, size budgets and Authenticode status.
#>
param([switch]$NoBuild)
. (Join-Path $PSScriptRoot '..' 'lib' 'Common.ps1')
Start-Stage 'Package'
$root = Get-RepoRoot
Push-Location $root
try {
    $version = Get-AppVersion
    $core4 = ($version -replace '-.*$', '') + '.0'
    if (-not $NoBuild) {
        if (-not (Invoke-Checked 'build installer (build.ps1 -Strict -Package)' { pwsh -NoProfile -File build.ps1 -Clean -Strict -Package })) {
            exit (Complete-Stage)
        }
    }

    $setup = Join-Path $root "dist/package/GamesHub-Setup-$version.exe"
    $payload = Join-Path $root 'dist/obj/payload.zip'
    Add-Check "installer exists: GamesHub-Setup-$version.exe" (Test-Path $setup)
    Add-Check 'payload.zip exists' (Test-Path $payload)
    if (-not (Test-Path $setup) -or -not (Test-Path $payload)) { exit (Complete-Stage) }

    # ---- checksum file (sha256sum format, used by the updater and by users)
    $hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $shaFile = "$setup.sha256"
    $shaText = if (Test-Path $shaFile) { (Get-Content $shaFile -Raw).Trim() } else { '' }
    Add-Check '.sha256 matches the installer' ($shaText -eq "$hash  $(Split-Path $setup -Leaf)") $hash

    # ---- size budgets
    $setupSize = (Get-Item $setup).Length
    Add-Check 'installer size budget (1–6 MB)' ($setupSize -gt 1MB -and $setupSize -le 6MB) ("{0:N1} MB" -f ($setupSize / 1MB))

    # ---- version info of the executables
    $vi = (Get-Item $setup).VersionInfo
    Add-Check 'installer FileVersion' ($vi.FileVersion -eq $core4) $vi.FileVersion
    Add-Check 'installer ProductVersion' ($vi.ProductVersion -eq $version) $vi.ProductVersion
    Add-Check 'installer ProductName' ($vi.ProductName -eq 'GamesHub') $vi.ProductName
    Add-Check 'installer CompanyName set' ([bool]$vi.CompanyName) $vi.CompanyName

    # ---- inspect the installer assembly in Windows PowerShell (.NET Framework can reflection-load it)
    $probe = @'
param($setup)
$a = [Reflection.Assembly]::ReflectionOnlyLoadFrom($setup)
$s = $a.GetManifestResourceStream('GamesHub.Payload.zip')
$h = if ($s) { $sha = [Security.Cryptography.SHA256]::Create(); ([BitConverter]::ToString($sha.ComputeHash($s)) -replace '-','').ToLower() } else { '' }
[pscustomobject]@{ Resources = $a.GetManifestResourceNames(); PayloadHash = $h;
  Refs = @($a.GetReferencedAssemblies() | ForEach-Object Name) } | ConvertTo-Json -Compress
'@
    $probeFile = Join-Path ([IO.Path]::GetTempPath()) 'gh-probe-setup.ps1'
    Set-Content $probeFile $probe -Encoding utf8
    $info = powershell.exe -NoProfile -ExecutionPolicy Bypass -File $probeFile $setup | ConvertFrom-Json
    $payloadHash = (Get-FileHash $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    Add-Check 'installer embeds GamesHub.Payload.zip' ($info.Resources -contains 'GamesHub.Payload.zip') ($info.Resources -join ', ')
    Add-Check 'embedded payload is byte-identical to the build' ($info.PayloadHash -eq $payloadHash) $payloadHash.Substring(0, 16)
    $allowedRefs = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.IO.Compression',
                   'System.IO.Compression.FileSystem', 'System.Web.Extensions', 'System.Xml', 'System.Xml.Linq', 'Microsoft.CSharp'
    $extra = @($info.Refs | Where-Object { $allowedRefs -notcontains $_ })
    Add-Check 'installer references only .NET Framework' ($extra.Count -eq 0) (($extra -join ', ') ?? '')

    # ---- payload contents
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($payload)
    try {
        $entries = @{}
        foreach ($e in $zip.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            $name = $e.FullName -replace '\\', '/'
            $sha = [Security.Cryptography.SHA256]::Create()
            $st = $e.Open(); try { $entries[$name] = ([BitConverter]::ToString($sha.ComputeHash($st)) -replace '-', '').ToLower() } finally { $st.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    Add-Check 'payload file count' ($entries.Count -gt 20) "$($entries.Count) files"

    $requiredEntries = 'GamesHub.exe', 'Uninstall.exe', 'GamesHub.exe.config', 'Microsoft.Web.WebView2.Core.dll',
        'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'WebView2-LICENSE.txt', 'gamehub-app.ico',
        'web/index.html', 'web/quick/index.html', 'web/js/main.js', 'web/css/tokens.css', 'web/fonts/outfit-var.woff2'
    foreach ($r in $requiredEntries) { Add-Check "payload contains $r" ($entries.ContainsKey($r)) }

    $forbiddenRx = '\.(pdb|tmp|log|map|bak)$|Tests?\.exe$|(^|/)(tests?|ci|docs|src)/|(^|/)\.|README\.md$'
    $bad = @($entries.Keys | Where-Object { $_ -match $forbiddenRx })
    Add-Check 'payload has no dev/test files' ($bad.Count -eq 0) ($bad -join ', ')

    # web/ in the payload must be exactly the tracked web/ (no stale or missing files)
    $repoWeb = @{}
    foreach ($f in (Get-TrackedFiles | Where-Object { $_ -like 'web/*' -and $_ -notlike '*.md' })) {
        $repoWeb[$f] = (Get-FileHash (Join-Path $root $f) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $payloadWeb = @($entries.Keys | Where-Object { $_ -like 'web/*' })
    $missing = @($repoWeb.Keys | Where-Object { -not $entries.ContainsKey($_) })
    $stale = @($payloadWeb | Where-Object { -not $repoWeb.ContainsKey($_) })
    $diff = @($repoWeb.Keys | Where-Object { $entries.ContainsKey($_) -and $entries[$_] -ne $repoWeb[$_] })
    Add-Check 'payload web/ == repository web/' (($missing.Count + $stale.Count + $diff.Count) -eq 0) `
        "missing: $($missing.Count), unexpected: $($stale.Count), different: $($diff.Count) $((@($missing) + @($stale) + @($diff) | Select-Object -First 5) -join ', ')"

    # third-party DLLs must be the pinned ones
    foreach ($line in Get-Content 'lib/checksums.sha256') {
        if ($line -match '^([0-9a-f]{64}) \*?(.+)$') {
            $n = $Matches[2].Trim()
            Add-Check "payload $n matches pinned hash" ($entries[$n] -eq $Matches[1])
        }
    }

    # the app exe in the payload is the one that was built and tested
    $appHash = (Get-FileHash 'dist/app/GamesHub.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
    Add-Check 'payload GamesHub.exe == tested build' ($entries['GamesHub.exe'] -eq $appHash)
    foreach ($exe in 'dist/app/GamesHub.exe', 'dist/app/Uninstall.exe') {
        $v = (Get-Item $exe).VersionInfo
        Add-Check "$(Split-Path $exe -Leaf) FileVersion" ($v.FileVersion -eq $core4) $v.FileVersion
    }

    # ---- Authenticode (required only when the release is configured to sign)
    foreach ($exe in $setup, (Join-Path $root 'dist/app/GamesHub.exe')) {
        $sig = Get-AuthenticodeSignature $exe
        $mustSign = $env:GAMESHUB_REQUIRE_SIGNATURE -eq '1'
        Add-Check "Authenticode: $(Split-Path $exe -Leaf)" ($sig.Status -eq 'Valid') "$($sig.Status)$(if (-not $mustSign) { ' (signing not configured)' })" -Warning:(-not $mustSign)
    }
}
finally { Pop-Location }
exit (Complete-Stage)
