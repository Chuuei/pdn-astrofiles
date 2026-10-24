#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $root 'artifacts'
$downloads = Join-Path $artifacts 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null

function Get-VerifiedArchive([string] $Url, [string] $Path, [string] $Sha256) {
    if (!(Test-Path -LiteralPath $Path)) {
        Write-Host "Downloading $Url"
        Invoke-WebRequest -Uri $Url -OutFile $Path
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Checksum mismatch for $Path. Remove this archive and retry."
    }
}

$xisfRoot = Join-Path $artifacts 'XISFLib.Net'
$revisionFile = Join-Path $xisfRoot '.astrofiles-revision'
if (Test-Path -LiteralPath $xisfRoot) {
    if (!(Test-Path -LiteralPath $revisionFile) -or
        (Get-Content -LiteralPath $revisionFile -Raw).Trim() -ne $lock.xisf.revision) {
        throw "The XISFLib cache is from a different revision. Remove $xisfRoot and retry."
    }
}
else {
    # Sparse checkout avoids downloading the upstream example photographs and viewers.
    git init $xisfRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the XISFLib source cache.' }
    git -C $xisfRoot remote add origin "$($lock.xisf.repository).git"
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the XISFLib source remote.' }
    git -C $xisfRoot sparse-checkout set XISFLib Notices
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the sparse XISFLib checkout.' }
    git -C $xisfRoot fetch --filter=blob:none --depth=1 origin $lock.xisf.revision
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the pinned XISFLib revision.' }
    git -C $xisfRoot -c advice.detachedHead=false checkout --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the pinned XISFLib revision.' }
    Set-Content -LiteralPath $revisionFile -Value $lock.xisf.revision -Encoding utf8
}
$actualRevision = git -C $xisfRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $lock.xisf.revision) {
    throw 'The XISFLib checkout does not match the pinned source revision.'
}

$paintDotNetRoot = Join-Path $artifacts 'paintdotnet'
$paintDotNetAssembly = Join-Path $paintDotNetRoot 'PaintDotNet.FileTypes.dll'
if (Test-Path -LiteralPath $paintDotNetAssembly) {
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($paintDotNetAssembly).FileVersion
    if ($actualVersion -ne $lock.paintDotNet.version) {
        throw "Expected Paint.NET $($lock.paintDotNet.version), found $actualVersion. Remove $paintDotNetRoot and retry."
    }
}
else {
    $archive = Join-Path $downloads "paint.net.$($lock.paintDotNet.version).portable.x64.zip"
    Get-VerifiedArchive $lock.paintDotNet.url $archive $lock.paintDotNet.sha256
    Expand-Archive -LiteralPath $archive -DestinationPath $paintDotNetRoot
    if (!(Test-Path -LiteralPath $paintDotNetAssembly)) {
        throw 'The Paint.NET archive does not contain the expected reference assemblies.'
    }
}
Write-Host 'Pinned build dependencies are ready.'
