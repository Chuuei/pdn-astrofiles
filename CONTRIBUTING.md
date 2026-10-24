# Contributing

Use Windows x64, PowerShell 7, the SDK pinned in `global.json`, and a .NET 10
runtime for imaging tests. See [README.md](README.md) for host compatibility.

```powershell
./build/Initialize-Dependencies.ps1
dotnet restore pdn-astrofiles.slnx
dotnet build pdn-astrofiles.slnx -c Release --no-restore
dotnet format pdn-astrofiles.slnx --verify-no-changes --no-restore --include src tests
dotnet test pdn-astrofiles.slnx -c Release --no-build --no-restore
./build/New-ReleasePackage.ps1
```

The first command downloads checksum-verified Paint.NET references and a pinned
sparse XISFLib checkout into the ignored `artifacts/` directory. No neighboring
checkout or installed copy of Paint.NET is needed. The script caches downloads;
remove a stale dependency directory before changing its pinned revision.
To work on XISFLib itself, pass `-p:XisfLibraryRoot=...` to build/test commands.

Keep warnings at zero. CI checks compiler and SDK analyzer diagnostics, NuGet
security advisories (including transitive dependencies), formatting, the portable
imaging tests, and the real Paint.NET host integration tests. Add regression
coverage when changing import/export behavior. Tests generate their own images;
avoid committing personal photographs, downloaded host binaries, or build output.

Commit updated `packages.lock.json` files when changing NuGet dependencies;
CI restores in locked mode to verify dependency versions and content hashes.

Open an issue with the Paint.NET version, AstroFiles version, reproduction steps,
and any error text. Share a small synthetic FITS/XISF example where possible.
Submit changes through a pull request. Contributions are under Apache 2.0.

## Releases

Set `<Version>` in `src/PdnAstroFiles/PdnAstroFiles.csproj` and update
`CHANGELOG.md`. That MSBuild property is the single source for plugin version,
ZIP name, and GitHub tag (`v<Version>`). Do not move or reuse a published tag.

After all checks pass on a push to `main`, CI publishes a GitHub prerelease for
any version without an existing tag. It attaches the installable ZIP and its
SHA-256 checksum. Prereleases reflect the required Paint.NET beta host. Existing
versions are skipped. Pull requests never publish releases. A manual run on
`main` can retry a failed publication; delete an incomplete draft release first
if GitHub created one without its assets. Review the release before promoting
it to a stable release once stable Paint.NET compatibility has been verified.
