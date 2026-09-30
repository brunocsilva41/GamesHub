#Requires -Version 7
<#
.SYNOPSIS Signs the installer's checksum manifest for the in-app updater: writes <Manifest>.sig (Base64 of the ECDSA
  P-256 / SHA-256 signature, IEEE P1363 r||s, over the manifest's exact bytes) and verifies it against the public key
  embedded in the app (src/GamesHub/Update/UpdateSigningKey.cs) before anything is published.
  The private key (PKCS#8, Base64) comes ONLY from the GAMESHUB_UPDATE_SIGNING_KEY environment variable and is never
  printed. Missing secret, wrong key or a manifest that does not name the installer: the script fails.
.EXAMPLE
  $env:GAMESHUB_UPDATE_SIGNING_KEY = Get-Content ../.local/update-signing.key -Raw
  ./ci/release/Sign-UpdateManifest.ps1 -Manifest dist/package/GamesHub-Setup-2.1.0.exe.sha256
#>
param(
    [Parameter(Mandatory)][string]$Manifest,
    [string]$PublicKeyFile = (Join-Path $PSScriptRoot '..' '..' 'src' 'GamesHub' 'Update' 'UpdateSigningKey.cs')
)
$ErrorActionPreference = 'Stop'

$secret = $env:GAMESHUB_UPDATE_SIGNING_KEY
if ([string]::IsNullOrWhiteSpace($secret)) {
    throw 'GAMESHUB_UPDATE_SIGNING_KEY is not set: updates cannot be published unsigned (see docs/PIPELINE.md).'
}
if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) { throw "Manifest not found: $Manifest" }

# The manifest must list the installer that sits next to it, by its bare file name (what the app checks).
$installer = [IO.Path]::GetFileNameWithoutExtension($Manifest)
$bytes = [IO.File]::ReadAllBytes($Manifest)
$text = [Text.Encoding]::UTF8.GetString($bytes)
if ($text -notmatch "(?m)^[0-9a-f]{64} [ *]$([regex]::Escape($installer))\s*$") { throw "Manifest does not list $installer" }
$installerPath = Join-Path (Split-Path $Manifest -Parent) $installer
if (Test-Path -LiteralPath $installerPath) {
    $actual = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $text.StartsWith($actual)) { throw "Manifest hash does not match $installer" }
}

# Embedded public key (what installed apps trust).
$cs = Get-Content -LiteralPath $PublicKeyFile -Raw
$x = [regex]::Match($cs, 'const string X = "([A-Za-z0-9+/=]+)"').Groups[1].Value
$y = [regex]::Match($cs, 'const string Y = "([A-Za-z0-9+/=]+)"').Groups[1].Value
$keyId = [regex]::Match($cs, 'const string KeyId = "([0-9a-f]+)"').Groups[1].Value
if (-not $x -or -not $y) { throw "Public key not found in $PublicKeyFile" }
$pub = [Security.Cryptography.ECParameters]::new()
$pub.Curve = [Security.Cryptography.ECCurve+NamedCurves]::nistP256
$point = [Security.Cryptography.ECPoint]::new()
$point.X = [Convert]::FromBase64String($x)
$point.Y = [Convert]::FromBase64String($y)
$pub.Q = $point

$signer = [Security.Cryptography.ECDsa]::Create()
$verifier = [Security.Cryptography.ECDsa]::Create($pub)
try {
    $read = 0
    try { $signer.ImportPkcs8PrivateKey([Convert]::FromBase64String($secret.Trim()), [ref]$read) }
    catch { throw 'GAMESHUB_UPDATE_SIGNING_KEY is not a Base64 PKCS#8 private key.' }
    if ($signer.KeySize -ne 256) { throw 'GAMESHUB_UPDATE_SIGNING_KEY is not an ECDSA P-256 key.' }

    $sig = $signer.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256)   # IEEE P1363 (r||s)
    if (-not $verifier.VerifyData($bytes, $sig, [Security.Cryptography.HashAlgorithmName]::SHA256)) {
        throw "The signing secret does not match the public key embedded in the app ($keyId). Installed apps would reject this update."
    }
    $sigFile = "$Manifest.sig"
    [IO.File]::WriteAllText($sigFile, [Convert]::ToBase64String($sig) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Signed $(Split-Path $Manifest -Leaf) -> $(Split-Path $sigFile -Leaf) (key $keyId, verified with the embedded public key)"
} finally {
    $signer.Dispose()
    $verifier.Dispose()
}
