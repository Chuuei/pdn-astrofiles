# pdn-astrofiles

[![Build, check, test, and release](https://github.com/Chuuei/pdn-astrofiles/actions/workflows/ci.yml/badge.svg)](https://github.com/Chuuei/pdn-astrofiles/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

AstroFiles is an astronomical image loading and saving plugin using the new
`PaintDotNet.FileTypes` API introduced in Paint.NET 5.2.

The plugin opens XISF (`.xisf`) and FITS (`.fits`, `.fit`, `.fts`) using XISFLib.
It imports full-resolution grayscale, grayscale-alpha, RGB and RGBA images.
There is no import options dialog. Saving supports FITS and XISF with metadata
preservation or best-effort conversion. Other formats use Paint.NET's own savers.

**Preview compatibility:** this release requires **Paint.NET 5.2 beta build
9772** (`5.200.9772.9330`), which uses .NET 11. Paint.NET 5.1 is incompatible.
CI and host integration tests run on Windows x64. Newer previews are not yet
verified against this build of the plugin.

## Download and install

1. Download `AstroFiles-<version>.zip` from
   [GitHub Releases](https://github.com/Chuuei/pdn-astrofiles/releases).
   Preview releases appear in the full release list.
2. Close Paint.NET. If Windows blocks the download, right-click the ZIP,
   choose **Properties**, select **Unblock**, and apply before extracting it.
3. Extract the ZIP and copy the entire `AstroFiles` folder into `FileTypes`:
   - Classic/portable: `<Paint.NET directory>\FileTypes\AstroFiles`
   - Microsoft Store: `Documents\paint.net App Files\FileTypes\AstroFiles`
4. Restart Paint.NET and open a `.xisf`, `.fits`, `.fit`, or `.fts` file.

Keep every DLL and `PdnAstroFiles.deps.json` together. To update, replace the
previous `AstroFiles` folder while Paint.NET is closed. Remove the folder to
uninstall. The ZIP's `.sha256` asset can be checked with `Get-FileHash`.

For FITS/XISF exports, review the **Image geometry unchanged** option described
below. Import stretching and edits are baked into exported pixels; keep your
original scientific files.

## Automatic display

Each image follows this priority:

1. Retain the exposure of data detected as already developed.
2. For linear data, apply the stored XISF `DisplayFunction` when present.
3. Otherwise, apply SkyBrowser's dark-sky stretch with a brighter background
   target of **0.20**, shadows
   clipping **−2.8**, and **linked RGB channels**.

The detection and high-pedestal handling follow
`SkyBrowser.Core/Imaging/SkyImageFinisher.cs`. At most 65,536 samples per channel
are measured, excluding transparent and non-finite samples. A robust median
below 0.18, or a robust standard deviation below 10% of the median, identifies
linear data when the dispersion is greater than 1e-10. A high additive pedestal
is recentered before automatic stretching. Constant images retain their exposure.
This is a heuristic; brightness statistics cannot establish linearity with certainty.

The stretch affects color channels and leaves alpha unchanged. It performs no
cosmetic processing. Source files and scientific sample buffers remain unchanged.
FITS scaling (`BSCALE`, `BZERO`, `BLANK`) is applied by XISFLib before analysis.
FITS third axes of length 3 or 4 are interpreted as RGB or RGBA.

Single-channel Bayer mosaics are automatically reconstructed to full-resolution
RGB before stretch detection. The four standard patterns (`RGGB`, `BGGR`,
`GRBG`, `GBRG`) are supported using native XISF CFA metadata, or FITS `BAYERPAT`
with Bayer offsets and `ROWORDER`. Native CFA metadata takes precedence.
Missing or unsupported metadata leaves the image grayscale; RGB/RGBA images
ignore stale Bayer metadata. Bilinear interpolation follows SkyBrowser's
debayering, and a stored grayscale display function applies to all three
reconstructed colors. A mosaic's grayscale ICC profile is replaced with sRGB.

Images enter Paint.NET as straight-alpha RGBA floating-point pixels with an
explicit ICC/sRGB color context. Paint.NET handles conversion to its canvas
format. The importer avoids an intermediate 8-bit display buffer and preserves
XISF display orientation and resolution.

Multiple images/HDU images become named layers on a canvas large enough for
all of them, aligned at the top left. The first image is visible; the remaining
layers start hidden. All images must use the same ICC profile. Scientific
volumes, complex/binary128 samples and color spaces beyond grayscale/RGB report
an unsupported-format error. Monolithic XISF
files are supported; external sidecar resolution is unavailable through the
host's input stream.

## Saving and metadata

Choose any supported target format in **Save As**. For PNG, JPEG and other
ordinary image formats, Paint.NET uses its own save options and retains only
information supported by that format. AstroFiles does not block these exports.

For FITS or XISF, the normal Paint.NET **Save Configuration** dialog contains an unchecked
**Image geometry unchanged** checkbox:

- Leave it unchecked after cropping, resizing, rotating, flipping or distorting
  the image. WCS, SIP distortion coefficients and recognized XISF astrometric
  solution properties are removed.
- Check it to retain the original astrometry after edits that leave pixel
  positions unchanged, such as color or contrast adjustments.
- A detected canvas size change disables the checkbox. Equal dimensions alone
  cannot detect a flip, rotation, crop followed by resizing, or a distortion.
  The checkbox is an explicit statement about these operations and is not
  remembered as the default for another save dialog.

Target, observation time, exposure, telescope, camera and other acquisition
metadata are retained regardless of the checkbox. Native XISF properties,
global metadata, custom XML and binary attachments are preserved. FITS headers,
comments/history and non-image extensions are retained. Already developed input
keeps this metadata through editing; stretched input also retains all compatible
metadata. Headers describing the original acquisition remain acquisition history.

Saving to the original astronomy format preserves the most metadata. For
**XISF → FITS** and **FITS → XISF**, AstroFiles uses the same XISFLib conversion
APIs and explicit `ConversionLossPolicy.Allow` as the Avalonia viewer. Common
acquisition properties are also mapped between native XISF properties and FITS
keywords: target, observation times, exposure, telescope, camera, filter, focal
length, binning, sensor temperature, pixel size, gain and target coordinates.
Unit conversions are applied where needed, including meters/millimeters for
focal length. Existing explicit FITS keywords take precedence.

Compatible FITS WCS is retained only when geometry is confirmed unchanged.
Native XISF astrometric solutions are not synthesized into FITS WCS. XISF
orientation is baked into pixels when converting to FITS, and coordinate
solutions are discarded in that case. Unsupported metadata may be omitted;
conversion omissions are recorded in output `HISTORY`. FITS tables are converted
where supported, with the library's documented table-storage limitations.

Output uses normalized **Float32** samples, preserving color and alpha without
an intermediate 8-bit conversion. Import stretching, debayering and subsequent
Paint.NET edits are baked into the saved pixels. Old thumbnails, display
functions, pixel statistics, photometric units and pedestal are removed or
updated; active Bayer metadata is removed after RGB reconstruction. Structural
information is regenerated, and FITS checksums are recomputed. Processing history
records these changes. The importer recognizes its saved display-referred pixels
and does not automatically stretch them again. The original raw sample values
and their precision cannot be recovered from the edited canvas.

For a single source image, saving exports the visible composite, including added
editing layers. For multiple source images, each original layer is saved as its
own image/HDU, including hidden layers and their individual metadata. Original
image order is retained. Empty import padding is removed when the original
canvas size is unchanged. Adding, deleting or merging layers, or changing layer
opacity/blending, in a multi-image document requires saving as PDN instead.

A pixel-free metadata template is kept in Paint.NET's custom document metadata,
which survives native **PDN** saves. Other formats such as PNG/JPEG do not retain
this private template. If the template is absent (for example, after reopening
a PNG), FITS/XISF export still works using the visible composite and supported
canvas information. Original astronomy metadata cannot be recovered in that case.
If metadata preservation fails or its non-image payload exceeds 32 MiB, loading
and exporting still work; the save options explain that original metadata is
unavailable.
Original digital signatures are not copied to edited output.

## Build

Requirements: Windows, a .NET 11 SDK (currently a preview), and the assemblies
from [Paint.NET 5.2 beta build 9772](https://github.com/paintdotnet/release/releases/tag/v5.2-beta.5.200.9772.9330).
The plugin targets `net11.0-windows` to match that beta. The original 5.2 alpha
used .NET 10; this plugin targets the newer beta and does not run in Paint.NET
5.1.12.

Install the exact SDK from `global.json`, PowerShell 7, Git, and a .NET 10 runtime
for the portable imaging tests. Initialize the pinned dependencies and build:

```powershell
./build/Initialize-Dependencies.ps1
dotnet restore pdn-astrofiles.slnx
dotnet build pdn-astrofiles.slnx -c Release
```

Setup fetches a sparse XISFLib checkout at the revision in
`build/dependencies.json` and checksum-verifies the official Paint.NET x64
portable ZIP. Both live under ignored `artifacts/`; no neighboring repository
or installed Paint.NET is needed. Repeating setup reuses these dependencies.
Paint.NET's assemblies are build/test references and are never redistributed
in the plugin ZIP. You can also extract the pinned portable release manually
into `artifacts/paintdotnet/`.

If no local reference directory exists, the project looks in
`C:\Program Files\paint.net`. To specify a different preview directory:

```powershell
dotnet build pdn-astrofiles.slnx -c Release '-p:PaintDotNetPath=D:\Apps\paint.net'
```

The decoder uses `artifacts/XISFLib.Net/XISFLib/XISFLib.csproj`.
Override its location to develop against your own checkout:

```powershell
dotnet build pdn-astrofiles.slnx -c Release '-p:XisfLibraryRoot=D:\Libraries\XISFLib.Net'
```

The output is `src/PdnAstroFiles/bin/Release/net11.0-windows/`. It contains the
plugin, imaging component, XISFLib, their runtime dependencies, and the plugin's
`.deps.json` dependency manifest. Paint.NET's own assemblies are excluded.

## Package from source

After a successful Release build and tests:

```powershell
./build/New-ReleasePackage.ps1
```

The script creates and verifies `artifacts/release/AstroFiles-<version>.zip`
and its SHA-256 checksum. The ZIP contains an installable `AstroFiles` folder,
runtime dependencies, documentation, and license texts. Its version comes from
the plugin's MSBuild `Version` property; stale compiled versions fail packaging.
Builds do not automatically install the plugin.

GitHub Actions restores and audits dependencies, compiles with analyzers and
warnings as errors, checks formatting/style, runs both test suites, and verifies
the package. Successful `main` builds automatically publish a prerelease when
the plugin version has no existing `v<version>` tag. Pull requests run the same
checks without publishing. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow.

## Development and tests

`PdnAstroFiles.Imaging` is the portable .NET 10 decoding/stretch/saving component.
`PdnAstroFiles` adapts its rows to Paint.NET's document and bitmap interfaces.
The test suite exercises decoding, stretch priority, dark/high-pedestal linear
data, Bayer metadata and reconstruction, alpha, byte order, channel layout,
orientation, stream ownership, metadata-preserving saves, cross-format conversion
and geometry changes.

```powershell
dotnet test tests/PdnAstroFiles.Imaging.Tests -c Release
```

The Windows plugin integration tests use Paint.NET's actual preview document
factory. They verify that imported FITS/XISF layers and their bitmap data remain
usable after local import handles are disposed. They also exercise edited saves,
8-bit canvas conversion, per-image metadata, the actual Paint.NET PNG saver and
the save configuration UI.
They require the preview's
runtime dependencies alongside its reference assemblies; the portable release
contains these. Host DLLs are copied into test output only.

```powershell
dotnet test tests/PdnAstroFiles.Plugin.Tests -c Release
```

`AstroFileTypeFactory.CreateFileTypes(IFileTypeHost)` is the discovery entry
point. It passes the host into each `FileType`, which creates a separate
`FileTypeLoader` through `OnCreateLoader()`.

`XisfFileTypeLoader.OnLoad` and `FitsFileTypeLoader.OnLoad` receive an
`IFileTypeLoadContext` containing the input stream and document factory.
`AstronomyDocument` creates layers and copies rows into locked host bitmaps.
`AstronomyFileTypeSaver` supplies the save options and delegates metadata-aware
pixel export to `AstronomyDocumentSaver` and `AstronomyImageWriter`.

See the [5.2 announcement](https://forums.paint.net/topic/134381-paintnet-52-alpha-build-9625/)
and [preview file type API reference](https://paint.net/doc/api/preview/api/PaintDotNet.FileTypes.html).

## Support and license

Report bugs and feature requests in
[GitHub Issues](https://github.com/Chuuei/pdn-astrofiles/issues), including the
plugin and Paint.NET versions, reproduction steps, and error text. A small
synthetic image helps reproduce file-specific problems.

AstroFiles is licensed under [Apache 2.0](LICENSE). See [NOTICE](NOTICE) and
[third-party notices](THIRD-PARTY-NOTICES.md) for XISFLib, SkyBrowser algorithm
attribution, and the runtime dependencies included in release ZIPs.
