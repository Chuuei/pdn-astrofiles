#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/PdnAstroFiles/PdnAstroFiles.csproj'
$version = dotnet msbuild $project -nologo -getProperty:Version
if ($LASTEXITCODE -ne 0) { throw 'Could not read the plugin version from MSBuild.' }
$version = ($version | Out-String).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Plugin version '$version' must have the form major.minor.patch."
}
$version
