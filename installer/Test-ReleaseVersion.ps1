[CmdletBinding()]
param(
    [string] $RefName,
    [string] $GitHubOutput
)

$ErrorActionPreference = 'Stop'

[xml] $props = Get-Content (Join-Path $PSScriptRoot '..\Directory.Build.props')
$version = [string] $props.Project.PropertyGroup.VersionPrefix
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props contains an invalid VersionPrefix: '$version'."
}

if ($RefName -and $RefName -ne "v$version") {
    throw "Tag '$RefName' does not match product version 'v$version'."
}

if ($GitHubOutput) {
    "version=$version" | Add-Content -Path $GitHubOutput
}

$version
