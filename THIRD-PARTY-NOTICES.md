# Third-party components

AstroFiles is licensed under Apache 2.0. Its release ZIP includes these runtime
components and their license texts in `AstroFiles/licenses/`:

| Component | License | Source |
| --- | --- | --- |
| XISFLib.Net | Apache 2.0 | https://github.com/Chuuei/XISFLib.Net |
| K4os.Compression.LZ4 1.3.8 | MIT | https://github.com/MiloszKrajewski/K4os.Compression.LZ4 |
| Original LZ4 codec | BSD 2-Clause | https://github.com/lz4/lz4 |
| ZstdSharp.Port 0.8.8 | MIT | https://github.com/oleg-st/ZstdSharp |
| Original Zstandard codec | BSD 3-Clause | https://github.com/facebook/zstd |

Stretch detection and Bayer reconstruction follow
[SkyBrowser](https://github.com/Chuuei/SkyBrowser) algorithms (Apache 2.0),
adapted for the Paint.NET image pipeline. Attribution is retained in `NOTICE`.

The exact XISFLib revision and Paint.NET reference download are pinned in
`build/dependencies.json`. XISFLib supplies the upstream runtime license texts
and notices copied into the package by `build/New-ReleasePackage.ps1`.

Paint.NET belongs to dotPDN LLC and Rick Brewster. It is downloaded only for
compilation and integration testing; its assemblies are excluded from releases.
Install Paint.NET separately from https://www.getpaint.net/.
The .NET SDK, test tooling, .NET runtime, and Windows Desktop runtime are also
excluded from the plugin package. XISFLib's System.Security.Cryptography.Xml
and Pkcs dependencies resolve from the host's Windows Desktop runtime for this
plugin target. The package retains the upstream .NET license notices as well.
