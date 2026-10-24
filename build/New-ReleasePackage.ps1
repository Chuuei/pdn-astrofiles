#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/PdnAstroFiles/PdnAstroFiles.csproj'
$version = & (Join-Path $PSScriptRoot 'Get-PluginVersion.ps1')
$targetPath = dotnet msbuild $project -nologo -p:Configuration=Release -getProperty:TargetPath
if ($LASTEXITCODE -ne 0) { throw 'Could not find the Release build output.' }
$targetPath = ($targetPath | Out-String).Trim()
if (!(Test-Path -LiteralPath $targetPath)) { throw 'Build the solution in Release configuration first.' }
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($targetPath).Version
if ($assemblyVersion.ToString() -ne "$version.0") {
    throw "Built plugin version $assemblyVersion does not match project version $version. Rebuild first."
}
$output = Split-Path $targetPath -Parent
$depsPath = Join-Path $output 'PdnAstroFiles.deps.json'
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -AsHashtable
$runtime = $deps.targets[$deps.runtimeTarget.name]
if (!$runtime.ContainsKey("PdnAstroFiles/$version")) { throw 'The dependency manifest has a stale plugin version.' }

$staging = Join-Path $root "artifacts/package-$([Guid]::NewGuid().ToString('N'))/AstroFiles"
$licenses = Join-Path $staging 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null

# Take only runtime assemblies declared by the plugin, never every DLL in a test/host directory.
$runtimeFiles = foreach ($library in $runtime.Values) {
    if ($library.ContainsKey('runtimeTargets') -or $library.ContainsKey('native')) {
        throw 'New native or RID-specific dependencies require an explicit packaging update.'
    }
    if ($library.ContainsKey('runtime')) {
        foreach ($asset in $library.runtime.Keys) {
            $name = [IO.Path]::GetFileName($asset)
            if ($name -notlike '*.dll' -or $name -match '^(PaintDotNet\.|testhost|xunit\.|Microsoft\.Test)') {
                throw "Unexpected runtime asset: $asset"
            }
            $name
        }
    }
}
foreach ($name in ($runtimeFiles | Sort-Object -Unique)) {
    Copy-Item -LiteralPath (Join-Path $output $name) -Destination $staging
}
Copy-Item -LiteralPath $depsPath -Destination $staging
foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $staging
}

$xisfRoot = Join-Path $root 'artifacts/XISFLib.Net'
Copy-Item -LiteralPath (Join-Path $xisfRoot 'LICENSE') -Destination (Join-Path $licenses 'LICENSE-XISFLib.txt')
Copy-Item -LiteralPath (Join-Path $xisfRoot 'NOTICE') -Destination (Join-Path $licenses 'NOTICE-XISFLib.txt')
foreach ($name in @('LICENSE-K4os.Compression.LZ4.txt', 'LICENSE-LZ4.txt', 'LICENSE-ZstdSharp.txt',
    'LICENSE-Zstandard.txt', 'LICENSE-DotNet.txt', 'DotNet-THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $xisfRoot "Notices/$name") -Destination $licenses
}

$releaseDirectory = Join-Path $root 'artifacts/release'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$zip = Join-Path $releaseDirectory "AstroFiles-$version.zip"
Compress-Archive -LiteralPath $staging -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $([IO.Path]::GetFileName($zip))" -Encoding utf8

# Verify the archive itself, including required decoder dependencies and notices.
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries.FullName.Replace('\', '/'))
    foreach ($name in @('PdnAstroFiles.dll', 'PdnAstroFiles.Imaging.dll', 'XISFLib.dll',
        'K4os.Compression.LZ4.dll', 'ZstdSharp.dll', 'PdnAstroFiles.deps.json', 'LICENSE',
        'NOTICE', 'licenses/LICENSE-XISFLib.txt', 'licenses/LICENSE-LZ4.txt')) {
        if ($entries -notcontains "AstroFiles/$name") { throw "Release archive is missing $name." }
    }
    if ($entries | Where-Object { $_ -match '(?i)/(paintdotnet[^/]*\.dll|testhost[^/]*|xunit[^/]*|[^/]*\.pdb)$' }) {
        throw 'A host assembly, test dependency, or debug symbol leaked into the release archive.'
    }
}
finally { $archive.Dispose() }
Write-Host "Created and verified $zip"
