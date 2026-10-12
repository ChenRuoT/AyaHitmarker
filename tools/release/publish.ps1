[CmdletBinding()]
param([Parameter(Mandatory)][string]$Commit, [string]$RefName = '')
$ErrorActionPreference = 'Stop'
$version = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
$tag = "v$version"
if ($RefName.StartsWith('v') -and $RefName -ne $tag) { throw "Tag $RefName does not match $tag" }
$releases = gh api --paginate --slurp "repos/$env:GITHUB_REPOSITORY/releases?per_page=100"
if ($LASTEXITCODE -ne 0) { throw 'Cannot list releases' }
$existing = @($releases | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object tag_name -eq $tag)
if ($existing.Count -gt 0 -and -not $existing[0].draft) { Write-Host "$tag is already published; skipping."; exit 0 }

$tagRefs = gh api "repos/$env:GITHUB_REPOSITORY/git/matching-refs/tags/$tag" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect release tag' }
$tagRef = @($tagRefs | Where-Object ref -eq "refs/tags/$tag")
if ($tagRef.Count -gt 0) {
    $object = $tagRef[0].object
    while ($object.type -eq 'tag') {
        $object = (gh api "repos/$env:GITHUB_REPOSITORY/git/tags/$($object.sha)" | ConvertFrom-Json).object
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve annotated tag' }
    }
    if ($object.type -ne 'commit' -or $object.sha -ne $Commit) { throw "Existing $tag points to a different commit; refusing to publish mismatched binaries." }
}

$notes = Join-Path $PSScriptRoot "../../docs/releases/$version.md"
if (-not (Test-Path -LiteralPath $notes)) { throw "Missing release notes: $notes" }
if ($existing.Count -eq 0) {
    $arguments = @('release','create',$tag,'--repo',$env:GITHUB_REPOSITORY,'--target',$Commit,'--title',"AyaHitmarker $tag — EFT 1.2.0.0 (IL2CPP)",'--notes-file',$notes,'--draft')
    if ($version.Contains('-')) { $arguments += '--prerelease' }
    & gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Create draft release failed' }
}
$assets = @(Get-ChildItem (Join-Path $PSScriptRoot '../../artifacts/release') -File | Where-Object { $_.Name -match '\.zip(\.sha256)?$' } | ForEach-Object FullName)
if ($assets.Count -ne 2) { throw 'Expected exactly one ZIP and its SHA256 file' }
gh release upload $tag @assets --repo $env:GITHUB_REPOSITORY --clobber
if ($LASTEXITCODE -ne 0) { throw 'Upload release assets failed' }
gh release edit $tag --repo $env:GITHUB_REPOSITORY --draft=false
if ($LASTEXITCODE -ne 0) { throw 'Publish release failed' }
