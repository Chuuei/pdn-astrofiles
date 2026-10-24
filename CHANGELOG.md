# Changelog

## 0.5.0

- Automatically finish linear FITS/XISF imports with conservative gradient
  reduction, neutral sky backgrounds, noise smoothing, gentle sharpening,
  and a small saturation boost.
- Keep full-resolution floating-point color, exact alpha, original geometry,
  and untouched source samples. Retain stored display functions and developed
  images without additional cosmetic processing.
- Record cosmetic processing in preserved document metadata and export history;
  saved display-referred images are not processed again when reopened.
- Preserve grayscale-alpha Bayer mosaic coverage during reconstruction and finishing.

## 0.4.0

First public AstroFiles preview for Paint.NET 5.2 beta build 9772.

- Load and save FITS and XISF with floating-point color and alpha.
- Automatically display linear data and reconstruct supported Bayer mosaics.
- Preserve astronomy metadata with explicit control over astrometry after edits.
- Import multiple images as separate Paint.NET layers.
- Provide reproducible builds, automated code checks and tests, and versioned
  release packages with dependency notices and installation instructions.
