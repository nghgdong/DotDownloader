<#
.SYNOPSIS
  Authenticode-sign ONLY DotDownloader's own binaries. Does NOT sign ffmpeg, the .NET
  runtime, or third-party libs (MonoTorrent, CommunityToolkit) - those have their own signatures.

.DESCRIPTION
  Finds signtool (Windows SDK) and signs our exe/dll with an RFC3161 timestamp.
  Cert source: -Pfx (a .pfx file), -Thumbprint (cert in store), or -DevSelfSigned (auto self-signed for dev).

.EXAMPLE
  ./sign.ps1 -Dir dist -DevSelfSigned
  ./sign.ps1 -Dir dist -Pfx C:\certs\dotdl.pfx -Password '***'
  ./sign.ps1 -Dir dist -Thumbprint A1B2C3...
#>
param(
    [string]$Dir = "dist",
    # Only DotDownloader assemblies - never ffmpeg/runtime/third-party.
    [string[]]$Files = @("DM.App.exe", "DM.App.dll", "DM.Core.dll", "DM.Server.dll"),
    [string]$Pfx,
    [string]$Password,
    [string]$Thumbprint,
    [switch]$DevSelfSigned,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Resolve-SignTool {
    $candidates = @()
    $candidates += Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue
    $candidates += Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe" -ErrorAction SilentlyContinue
    $best = $candidates | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if ($best) { return $best }
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "signtool.exe not found. Install 'Windows SDK Signing Tools' (winget install Microsoft.WindowsSDK) or Visual Studio."
}

function Get-DevCertThumbprint {
    $subject = "CN=DotDownloader Dev"
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
    if (-not $cert) {
        Write-Host "Creating self-signed code-signing cert: $subject" -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature `
            -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
        # Trust it on THIS machine (only useful when Smart App Control is OFF).
        $src = Get-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
        foreach ($s in @("Root", "TrustedPublisher")) {
            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($s, "CurrentUser")
            $store.Open("ReadWrite"); $store.Add($src); $store.Close()
        }
        Write-Host "Installed cert into CurrentUser Trusted Root + Trusted Publishers." -ForegroundColor Yellow
    }
    return $cert.Thumbprint
}

$signtool = Resolve-SignTool
Write-Host "signtool: $signtool"

$idArgs = @()
if ($Pfx) {
    $idArgs += @("/f", $Pfx)
    if ($Password) { $idArgs += @("/p", $Password) }
}
elseif ($Thumbprint) {
    $idArgs += @("/sha1", $Thumbprint)
}
elseif ($DevSelfSigned) {
    $idArgs += @("/sha1", (Get-DevCertThumbprint))
}
else {
    throw "Provide one of: -Pfx <file>, -Thumbprint <hex>, or -DevSelfSigned."
}

$targets = @()
foreach ($f in $Files) {
    $p = Join-Path $Dir $f
    if (Test-Path $p) { $targets += $p } else { Write-Host "Skip (missing): $p" -ForegroundColor DarkGray }
}
if ($targets.Count -eq 0) { throw "No files to sign in '$Dir'." }

Write-Host "Signing $($targets.Count) file(s)..." -ForegroundColor Cyan
$signArgs = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256") + $idArgs + $targets
& $signtool @signArgs
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed (exit $LASTEXITCODE)." }

Write-Host "Verifying..." -ForegroundColor Cyan
foreach ($t in $targets) {
    & $signtool verify /pa /q $t
    $ok = if ($LASTEXITCODE -eq 0) { "OK" } else { "UNTRUSTED (self-signed / cert not installed?)" }
    Write-Host ("  {0,-40} {1}" -f (Split-Path $t -Leaf), $ok)
}
Write-Host "Done." -ForegroundColor Green
