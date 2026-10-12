[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
[xml]$props = Get-Content (Join-Path $PSScriptRoot '../../Version.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$') { throw "Invalid release version: $version" }
$version
