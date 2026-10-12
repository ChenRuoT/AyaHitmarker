[CmdletBinding()]
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
try {
    $prefix = 'BepInEx/plugins/AyaHitmarker/'
    $expected = @('AyaHitmarker.dll', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', '安装说明.txt',
        'Icons/hit.png', 'Icons/skull.png', 'Icons/skull_head.png',
        'Sounds/hit.ogg', 'Sounds/headshot_hit.ogg', 'Sounds/kill.ogg', 'Sounds/headshot_kill.ogg')
    foreach ($tier in @(1,5,10,15,20,25,30,35,40,45,50,55,60,65,70,75)) { $expected += "Icons/RankIcons/Rank$tier.png" }
    $files = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    $names = @($files | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($entry in $files) {
        $name = $entry.FullName.Replace('\', '/')
        if (-not $name.StartsWith($prefix, [StringComparison]::Ordinal) -or $name -match '(^|/)\.\.(/|$)' -or
            $name.Substring($prefix.Length) -cnotin $expected -or $entry.Length -eq 0) { throw "Unexpected/empty package entry: $name" }
    }
    if (@($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Duplicate ZIP entries' }
    foreach ($file in $expected) { if (($prefix + $file) -cnotin $names) { throw "Missing package entry: $file" } }
    Write-Host "Package verified: $($files.Count) files; game-root BepInEx layout; all resources present."
} finally { $zip.Dispose() }
