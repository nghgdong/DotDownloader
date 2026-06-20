<#
.SYNOPSIS
  Publish DotDownloader (self-contained) -> bundle FFmpeg + extension -> (optional) SIGN -> zip.
  One command for the whole publish step.

.EXAMPLE
  ./publish.ps1                                            # publish + bundle + zip (no signing)
  ./publish.ps1 -DevSelfSigned                             # + sign with a dev self-signed cert
  ./publish.ps1 -Pfx C:\certs\dotdl.pfx -Password '***'   # + sign with a real cert
  ./publish.ps1 -NoZip                                     # skip the zip
#>
param(
    [string]$Output = "dist",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,   # default is self-contained (target needs no .NET install)
    [switch]$NoZip,

    # Signing (forwarded to sign.ps1). Leave empty to skip signing.
    [switch]$DevSelfSigned,
    [string]$Pfx,
    [string]$Password,
    [string]$Thumbprint
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Stop a running instance so files are not locked.
Get-Process DM.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$sc = if ($FrameworkDependent) { "false" } else { "true" }
Write-Host "Publish (self-contained=$sc, runtime=$Runtime) -> $Output" -ForegroundColor Cyan
dotnet publish (Join-Path $root "DM.App\DM.App.csproj") -c Release -r $Runtime --self-contained $sc -o $Output -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed." }

# Bundle FFmpeg (if present) + extension.
$ffSrc = Join-Path $root "tools\ffmpeg\ffmpeg.exe"
if (Test-Path $ffSrc) {
    New-Item -ItemType Directory -Force (Join-Path $Output "tools\ffmpeg") | Out-Null
    Copy-Item $ffSrc (Join-Path $Output "tools\ffmpeg\ffmpeg.exe") -Force
    Write-Host "Bundled ffmpeg.exe"
}
else {
    Write-Host "WARNING: tools\ffmpeg\ffmpeg.exe missing - HLS/DASH will need ffmpeg on PATH." -ForegroundColor Yellow
}
Copy-Item (Join-Path $root "extension") (Join-Path $Output "extension") -Recurse -Force
Write-Host "Copied extension"

# Optional signing.
if ($DevSelfSigned -or $Pfx -or $Thumbprint) {
    Write-Host "Signing DotDownloader binaries..." -ForegroundColor Cyan
    $signArgs = @{ Dir = $Output }
    if ($DevSelfSigned) { $signArgs.DevSelfSigned = $true }
    if ($Pfx) { $signArgs.Pfx = $Pfx }
    if ($Password) { $signArgs.Password = $Password }
    if ($Thumbprint) { $signArgs.Thumbprint = $Thumbprint }
    & (Join-Path $root "sign.ps1") @signArgs
}
else {
    Write-Host "Not signing (add -DevSelfSigned / -Pfx / -Thumbprint to sign)." -ForegroundColor DarkGray
}

# Zip.
if (-not $NoZip) {
    $zip = Join-Path $root "DotDownloader-$Runtime.zip"
    Compress-Archive -Path (Join-Path $Output "*") -DestinationPath $zip -CompressionLevel Optimal -Force
    $zmb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "ZIP: $zip - $zmb MB" -ForegroundColor Green
}

$size = [math]::Round((Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "Done. $Output = $size MB" -ForegroundColor Green
