[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/release'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$version = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
$dll = Join-Path $root "bin/$Configuration/AyaHitmarker.dll"
if (-not (Test-Path -LiteralPath $dll)) { throw "Build first: $dll is missing" }
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion.Split('+')[0]
if ($actual -ne $version) { throw "DLL version $actual differs from Version.props $version" }
$out = [IO.Path]::GetFullPath($OutputDirectory, $root)
New-Item -ItemType Directory -Path $out -Force | Out-Null
# Unique staging directory avoids stale files and never deletes a caller's output folder.
$stage = Join-Path $out ('stage-' + [Guid]::NewGuid().ToString('N'))
$plugin = Join-Path $stage 'BepInEx/plugins/AyaHitmarker'
try {
    New-Item -ItemType Directory -Path $plugin -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination $plugin
    foreach ($directory in @('Icons', 'Sounds')) {
        Copy-Item -LiteralPath (Join-Path $root "ReleaseAssets/$directory") -Destination $plugin -Recurse
    }
    foreach ($file in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $plugin
    }
    Copy-Item -LiteralPath (Join-Path $root 'docs/INSTALL.txt') -Destination (Join-Path $plugin '安装说明.txt')
    $zip = Join-Path $out "AyaHitmarker-v$version-EFT-1.2.0.0.zip"
    Compress-Archive -LiteralPath (Join-Path $stage 'BepInEx') -DestinationPath $zip -Force
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -Archive $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding utf8NoBOM
    Write-Host "Package ready: $zip"
} finally {
    # Verify the resolved target is the staging directory inside our output before recursive removal.
    $resolved = [IO.Path]::GetFullPath($stage)
    if ($resolved.StartsWith($out.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -match '^stage-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
